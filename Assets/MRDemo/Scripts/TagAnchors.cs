using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.OpenXR.Features.Meta;
using AT = AprilTag.Interop;

/// <summary>
/// Marker anchors: find printed AprilTags (tagStandard41h12) in the passthrough cameras and put a spatial anchor on each.
///
/// Cheap on purpose:
///   - Only the luma (Y) plane of each camera image is used. It already is the 8-bit grayscale image AprilTag wants:
///     one row-by-row memcpy into AprilTag's buffer, no colour conversion at all.
///   - Once a tag is found, frames search only small windows around where it was. A full scan of the left image
///     (2x quad decimation, ~25 ms) runs every few frames, and whenever nothing is tracked, to pick up new tags.
///     The right image is only ever searched in windows, taken from its own last boxes or from the left ones.
///   - Detection runs on two worker threads, so a new frame can start while the last one finishes.
///
/// Stereo: a tag seen by both front cameras is triangulated (its centre, along two rays 6.4 cm apart) rather than
/// placed by its apparent size in one image, which is where single-camera depth gets noisy.
///
/// Tracking and anchoring are separate: in view, the tag's object follows the tag every frame; out of view, it sits on
/// the tag's anchor. The anchor is re-placed only after the tag has been moved and has settled.
/// </summary>
public class TagAnchors : MonoBehaviour
{
    public ARCameraManager cameraManager;
    public ARAnchorManager anchors;
    public Material tagMaterial, frameMaterial;
    public RawImage previewLeft, previewRight;  // on the panel: both cameras, live
    public float tagSize = 0.10f;             // metres: the white square inside the black ring (printed sheets are 100 mm)
    public int decimate = 2;                  // AprilTag quad_decimate for full scans
    public int threads = 2;                   // AprilTag threads per worker
    public float cameraLatency = 0.035f;      // seconds from exposure to the image being ours (Quest: 20–40 ms)
    public float minMargin = 40f;             // reject weak decodes
    public int fullScanEvery = 8;             // frames between full scans while tags are tracked
    public bool stereo = true;

    // read by the panel
    public bool stereoAvailable { get; private set; }
    public float cameraFps { get; private set; }
    public float detectHz { get; private set; }
    public float windowMs { get; private set; }
    public float fullMs { get; private set; }
    public string jitterText { get; private set; } = "";
    public string status = "tags: waiting for camera";
    public int anchoredCount { get { int n = 0; foreach (var t in tags.Values) if (t.anchor) n++; return n; } }

    struct CamInfo { public double fx, fy, cx, cy; }
    // Managed worker data, never a native ABI struct. A value type here made IL2CPP generate
    // invalid SafeHandle-field marshal wrappers in the Android build, even with markers unused.
    sealed class Crop { public AT.ImageU8 image; public int x, y, cam; public int decimate; }
    struct TagHit { public int id, cam; public Vector3 position; public Quaternion rotation; public float margin; public RectInt box; public Vector2 center; public Vector2[] corners; }
    class Worker
    {
        public AT.Detector detector; public AT.ImageU8 full;
        public Task<List<TagHit>> job; public Pose head; public double stamp; public float ms; public bool isFull;
        public readonly CamInfo[] cams = new CamInfo[2];
    }
    class TagState
    {
        public ARAnchor anchor; public GameObject content; public TextMesh label;
        public Pose tracked, settleFrom; public float settleSince, lastSeen; public bool hasPose, anchoring, stereo;
        public readonly Queue<Vector3> recent = new();
    }

    readonly Worker[] workers = { new Worker(), new Worker() };
    AT.Family family;
    readonly Dictionary<int, TagState> tags = new();
    readonly Dictionary<int, (RectInt box, float time)>[] windows = { new(), new() };   // per camera: left, right
    readonly Dictionary<int, Vector2[]>[] corners = { new(), new() };                    // the tag's outline, per camera
    readonly Pose[] lens = { new Pose(new Vector3(-0.032f, -0.018f, 0.063f), Quaternion.Euler(11, 0, 0)),
                             new Pose(new Vector3(0.032f, -0.018f, 0.063f), Quaternion.Euler(11, 0, 0)) };
    bool lensRead, haveRightLens;
    readonly Queue<(double t, Pose p)> headHistory = new();
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    double lastStamp = -1, newestApplied = -1, fpsStart, rateStart;
    int frameNo, fpsFrames, rateCount;
    Texture2D previewTexL, previewTexR; float nextPreview;

