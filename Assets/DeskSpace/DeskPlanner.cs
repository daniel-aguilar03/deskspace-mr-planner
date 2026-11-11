using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>One measured desk with a live session anchor. No layout or anchor persistence.</summary>
public sealed class DeskPlanner : MonoBehaviour
{
    public HandRay left, right;
    public ARAnchorManager anchors;
    public ARShaderOcclusion occlusion;
    public AROcclusionManager depth;
    public ARSession session;
    public Material[] objectMaterials;
    public Material validPreview, invalidPreview, shadowMaterial;
    public Light keyLight;
    public RectTransform panel;
    public Text info, status, selection, occlusionLabel;
    public GameObject setupRoot, layoutRoot, setupPrimary, setupUndo, setupReset;
    public Text setupTitle, setupBody, setupFeedback, setupPrimaryLabel, setupProgress;
    public Image[] setupDots;
    public Mesh markerMesh;
    public Material markerMaterial;
    public bool SetupStarted { get; private set; }
    public int SetupPointCount => corners.Count;
    bool helpOpen;
    GameObject guideRoot, aimDot;
    readonly List<GameObject> markedPoints = new();
    LineRenderer setupOutline;

    public const string SessionNotice = "SESSION ONLY\nResets when app closes";
    readonly List<DeskItem> items = new();
    readonly List<Vector3> corners = new();
    ARAnchor desk;
    Vector2 deskSize;
    DeskItem selected, moving;
    GameObject preview;
    int placingKind = -1;
    float placingYaw, confirmClearAt = -10f;
    bool busy, panelPlaced, validPlacement;
    Vector3 candidate;
    LineRenderer boundary;
    string note = "Select BEGIN SETUP. You will mark three points on your real desktop.";
    float nextReadout, fpsWindow, fps;
    int fpsFrames;