    void Start()
    {
        family = AT.Family.CreateTagStandard41h12();
        foreach (var w in workers)
        {
            w.detector = AT.Detector.Create();
            w.detector.ThreadCount = threads;
            w.detector.QuadDecimate = decimate;
            w.detector.AddFamily(family);
        }
    }

    void OnDestroy()
    {
        foreach (var w in workers) { w.job?.Wait(); w.detector?.RemoveFamily(family); w.detector?.Dispose(); w.full?.Dispose(); }
        family?.Dispose();
    }

    void Update()
    {
        double now = clock.Elapsed.TotalSeconds;
        var cam = Camera.main.transform;
        headHistory.Enqueue((now, new Pose(cam.position, cam.rotation)));
        while (headHistory.Count > 0 && now - headHistory.Peek().t > 0.5) headHistory.Dequeue();

        foreach (var w in workers)
        {
            if (w.job == null || !w.job.IsCompleted) continue;
            if (w.job.Exception != null) Debug.LogException(w.job.Exception);
            else if (w.stamp > newestApplied) { newestApplied = w.stamp; Apply(w, now); }   // never apply an older frame
            w.job = null;
        }
        foreach (var kv in tags) Present(kv.Key, kv.Value);
        if (!PermissionGate.HasCamera) return;
        foreach (var w in workers) if (w.job == null) { StartDetection(now, w); break; }
    }

    // where the head was at time t
    Pose HeadAt(double t)
    {
        Pose prev = default; double tp = 0; bool have = false;
        foreach (var (ht, hp) in headHistory)
        {
            if (ht >= t && have) { float a = (float)((t - tp) / Math.Max(1e-6, ht - tp)); return new Pose(Vector3.Lerp(prev.position, hp.position, a), Quaternion.Slerp(prev.rotation, hp.rotation, a)); }
            prev = hp; tp = ht; have = true;
        }
        return prev;
    }

    // ---------- main thread: grab the newest frame (or stereo pair), copy only what will be searched ----------

    unsafe void StartDetection(double now, Worker worker)
    {
        if (!lensRead) { lensRead = true; haveRightLens = TryReadLensOffsets(lens); }
        var sub = cameraManager.subsystem as MetaOpenXRCameraSubsystem;
        stereoAvailable = sub != null && haveRightLens && sub.SupportsStereoImageCapture();

        XRCpuImage left, right = default; bool hasRight = false;
        if (stereo && stereoAvailable && sub.TryAcquireLatestStereoCpuImagePair(out var pair))
        {
            left = new XRCpuImage(sub.cpuImageApi, pair.leftEyeImageCinfo);
            right = new XRCpuImage(sub.cpuImageApi, pair.rightEyeImageCinfo);
            hasRight = true;
        }
        else if (!cameraManager.TryAcquireLatestCpuImage(out left)) { status = "no camera image"; return; }
        try
        {
            if (left.timestamp == lastStamp) return;   // no new frame yet
            lastStamp = left.timestamp;
            if (++fpsFrames >= 30) { cameraFps = (float)(fpsFrames / (now - fpsStart)); fpsStart = now; fpsFrames = 0; }

            int w = left.width, h = left.height;
            worker.cams[0] = Intrinsics(sub, MetaOpenXRCameraSubsystem.CameraPosition.LeftEye, w, h);
            if (hasRight) worker.cams[1] = Intrinsics(sub, MetaOpenXRCameraSubsystem.CameraPosition.RightEye, w, h);

            var yl = left.GetPlane(0);
            if (Time.time >= nextPreview)
            {
                nextPreview = Time.time + 1f / 30f;
                UpdatePreview(yl, w, h, ref previewTexL, previewLeft);
                if (hasRight) UpdatePreview(right.GetPlane(0), w, h, ref previewTexR, previewRight);
            }

            // left: windows around tracked tags, or a full scan
            var crops = new List<Crop>();
            var recent = Recent(0);
            bool full = recent.Count == 0 || ++frameNo % fullScanEvery == 0;
            if (full)
            {
                if (worker.full == null || worker.full.Width != w || worker.full.Height != h) { worker.full?.Dispose(); worker.full = AT.ImageU8.Create(w, h); }
                fixed (byte* dst = worker.full.Buffer)
                    UnsafeUtility.MemCpyStride(dst, worker.full.Stride, yl.data.GetUnsafeReadOnlyPtr(), yl.rowStride, w, h);
                crops.Add(new Crop { image = worker.full, cam = 0, decimate = decimate });
            }
            else foreach (var b in recent) AddWindow(crops, yl, w, h, Grow(b, 0), 0, b);

            // right: its own last boxes, or the left ones widened toward the left (a near tag shifts left in the right image)
            if (hasRight)
            {
                var yr = right.GetPlane(0);
                var rightBoxes = Recent(1);
                if (rightBoxes.Count > 0) foreach (var b in rightBoxes) AddWindow(crops, yr, w, h, Grow(b, 0), 1, b);
                else foreach (var b in Recent(0)) AddWindow(crops, yr, w, h, Grow(b, 160), 1, b);
            }

            worker.head = HeadAt(now - cameraLatency);
            worker.stamp = left.timestamp;
            worker.isFull = full;
            double size = tagSize; float margin = minMargin;
            worker.job = Task.Run(() => Detect(worker, crops, size, margin));
        }
        finally { left.Dispose(); if (hasRight) right.Dispose(); }
    }

    List<RectInt> Recent(int cam)
    {
        var list = new List<RectInt>();
        foreach (var v in windows[cam].Values) if (Time.time - v.time < 0.2f) list.Add(v.box);
        return list;
    }

    // the last box, grown for motion between frames (and, for the right camera's first look, for disparity)
    static RectInt Grow(RectInt b, int extraLeft)
    {
        int g = Mathf.Max(32, Mathf.Max(b.width, b.height) / 3);
        return new RectInt(b.xMin - g - extraLeft, b.yMin - g, b.width + 2 * g + extraLeft, b.height + 2 * g);
    }

    unsafe void AddWindow(List<Crop> crops, XRCpuImage.Plane plane, int w, int h, RectInt r, int cam, RectInt tagBox)
    {
        int x0 = Mathf.Max(0, r.xMin), y0 = Mathf.Max(0, r.yMin), x1 = Mathf.Min(w, r.xMax), y1 = Mathf.Min(h, r.yMax);
        if (x1 - x0 < 16 || y1 - y0 < 16) return;
        var img = AT.ImageU8.Create(x1 - x0, y1 - y0);
        byte* src = (byte*)plane.data.GetUnsafeReadOnlyPtr() + y0 * plane.rowStride + x0;
        fixed (byte* dst = img.Buffer) UnsafeUtility.MemCpyStride(dst, img.Stride, src, plane.rowStride, x1 - x0, y1 - y0);
        // full resolution only for small (distant) tags; a tag over 120 px wide survives 2x decimation
        crops.Add(new Crop { image = img, x = x0, y = y0, cam = cam, decimate = Mathf.Min(tagBox.width, tagBox.height) >= 120 ? 2 : 1 });
    }

    CamInfo Intrinsics(MetaOpenXRCameraSubsystem sub, MetaOpenXRCameraSubsystem.CameraPosition pos, int w, int h)
    {
        XRCameraIntrinsics k;
        if (sub == null || !sub.TryGetIntrinsicsForPosition(pos, out k)) cameraManager.TryGetIntrinsics(out k);
        // intrinsics are given for the sensor resolution; scale them to this image
        float sx = (float)w / Mathf.Max(1, k.resolution.x), sy = (float)h / Mathf.Max(1, k.resolution.y);
        return new CamInfo { fx = k.focalLength.x * sx, fy = k.focalLength.y * sy, cx = k.principalPoint.x * sx, cy = k.principalPoint.y * sy };
    }