    void Start()
    {
        SceneLook.visible = false;
        Shader.SetGlobalFloat("_OcclusionBiasNear", .008f);
        Shader.SetGlobalFloat("_OcclusionBiasScale", .008f);
        RefreshSetup();
    }
    void Update()
    {
        fpsFrames++; fpsWindow += Time.unscaledDeltaTime;
        if (fpsWindow >= .5f) { fps = fpsFrames / fpsWindow; fpsFrames = 0; fpsWindow = 0; }
        var head = Camera.main.transform;
        if (!panelPlaced && head.position.y > .3f) { RecenterPanel(); panelPlaced = true; }
        if (Time.unscaledTime >= nextReadout)
        {
        nextReadout = Time.unscaledTime + .1f;
        info.text = desk ? $"YOUR DESK / {deskSize.x * 100:0} × {deskSize.y * 100:0} cm\n{items.Count}/8 · 1:1 scale · {fps:0} fps" :
            $"SET UP DESK / corner {Mathf.Min(corners.Count + 1, 3)} of 3\nFront-left → front-right → back edge";
        selection.text = preview ? $"PLACE / {DeskCatalog.Names[placingKind]}\nTurn ±15° with the buttons. Trigger on desk to confirm; Cancel to keep the old layout." :
            selected ? $"SELECTED / {DeskCatalog.Names[selected.kind]}\n{DeskCatalog.Sizes[selected.kind].x * 100:0.#} W × {DeskCatalog.Sizes[selected.kind].z * 100:0.#} D × {DeskCatalog.Sizes[selected.kind].y * 100:0.#} H cm" : "Select an object with the ray + trigger to move, rotate or remove it.";
        string tracking = session && ARSession.state != ARSessionState.SessionTracking ? "TRACKING / " + ARSession.state + "\n" : "";
        status.text = tracking + (busy ? "Please wait—updating spatial anchor..." : note);
        occlusionLabel.text = "Occlusion: " + occlusion.occlusionShaderMode;
        RefreshSetup();
        }
        if (right.input == null || left.input == null) return;
        if (left.input.secondary.WasPressedThisFrame()) RecenterPanel();
        if (right.input.secondary.WasPressedThisFrame()) panel.gameObject.SetActive(!panel.gameObject.activeSelf);
        if (busy) return;
        UpdateSetupAim();
        UpdatePreview();
        foreach (var hand in new[] { right, left })
        {
            if (!hand.input.select.WasPressedThisFrame()) continue;
            if (hand.hitCollider && hand.hitCollider.GetComponentInParent<WorldButton>() is WorldButton button) { button.Press(); continue; }
            if (hand.hitCollider && hand.hitCollider.transform.IsChildOf(panel)) continue;
            if (helpOpen) continue;
            if (ARSession.state != ARSessionState.SessionTracking || (desk && desk.trackingState != TrackingState.Tracking))
            { note = "Tracking is not ready. Look around the room and wait before placing or moving objects."; continue; }
            if (!desk) { if (SetupStarted && hand == right && corners.Count < 3) MarkCorner(hand); continue; }
            if (preview) { if (hand == right) ConfirmPlacement(); continue; }
            selected = hand.hitCollider ? hand.hitCollider.GetComponentInParent<DeskItem>() : null;
            if (selected) note = "Object selected. Move keeps its old position until you confirm a valid new one.";
        }
    }
    public void RecenterPanel()
    {
        panel.gameObject.SetActive(true);
        var h = Camera.main.transform; var f = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
        if (f.sqrMagnitude < .01f) f = Vector3.forward;
        panel.position = h.position + f * .95f - Vector3.Cross(Vector3.up, f) * .45f + Vector3.down * .08f;
        panel.rotation = Quaternion.LookRotation(panel.position - h.position);
    }
    void MarkCorner(HandRay hand)
    {
        RecordSetupPoint(hand.hasHit,hand.point,hand.normal);
    }
    public bool RecordSetupPoint(bool hasHit, Vector3 point, Vector3 normal)
    {
        if (!SetupStarted || busy || desk || helpOpen) return false;
        if (!DeskSetupGuide.CanMark(corners,hasHit,point,normal,out string reason)) { note = reason; return false; }
        corners.Add(point);
        note = corners.Count == 1 ? "Point 1 marked. Now mark the other end of the near edge." : corners.Count == 2 ?
            "Point 2 marked. Now mark the far edge, across the desktop." : "Three points marked. Check the green outline before confirming.";
        RedrawSetup(); RefreshSetup();
        return true;
    }
    public void SetupAction()
    {
        if (busy) return;
        if (helpOpen) { helpOpen = false; RefreshSetup(); return; }
        if (desk) return;
        if (!SetupStarted) { SetupStarted = true; note = "Aim at near-left: the corner closest to you, on your left."; }
        else if (corners.Count == 3) CreateDesk();
        RefreshSetup();
    }
    public void ShowSetupHelp() { helpOpen = true; panel.gameObject.SetActive(true); RefreshSetup(); }
    public void UndoSetupPoint()
    {
        if (busy || desk || corners.Count == 0) return;
        corners.RemoveAt(corners.Count - 1); note = "Last point removed. Mark it again with the right ray and index trigger.";
        RedrawSetup(); RefreshSetup();
    }
    public void ResetSetupPoints()
    {
        if (busy || desk) return;
        corners.Clear(); SetupStarted = true; note = "Points reset. Start again with the near-left corner.";
        RedrawSetup(); RefreshSetup();
    }
    void RefreshSetup()
    {
        if (!setupRoot) return;
        bool visible = !desk || helpOpen;
        setupRoot.SetActive(visible); layoutRoot.SetActive(!visible);
        if (guideRoot) guideRoot.SetActive(!desk && SetupStarted && !helpOpen);
        if (!visible) return;
        bool intro = !SetupStarted || helpOpen;
        setupTitle.text = intro ? "SET UP YOUR REAL DESK" : DeskSetupGuide.Title(corners.Count);
        setupBody.text = intro ? DeskSetupGuide.Intro : DeskSetupGuide.Body(corners.Count);
        setupProgress.text = helpOpen ? "HELP / Your layout and points are unchanged" : $"{corners.Count}/3 points marked / right ray + index trigger";
        setupFeedback.text = busy ? "Please wait. Creating the session's spatial anchor..." : !PermissionScene() ?
            "Allow spatial data in the Quest permission prompt. Then complete Space Setup if your desktop is not detected." :
            ARSession.state != ARSessionState.SessionTracking ? "Tracking is not ready. Look around your room and wait, keeping your boundary clear." : note;
        setupPrimary.SetActive(!busy && (intro || corners.Count == 3));
        setupPrimaryLabel.text = helpOpen ? (desk ? "BACK TO LAYOUT" : "BACK TO SETUP") : !SetupStarted ? "BEGIN SETUP" : "USE THIS DESK";
        setupUndo.SetActive(!busy && !intro && corners.Count > 0);
        setupReset.SetActive(!busy && !intro && corners.Count > 0);
        for (int i = 0; i < setupDots.Length; i++) setupDots[i].color = !intro && i < corners.Count ? new Color(.16f,.7f,.46f) :
            (intro || i == corners.Count ? new Color(.96f,.77f,.40f) : new Color(.28f,.34f,.36f));
    }
    GameObject GuideDot(string name, Vector3 position, float scale, Material material)
    {
        var dot = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); dot.transform.SetParent(guideRoot.transform, false);
        dot.GetComponent<MeshFilter>().sharedMesh = markerMesh; dot.GetComponent<MeshRenderer>().sharedMaterial = material;
        dot.transform.position = position; dot.transform.localScale = Vector3.one * scale; return dot;
    }
    void EnsureGuides()
    {
        if (guideRoot) return;
        guideRoot = new GameObject("Desk setup / measured points only"); guideRoot.transform.SetParent(transform, false);
        aimDot = GuideDot("Right pointer / candidate", Vector3.zero, .022f, validPreview);
        setupOutline = new GameObject("Measured edge / review outline").AddComponent<LineRenderer>();
        setupOutline.transform.SetParent(guideRoot.transform, false); setupOutline.useWorldSpace = true;
        setupOutline.widthMultiplier = .004f; setupOutline.sharedMaterial = markerMaterial;
    }
    void RedrawSetup()
    {
        EnsureGuides();
        foreach (var point in markedPoints) Destroy(point); markedPoints.Clear();
        foreach (var p in corners) markedPoints.Add(GuideDot("Marked desktop point", p + Vector3.up * .015f, .03f, markerMaterial));
        setupOutline.loop = false; setupOutline.positionCount = 0;
        if (corners.Count == 2) { setupOutline.positionCount = 2; setupOutline.SetPositions(corners.Select(p => p + Vector3.up * .015f).ToArray()); }
        if (corners.Count == 3 && DeskGeometry.TryDesk(corners[0],corners[1],corners[2],out var pose,out var size))
        {
            setupOutline.loop = true; setupOutline.positionCount = 4;
            var local = DeskGeometry.Corners(Vector2.zero,size,0);
            for (int i = 0; i < 4; i++) setupOutline.SetPosition(i,pose.position + pose.rotation * new Vector3(local[i].x,.015f,local[i].y));
            note = $"Measured {size.x * 100:0} cm wide by {size.y * 100:0} cm deep. Check the rectangle, then USE THIS DESK.";
        }
    }
    void UpdateSetupAim()
    {
        if (!SetupStarted || desk || helpOpen || corners.Count >= 3) { if (aimDot) aimDot.SetActive(false); return; }
        EnsureGuides();
        bool onPanel = right.hitCollider && right.hitCollider.transform.IsChildOf(panel);
        bool show = right.hasHit && !onPanel && ARSession.state == ARSessionState.SessionTracking;
        aimDot.SetActive(show);
        if (!show) return;
        aimDot.transform.position = right.point + Vector3.up * .015f;
        bool valid = DeskSetupGuide.CanMark(corners,true,right.point,right.normal,out _);
        aimDot.GetComponent<MeshRenderer>().sharedMaterial = valid ? validPreview : invalidPreview;
    }
    async void CreateDesk()
    {
        if (!DeskGeometry.TryDesk(corners[0], corners[1], corners[2], out var pose, out var size))
        { corners.Clear(); note = "Those points do not form a flat desk. Start again with the near-left corner."; RedrawSetup(); RefreshSetup(); return; }
        busy = true;
        try
        {
            var result = await anchors.TryAddAnchorAsync(pose);
            if (!result.status.IsSuccess()) { note = "Anchor creation failed. Check spatial-data permission, scan the room, then try again."; corners.Clear(); return; }
            desk = result.value; deskSize = size; DrawBoundary();
            if (guideRoot) guideRoot.SetActive(false);
            note = "Desk anchored for this session. Choose an accessory, aim inside the outline, then trigger to place.";
        }
        catch (Exception e) { note = "Anchor unavailable: " + e.GetType().Name; corners.Clear(); }
        finally { busy = false; if (!desk) RedrawSetup(); RefreshSetup(); }
    }
    public void Add(int kind)
    {
        if (busy) return;
        if (!desk) { note = "Mark the three real desk points first."; return; }
        if (items.Count >= 8) { note = "Eight-object limit keeps the scene readable and lightweight. Remove an item first."; return; }
        Cancel(); placingKind = kind; placingYaw = 0;
        preview = DeskCatalog.Create(kind, objectMaterials); preview.GetComponent<BoxCollider>().enabled = false;
        preview.SetActive(false); note = "Aim the right ray at the desk. Green fits; red is outside or overlaps another preview.";
    }
    public void MoveSelected()
    {
        if (busy || !selected) { note = "Select an existing accessory first."; return; }
        var keep = selected; Cancel(); placingKind = keep.kind;
        preview = DeskCatalog.Create(placingKind, objectMaterials); preview.GetComponent<BoxCollider>().enabled = false;
        preview.SetActive(false);
        moving = keep; placingYaw = keep.Yaw;
        keep.gameObject.SetActive(false);
    }
    void UpdatePreview()
    {
        if (!preview || !desk) return;
        if (right.hitCollider && right.hitCollider.transform.IsChildOf(panel)) { preview.SetActive(false); validPlacement = false; return; }
        var plane = new Plane(desk.transform.up, desk.transform.position);
        if (!plane.Raycast(right.ray, out float d) || d < .1f || d > 3f) { preview.SetActive(false); validPlacement = false; return; }
        candidate = desk.transform.InverseTransformPoint(right.ray.GetPoint(d)); candidate.y = 0;
        validPlacement = ValidPose(placingKind, new Vector2(candidate.x, candidate.z), placingYaw, moving);
        preview.SetActive(true); preview.transform.SetPositionAndRotation(desk.transform.TransformPoint(candidate), desk.transform.rotation * Quaternion.Euler(0, placingYaw, 0));
        foreach (var r in preview.GetComponentsInChildren<Renderer>()) r.sharedMaterial = validPlacement ? validPreview : invalidPreview;
    }
    bool ValidPose(int kind, Vector2 center, float yaw, DeskItem ignore = null)
    {
        var size = DeskCatalog.Sizes[kind];
        if (!DeskGeometry.Fits(deskSize, center, new Vector2(size.x, size.z), yaw)) return false;
        foreach (var i in items) if (i != ignore && DeskGeometry.Overlaps(center, new Vector2(size.x, size.z), yaw, i.Center, i.Footprint, i.Yaw)) return false;
        return true;
    }
    void ConfirmPlacement()
    {
        if (!validPlacement || !preview.activeSelf) { note = "That position is outside the measured desk or overlaps another accessory. Choose a green position."; return; }
        var item = moving;
        if (!item) { item = DeskCatalog.Create(placingKind, objectMaterials).GetComponent<DeskItem>(); items.Add(item); }
        item.transform.SetParent(desk.transform, false); item.transform.localPosition = candidate;
        item.transform.localRotation = Quaternion.Euler(0, placingYaw, 0); item.gameObject.SetActive(true);
        moving = null; selected = item; Destroy(preview); preview = null; placingKind = -1;
        note = "Placed at life size. Select to edit, or choose another object. Layout lasts until the app closes.";
    }
    public void Cancel()
    {
        if (busy) return;
        if (moving) moving.gameObject.SetActive(true);
        moving = null; if (preview) Destroy(preview); preview = null; placingKind = -1;
    }
    public void RotateLeft() => Rotate(-15);
    public void RotateRight() => Rotate(15);
    void Rotate(float amount)
    {
        if (busy) return;
        if (preview) { placingYaw = Mathf.Repeat(placingYaw + amount, 360); return; }
        if (!selected) { note = "Select an item or choose an accessory to place first."; return; }
        float yaw = Mathf.Repeat(selected.Yaw + amount, 360);
        if (!ValidPose(selected.kind, selected.Center, yaw, selected)) { note = "Rotation blocked: it would leave the desk or overlap another item."; return; }
        selected.transform.localRotation = Quaternion.Euler(0, yaw, 0);
    }
    public void RemoveSelected()
    {
        if (busy) return; Cancel();
        if (!selected) { note = "Select an item first."; return; }
        items.Remove(selected); Destroy(selected.gameObject); selected = null; note = "Accessory removed from this session's layout.";
    }
    public void CycleOcclusion()
    {
        if (busy) return;
        occlusion.occlusionShaderMode = occlusion.occlusionShaderMode == AROcclusionShaderMode.None ? AROcclusionShaderMode.HardOcclusion :
            occlusion.occlusionShaderMode == AROcclusionShaderMode.HardOcclusion ? AROcclusionShaderMode.SoftOcclusion : AROcclusionShaderMode.None;
        if (PermissionScene()) depth.enabled = occlusion.occlusionShaderMode != AROcclusionShaderMode.None;
        note = "Compare your real hand or another real object in front of an accessory. Depth edges are approximate.";
    }
    static bool PermissionScene() => Application.isEditor || UnityEngine.Android.Permission.HasUserAuthorizedPermission(PermissionGate.Scene);
    int lightingMode;
    public void Lighting()
    {
        lightingMode = (lightingMode + 1) % 3;
        keyLight.intensity = new[] { .8f, 1.2f, .5f }[lightingMode];
        keyLight.color = lightingMode == 2 ? new Color(1f, .82f, .64f) : Color.white;
        RenderSettings.ambientLight = new Color(.5f, .52f, .55f) * (lightingMode == 2 ? .8f : 1f);
        note = "Lighting preset: " + new[] { "soft daylight", "bright room", "warm room" }[lightingMode] + ". Manual visual match, not automatic light estimation.";
    }
    public void NewDesk()
    {
        if (busy) return;
        if (desk && Time.time - confirmClearAt > 3f)
        { confirmClearAt = Time.time; note = "Clear this session's desk and objects? Press New desk again within 3 seconds to confirm."; return; }
        Cancel(); busy = true;
        try
        {
            if (desk && !anchors.TryRemoveAnchor(desk)) { note = "Desk could not be cleared. Current layout is unchanged; try again."; return; }
            foreach (var i in items) if (i) Destroy(i.gameObject);
            items.Clear(); desk = null; selected = null; corners.Clear(); confirmClearAt = -10f;
            SetupStarted = false; helpOpen = false; RedrawSetup();
            note = "Session layout cleared. Select BEGIN SETUP to measure a new desk.";
        }
        catch (Exception e) { note = "Reset could not finish: " + e.GetType().Name; }
        finally { busy = false; RefreshSetup(); }
    }
    void DrawBoundary()
    {
        boundary = new GameObject("Measured desk outline").AddComponent<LineRenderer>(); boundary.transform.SetParent(desk.transform, false);
        boundary.useWorldSpace = false; boundary.loop = true; boundary.positionCount = 4; boundary.widthMultiplier = .003f;
        boundary.sharedMaterial = validPreview;
        var p = DeskGeometry.Corners(Vector2.zero, deskSize, 0);
        for (int i = 0; i < 4; i++) boundary.SetPosition(i, new Vector3(p[i].x, .003f, p[i].y));
        var catcher = GameObject.CreatePrimitive(PrimitiveType.Quad); catcher.name = "Real desk shadow receiver";
        Destroy(catcher.GetComponent<Collider>()); catcher.transform.SetParent(desk.transform, false);
        catcher.transform.localPosition = new Vector3(0, .002f, 0); catcher.transform.localRotation = Quaternion.Euler(90, 0, 0);
        catcher.transform.localScale = new Vector3(deskSize.x, deskSize.y, 1);
        var r = catcher.GetComponent<Renderer>(); r.sharedMaterial = shadowMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}