    unsafe void UpdatePreview(XRCpuImage.Plane y, int w, int h, ref Texture2D tex, RawImage target)
    {
        if (!target) return;
        if (!tex || tex.width != w || tex.height != h)
        {
            tex = new Texture2D(w, h, TextureFormat.R8, false) { filterMode = FilterMode.Bilinear };
            target.texture = tex;
        }
        var raw = tex.GetRawTextureData<byte>();
        UnsafeUtility.MemCpyStride(raw.GetUnsafePtr(), w, y.data.GetUnsafeReadOnlyPtr(), y.rowStride, w, h);
        tex.Apply(false);
    }

    // ---------- worker thread: detect in each crop, estimate each tag's pose in its camera ----------

    static List<TagHit> Detect(Worker worker, List<Crop> crops, double size, float minMargin)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var best = new Dictionary<int, TagHit>();
        foreach (var crop in crops)
        {
            var k = worker.cams[crop.cam];
            worker.detector.QuadDecimate = crop.decimate;
            using (var dets = worker.detector.Detect(crop.image))
            for (int i = 0; i < dets.Length; i++)
            {
                ref var d = ref dets[i];
                if (d.Hamming > 0 || d.DecisionMargin < minMargin) continue;
                var info = new AT.DetectionInfo(ref d, size, k.fx, k.fy, k.cx - crop.x, k.cy - crop.y);   // optical centre in crop pixels
                using var pose = new AT.Pose(ref info);
                ref var R = ref pose.R; ref var t = ref pose.t;
                // AprilTag/OpenCV camera: x right, y down, z forward. Unity camera: y up. Flip y.
                var pos = new Vector3((float)t.e0, -(float)t.e1, (float)t.e2);
                var yAxis = new Vector3((float)R.e01, -(float)R.e11, (float)R.e21);   // tag's y (down the printed page)
                var zAxis = new Vector3((float)R.e02, -(float)R.e12, (float)R.e22);   // tag's z (into the paper)
                double minX = Math.Min(Math.Min(d.Corner1.x, d.Corner2.x), Math.Min(d.Corner3.x, d.Corner4.x));
                double maxX = Math.Max(Math.Max(d.Corner1.x, d.Corner2.x), Math.Max(d.Corner3.x, d.Corner4.x));
                double minY = Math.Min(Math.Min(d.Corner1.y, d.Corner2.y), Math.Min(d.Corner3.y, d.Corner4.y));
                double maxY = Math.Max(Math.Max(d.Corner1.y, d.Corner2.y), Math.Max(d.Corner3.y, d.Corner4.y));
                var hit = new TagHit
                {
                    id = d.ID, cam = crop.cam, margin = d.DecisionMargin, position = pos,
                    rotation = Quaternion.LookRotation(-yAxis, -zAxis),   // up = out of the paper, forward = up the page
                    box = new RectInt((int)minX + crop.x, (int)minY + crop.y, (int)(maxX - minX) + 1, (int)(maxY - minY) + 1),
                    center = new Vector2((float)d.Center.x + crop.x, (float)d.Center.y + crop.y),
                    corners = new[] { new Vector2((float)d.Corner1.x + crop.x, (float)d.Corner1.y + crop.y), new Vector2((float)d.Corner2.x + crop.x, (float)d.Corner2.y + crop.y),
                                      new Vector2((float)d.Corner3.x + crop.x, (float)d.Corner3.y + crop.y), new Vector2((float)d.Corner4.x + crop.x, (float)d.Corner4.y + crop.y) },
                };
                int key = d.ID * 2 + crop.cam;
                if (!best.TryGetValue(key, out var prev) || prev.margin < hit.margin) best[key] = hit;
            }
            if (crop.image != worker.full) crop.image.Dispose();
        }
        worker.ms = (float)sw.Elapsed.TotalMilliseconds;
        return new List<TagHit>(best.Values);
    }

    // ---------- main thread: camera poses → world poses; stereo triangulation ----------

    void Apply(Worker w, double now)
    {
        if (w.isFull) fullMs = w.ms; else windowMs = w.ms;
        if (++rateCount >= 10) { detectHz = (float)(rateCount / (now - rateStart)); rateStart = now; rateCount = 0; }

        var camPose = new Pose[2];
        for (int c = 0; c < 2; c++) camPose[c] = new Pose(w.head.position + w.head.rotation * lens[c].position, w.head.rotation * lens[c].rotation);

        var byId = new Dictionary<int, (TagHit? l, TagHit? r)>();
        foreach (var hit in w.job.Result)
        {
            windows[hit.cam][hit.id] = (hit.box, Time.time);
            corners[hit.cam][hit.id] = hit.corners;
            byId.TryGetValue(hit.id, out var e);
            if (hit.cam == 0) e.l = hit; else e.r = hit;
            byId[hit.id] = e;
        }
        foreach (var kv in byId)
        {
            int id = kv.Key; var e = kv.Value;
            var mono = e.l ?? e.r.Value;
            var cp = camPose[mono.cam];
            var world = new Pose(cp.position + cp.rotation * mono.position, cp.rotation * mono.rotation);
            bool usedStereo = false;
            if (e.l.HasValue && e.r.HasValue && TryTriangulate(w, camPose, e.l.Value, e.r.Value, out var p)
                && Vector3.Distance(p, world.position) < 0.15f)   // keep it only if it agrees with one camera to 15 cm
            {
                var rRot = camPose[1].rotation * e.r.Value.rotation;
                world = new Pose(p, Quaternion.Slerp(world.rotation, rRot, 0.5f));
                usedStereo = true;
            }
            Handle(id, world, usedStereo);
        }
    }

    // closest point between the two cameras' rays through the tag's centre
    static bool TryTriangulate(Worker w, Pose[] cam, TagHit l, TagHit r, out Vector3 point)
    {
        Vector3 Ray(int c, Vector2 px)
        {
            var k = w.cams[c];
            return (cam[c].rotation * new Vector3((float)((px.x - k.cx) / k.fx), -(float)((px.y - k.cy) / k.fy), 1f)).normalized;
        }
        Vector3 p1 = cam[0].position, d1 = Ray(0, l.center), p2 = cam[1].position, d2 = Ray(1, r.center);
        float a = Vector3.Dot(d1, d1), b = Vector3.Dot(d1, d2), c2 = Vector3.Dot(d2, d2);
        Vector3 r0 = p1 - p2;
        float d = Vector3.Dot(d1, r0), e = Vector3.Dot(d2, r0), den = a * c2 - b * b;
        point = default;
        if (den < 1e-6f) return false;                       // rays (nearly) parallel
        float s = (b * e - c2 * d) / den, t = (a * e - b * d) / den;
        if (s <= 0 || t <= 0) return false;                  // behind a camera
        point = 0.5f * (p1 + s * d1 + p2 + t * d2);
        return true;
    }

    void Handle(int id, Pose world, bool usedStereo)
    {
        if (!tags.TryGetValue(id, out var s)) { tags[id] = s = new TagState(); s.content = MakeContent(id, out s.label); }
        // light smoothing: follow quickly
        s.tracked = s.hasPose ? new Pose(Vector3.Lerp(s.tracked.position, world.position, 0.6f), Quaternion.Slerp(s.tracked.rotation, world.rotation, 0.6f)) : world;
        if (!s.hasPose) { s.settleFrom = world; s.settleSince = Time.time; }
        s.hasPose = true; s.lastSeen = Time.time; s.stereo = usedStereo;

        // jitter: spread of the raw estimates while the tag is still (what stereo is meant to shrink)
        if (s.recent.Count > 0 && Vector3.Distance(world.position, s.tracked.position) > 0.03f) s.recent.Clear();
        s.recent.Enqueue(world.position); while (s.recent.Count > 30) s.recent.Dequeue();
        if (s.recent.Count >= 10)
        {
            Vector3 mean = Vector3.zero; foreach (var p in s.recent) mean += p; mean /= s.recent.Count;
            float sq = 0; foreach (var p in s.recent) sq += (p - mean).sqrMagnitude;
            jitterText = $"tag {id}: jitter {Mathf.Sqrt(sq / s.recent.Count) * 1000f:0.0} mm ({(usedStereo ? "stereo" : "one camera")})";
        }

        // settled? restart the clock whenever it moves more than 1 cm
        if (Vector3.Distance(s.tracked.position, s.settleFrom.position) > 0.01f || Quaternion.Angle(s.tracked.rotation, s.settleFrom.rotation) > 3f)
        { s.settleFrom = s.tracked; s.settleSince = Time.time; }
        bool settled = Time.time - s.settleSince > 0.5f;
        bool anchorOff = !s.anchor || Vector3.Distance(s.anchor.transform.position, s.tracked.position) > 0.02f
                                   || Quaternion.Angle(s.anchor.transform.rotation, s.tracked.rotation) > 5f;
        if (settled && anchorOff && !s.anchoring) ReAnchor(s, s.tracked);
        status = $"{tags.Count} tag(s)";
    }

    async void ReAnchor(TagState s, Pose pose)
    {
        s.anchoring = true;
        var r = await anchors.TryAddAnchorAsync(pose);
        s.anchoring = false;
        if (!r.status.IsSuccess()) { status = "anchor failed " + r.status; return; }
        var old = s.anchor;
        s.anchor = r.value;
        Axes.Add(s.anchor.transform, 0.06f);
        if (old) anchors.TryRemoveAnchor(old);
    }

    // in view: follow the tag; out of view: sit on the anchor
    void Present(int id, TagState s)
    {
        bool live = Time.time - s.lastSeen < 0.25f;
        var t = s.content.transform;
        if (live) t.SetPositionAndRotation(s.tracked.position, s.tracked.rotation);
        else if (s.anchor) t.SetPositionAndRotation(s.anchor.transform.position, s.anchor.transform.rotation);
        s.label.text = $"tag {id} · " + (live ? (s.stereo ? "tracking (stereo)" : "tracking") : s.anchor ? "on its anchor" : "lost");
    }

    // each camera's current tag outlines (four corners, as fractions of the image, y down from the top), for the panel
    public void GetPreviewQuads(int cam, List<Vector2[]> into)
    {
        into.Clear();
        var tex = cam == 0 ? previewTexL : previewTexR;
        if (!tex) return;
        foreach (var kv in windows[cam])
        {
            if (Time.time - kv.Value.time > 0.2f || !corners[cam].TryGetValue(kv.Key, out var c)) continue;
            var q = new Vector2[4];
            for (int i = 0; i < 4; i++) q[i] = new Vector2(c[i].x / tex.width, c[i].y / tex.height);
            into.Add(q);
        }
    }

    // a frame on the tag's outline and a small labelled object standing on it
    GameObject MakeContent(int id, out TextMesh labelOut)
    {
        var root = new GameObject("Tag " + id);
        float half = tagSize * 0.5f, bar = 0.006f;
        for (int k = 0; k < 4; k++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(b.GetComponent<Collider>());
            b.transform.SetParent(root.transform, false);
            bool alongX = k < 2;
            b.transform.localPosition = alongX ? new Vector3(0, 0, k == 0 ? half : -half) : new Vector3(k == 2 ? half : -half, 0, 0);
            b.transform.localScale = alongX ? new Vector3(tagSize + bar, 0.004f, bar) : new Vector3(bar, 0.004f, tagSize + bar);
            b.GetComponent<Renderer>().sharedMaterial = frameMaterial;
        }
        var shape = GameObject.CreatePrimitive(id % 3 == 0 ? PrimitiveType.Cube : id % 3 == 1 ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
        Destroy(shape.GetComponent<Collider>());
        shape.transform.SetParent(root.transform, false);
        shape.transform.localScale = Vector3.one * 0.05f;
        shape.transform.localPosition = new Vector3(0, 0.05f, 0);
        shape.GetComponent<Renderer>().material = new Material(tagMaterial) { color = Color.HSVToRGB((id * 0.17f) % 1f, 0.7f, 1f) };
        var label = new GameObject("Label").AddComponent<TextMesh>();
        label.transform.SetParent(root.transform, false);
        label.transform.localPosition = new Vector3(0, 0.11f, 0);
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
        label.fontSize = 48; label.characterSize = 0.004f; label.anchor = TextAnchor.MiddleCenter;
        label.gameObject.AddComponent<FaceCamera>();
        labelOut = label;
        return root;
    }

    public void ToggleStereo() { stereo = !stereo; foreach (var t in tags.Values) t.recent.Clear(); windows[1].Clear(); }

    public void ClearAll()
    {
        foreach (var s in tags.Values) { if (s.content) Destroy(s.content); if (s.anchor) anchors.TryRemoveAnchor(s.anchor); }
        tags.Clear(); windows[0].Clear(); windows[1].Clear(); corners[0].Clear(); corners[1].Clear(); jitterText = "";
    }

    // Camera2 says where each passthrough camera sits on the headset (same conversion as Meta's MR Utility Kit).
    // Android lists the cameras only once the camera permission is granted. Returns true if both were found.
    static bool TryReadLensOffsets(Pose[] lens)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            using var manager = activity.Call<AndroidJavaObject>("getSystemService", "camera");
            using var keys = new AndroidJavaClass("android.hardware.camera2.CameraCharacteristics");
            using var keyT = keys.GetStatic<AndroidJavaObject>("LENS_POSE_TRANSLATION");
            using var keyR = keys.GetStatic<AndroidJavaObject>("LENS_POSE_ROTATION");
            var found = new List<(float x, Pose pose)>();
            foreach (var id in manager.Call<string[]>("getCameraIdList"))
            {
                using var ch = manager.Call<AndroidJavaObject>("getCameraCharacteristics", id);
                using var tObj = ch.Call<AndroidJavaObject>("get", keyT);
                using var rObj = ch.Call<AndroidJavaObject>("get", keyR);
                if (tObj == null || rObj == null) continue;
                var t = AndroidJNIHelper.ConvertFromJNIArray<float[]>(tObj.GetRawObject());
                var r = AndroidJNIHelper.ConvertFromJNIArray<float[]>(rObj.GetRawObject());
                if (t.Length < 3 || r.Length < 4) continue;
                found.Add((t[0], new Pose(new Vector3(t[0], t[1], -t[2]),
                    Quaternion.Inverse(new Quaternion(-r[0], -r[1], r[2], r[3])) * Quaternion.Euler(180, 0, 0))));
            }
            found.Sort((a, b) => a.x.CompareTo(b.x));
            if (found.Count > 0) lens[0] = found[0].pose;
            if (found.Count > 1) lens[1] = found[found.Count - 1].pose;
            Debug.Log($"[MR] tags: {found.Count} cameras; left {lens[0].position} right {lens[1].position}");
            return found.Count > 1;
        }
        catch (Exception e) { Debug.LogWarning("[MR] tags: lens pose unavailable: " + e.Message); }
#endif
        return false;
    }
}

/// <summary>Turns a label toward the viewer every frame.</summary>
public class FaceCamera : MonoBehaviour
{
    void LateUpdate() { if (Camera.main) transform.rotation = Quaternion.LookRotation(transform.position - Camera.main.transform.position); }
}

/// <summary>Red, green and blue sticks for an anchor's x, y and z, so you can see where the anchors are.</summary>
public static class Axes
{
    static Material r, g, b;
    public static void Add(Transform parent, float length)
    {
        r ??= Unlit(Color.red); g ??= Unlit(Color.green); b ??= Unlit(new Color(0.2f, 0.4f, 1f));
        Stick(parent, Vector3.right, length, r); Stick(parent, Vector3.up, length, g); Stick(parent, Vector3.forward, length, b);
    }
    static void Stick(Transform parent, Vector3 dir, float len, Material m)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.Destroy(s.GetComponent<Collider>());
        s.transform.SetParent(parent, false);
        s.transform.localPosition = dir * len * 0.5f;
        s.transform.localScale = new Vector3(dir.x != 0 ? len : 0.004f, dir.y != 0 ? len : 0.004f, dir.z != 0 ? len : 0.004f);
        s.GetComponent<Renderer>().sharedMaterial = m;
    }
    static Material Unlit(Color c) { var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c); return m; }
}
