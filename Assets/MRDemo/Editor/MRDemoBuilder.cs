using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Text = UnityEngine.UI.Text;

/// <summary>
/// Builds the whole project from code: project settings (URP, Android, Vulkan, OpenXR with the Meta Quest
/// AR Foundation features), the MRDemo scene, and the APK.
///   Menu: MR Demo > Set Up Project, MR Demo > Build Scene, MR Demo > Build APK
///   Batch: Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod MRDemoBuilder.BuildApk
/// </summary>
public static class MRDemoBuilder
{
    const string Root = "Assets/MRDemo";
    const string ScenePath = Root + "/MRDemo.unity";
    const string ApkPath = "Builds/MRDemo.apk";

    // ---------- project settings ----------

    [MenuItem("MR Demo/Set Up Project")]
    public static void SetUpProject()
    {
        // URP, tuned for a mobile headset: no HDR, 4x MSAA, no post-processing, intermediate texture "Auto"
        // (Meta's recommended settings; with "Always" the transparent background is lost and passthrough looks off)
        if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset))
        {
            System.IO.Directory.CreateDirectory(Root + "/Settings");
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(data, Root + "/Settings/MR_Renderer.asset");
            var rp = UniversalRenderPipelineAsset.Create(data);
            AssetDatabase.CreateAsset(rp, Root + "/Settings/MR_RPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = rp;
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
        }
        var urp = (UniversalRenderPipelineAsset)GraphicsSettings.defaultRenderPipeline;
        urp.supportsHDR = false; urp.msaaSampleCount = 4;
        var urpSo = new SerializedObject(urp); urpSo.FindProperty("m_SupportsTerrainHoles").boolValue = false; urpSo.ApplyModifiedPropertiesWithoutUndo();
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Settings/MR_Renderer.asset");
        if (renderer) { renderer.postProcessData = null; renderer.intermediateTextureMode = IntermediateTextureMode.Auto; EditorUtility.SetDirty(renderer); }
        EditorUtility.SetDirty(urp);

        // Android for Quest: ARM64, IL2CPP, API 32+ (camera images need it), Vulkan (occlusion needs it), linear color
        var android = NamedBuildTarget.Android;
        PlayerSettings.SetApplicationIdentifier(android, "com.ugavr.mixedreality");
        PlayerSettings.productName = "MR Demo";
        PlayerSettings.companyName = "UGA VR";
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.allowUnsafeCode = true;   // the AprilTag frame copy is a raw memcpy
        var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        ps.FindProperty("activeInputHandler").intValue = 1;   // the Input System package only
        ps.ApplyModifiedProperties();

        // XR Plug-in Management: OpenXR on Android
        var perTarget = EnsureXRSettings();
        var general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android);
        general.InitManagerOnStart = true;

        // OpenXR features: Quest support, controllers and hands, and every Meta Quest AR Foundation feature we use
        UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
        var s = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        string[] wanted =
        {
            "com.unity.openxr.feature.metaquest", "com.unity.openxr.feature.compositionlayers",   // passthrough is submitted as a composition layer
            "com.unity.openxr.feature.input.oculustouch", "com.unity.openxr.feature.input.metaquestplus",
            "com.unity.openxr.feature.input.handinteraction", "com.unity.openxr.feature.input.handtracking", "com.unity.openxr.feature.input.metahandtrackingaim",
            "com.unity.openxr.feature.arfoundation-meta-session", "com.unity.openxr.feature.arfoundation-meta-camera",
            "com.unity.openxr.feature.arfoundation-meta-plane", "com.unity.openxr.feature.arfoundation-meta-bounding-boxes",
            "com.unity.openxr.feature.arfoundation-meta-mesh", "com.unity.openxr.feature.arfoundation-meta-anchor",
            "com.unity.openxr.feature.arfoundation-meta-raycast", "com.unity.openxr.feature.arfoundation-meta-occlusion",
        };
        foreach (var id in wanted)
        {
            var f = UnityEditor.XR.OpenXR.Features.FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, id);
            if (f) f.enabled = true; else Debug.LogWarning("[MR] missing OpenXR feature " + id);
        }
        var cam = s.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARCameraFeature>();
        if (cam) cam.cameraImageSupportEnabled = true;    // lets the app read passthrough camera pixels
        foreach (var f in s.GetFeatures()) if (f.enabled) Debug.Log($"[MR] OpenXR feature on: {f.name}");
        EditorUtility.SetDirty(s);
        AssetDatabase.SaveAssets();
    }

    static XRGeneralSettingsPerBuildTarget EnsureXRSettings()
    {
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
        if (perTarget == null)
        {
            System.IO.Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android)) perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
        return perTarget;
    }

    // ---------- the scene ----------

    [MenuItem("MR Demo/Build Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // materials: the virtual things use AR Foundation's occlusion shader, so real objects can hide them
        var occluded = Shader.Find("Occlusion/OcclusionSimpleLit");
        Material tagObject = Mat("Tag object", occluded, new Color(1f, 0.45f, 0.15f));
        Material tagFrame = Mat("Tag frame", occluded, new Color(0.75f, 0.35f, 1f));
        Material overlay = Transparent("Room overlay");
        Material previewMat = Mat("Camera preview", Shader.Find("MRDemo/GrayPreview"), Color.white);
        var cactus = CactusPrefab(occluded);

        // light for the virtual objects
        var sun = new GameObject("Directional Light").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.1f; sun.shadows = LightShadows.Soft;
        sun.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

        // AR Session: starts and stops the platform's tracking
        new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));

        // XR Origin: tracking space -> Unity world. Floor-level origin.
        var originGo = new GameObject("XR Origin");
        var origin = originGo.AddComponent<XROrigin>();
        var offset = new GameObject("Camera Offset").transform; offset.SetParent(originGo.transform, false);
        var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camGo.tag = "MainCamera";
        camGo.transform.SetParent(offset, false);
        var camera = camGo.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);    // alpha 0: the passthrough layer shows through
        camera.nearClipPlane = 0.05f; camera.farClipPlane = 50f;
        var tpd = camGo.AddComponent<TrackedPoseDriver>();
        tpd.positionInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("head pos", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
        tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(new UnityEngine.InputSystem.InputAction("head rot", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
        origin.Camera = camera; origin.CameraFloorOffsetObject = offset.gameObject;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        // passthrough and the depth map
        var cameraManager = camGo.AddComponent<ARCameraManager>();
        var occlusionManager = camGo.AddComponent<AROcclusionManager>();
        occlusionManager.requestedEnvironmentDepthMode = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Medium;
        var shaderOcclusion = camGo.AddComponent<ARShaderOcclusion>();
        var so = new SerializedObject(shaderOcclusion);
        so.FindProperty("m_OcclusionShaderMode").intValue = (int)AROcclusionShaderMode.SoftOcclusion;
        so.FindProperty("m_SoftOcclusionPreprocessShader").objectReferenceValue = Shader.Find("Occlusion/Soft/DepthPreprocessing");
        so.ApplyModifiedPropertiesWithoutUndo();

        // what the headset knows about the room (from Space Setup): planes, furniture boxes, the room mesh
        var planes = originGo.AddComponent<ARPlaneManager>();
        planes.planePrefab = PlanePrefab(overlay);
        var boxes = originGo.AddComponent<ARBoundingBoxManager>();
        boxes.boundingBoxPrefab = BoxPrefab(overlay);
        var anchors = originGo.AddComponent<ARAnchorManager>();
        var raycasts = originGo.AddComponent<ARRaycastManager>();
        var meshGo = new GameObject("Room Mesh"); meshGo.transform.SetParent(originGo.transform, false);
        var meshes = meshGo.AddComponent<ARMeshManager>();
        meshes.meshPrefab = MeshPrefab().GetComponent<MeshFilter>();

        // two hands
        HandRay Hand(string side)
        {
            var go = new GameObject(side + " Ray", typeof(LineRenderer));
            go.transform.SetParent(offset, false);
            var lr = go.GetComponent<LineRenderer>();
            lr.positionCount = 2; lr.widthMultiplier = 0.004f; lr.useWorldSpace = true;
            lr.sharedMaterial = Mat(side + " ray", Shader.Find("Sprites/Default"), Color.white);
            var h = go.AddComponent<HandRay>(); h.hand = side; h.raycasts = raycasts;
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); ring.name = side + " reticle";
            Object.DestroyImmediate(ring.GetComponent<Collider>());
            ring.transform.localScale = new Vector3(0.1f, 0.0015f, 0.1f);
            ring.GetComponent<Renderer>().sharedMaterial = Transparent("Reticle", new Color(1f, 1f, 1f, 0.55f));
            ring.SetActive(false);
            h.reticle = ring.transform;
            return h;
        }
        var left = Hand("LeftHand"); var right = Hand("RightHand");

        // the rules: pinch / trigger places a cactus on a saved anchor
        var sandbox = new GameObject("MR Sandbox").AddComponent<MRSandbox>();
        sandbox.left = left; sandbox.right = right; sandbox.anchors = anchors;
        sandbox.occlusion = shaderOcclusion; sandbox.occlusionManager = occlusionManager;
        sandbox.cactusPrefab = cactus;

        // marker anchors: AprilTags seen by the passthrough cameras
        var tagAnchors = new GameObject("Marker Anchors (AprilTag)").AddComponent<TagAnchors>();
        tagAnchors.cameraManager = cameraManager; tagAnchors.anchors = anchors;
        tagAnchors.tagMaterial = tagObject; tagAnchors.frameMaterial = tagFrame;
        sandbox.tags = tagAnchors;

        // the panel
        var hud = BuildPanel(previewMat, sandbox, tagAnchors);
        hud.planes = planes; hud.boxes = boxes; hud.occlusion = shaderOcclusion; hud.sandbox = sandbox; hud.tags = tagAnchors;

        // nothing reads the room or the cameras until the user says yes
        var gate = new GameObject("Permissions").AddComponent<PermissionGate>();
        gate.needScene = new Behaviour[] { planes, boxes, meshes, raycasts, occlusionManager, shaderOcclusion };
        foreach (var b in gate.needScene) b.enabled = false;      // off in the scene; PermissionGate turns them on
        tagAnchors.enabled = false;
        gate.needCamera = new Behaviour[] { tagAnchors };

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[MR] scene built: " + ScenePath);
    }

    // ---------- the panel (Unity UI, world space; 1 unit = 0.5 mm) ----------

    const float PanelScale = 0.0005f;
    static Font uiFont;

    static Hud BuildPanel(Material previewMat, MRSandbox sandbox, TagAnchors tags)
    {
        uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        go.GetComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 2f;
        var root = go.GetComponent<RectTransform>();
        root.sizeDelta = new Vector2(860, 940); root.localScale = Vector3.one * PanelScale; root.position = new Vector3(0, 1.2f, 0.7f);
        var hud = go.AddComponent<Hud>();

        // background, with a collider so rays stop at the panel
        var bg = Box(root, "Background", 0, 0, 860, 940, new Color(0.07f, 0.09f, 0.11f, 0.93f));
        var bgc = bg.gameObject.AddComponent<BoxCollider>(); bgc.size = new Vector3(860, 940, 4); bgc.center = new Vector3(430, -470, 0);

        Txt(root, "Anchors", 40, 30, 780, 70, 60, Color.white, FontStyle.Bold);
        Txt(root, "Pinning virtual things to the room, and what the headset sees", 40, 100, 780, 40, 28, new Color(0.62f, 0.68f, 0.75f));

        // what the headset gives an app: both passthrough cameras (luma) and Meta's depth map, live
        RectTransform Tile(float x, string caption, Material m)
        {
            var t = new GameObject(caption, typeof(RectTransform), typeof(UnityEngine.UI.RawImage)).GetComponent<RectTransform>();
            Place(t, root, x, 160, 250, 250);
            var ri = t.GetComponent<UnityEngine.UI.RawImage>(); ri.material = m; ri.color = Color.white;
            Txt(root, caption, x, 416, 250, 30, 22, new Color(0.62f, 0.68f, 0.75f), FontStyle.Normal, TextAnchor.UpperCenter);
            return t;
        }
        RectTransform[] Edges(RectTransform tile)
        {
            var edges = new RectTransform[16];
            for (int i = 0; i < edges.Length; i++)
            {
                var e = new GameObject("tag edge", typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
                e.SetParent(tile, false); e.anchorMin = e.anchorMax = new Vector2(0, 1); e.pivot = new Vector2(0, 0.5f);
                e.GetComponent<UnityEngine.UI.Image>().color = new Color(0.75f, 0.35f, 1f);
                e.gameObject.SetActive(false);
                edges[i] = e;
            }
            return edges;
        }
        var left = Tile(40, "Left camera", previewMat);
        var right = Tile(305, "Right camera", previewMat);
        var depthMat = Mat("Depth view", Shader.Find("MRDemo/DepthView"), Color.white);
        var depth = Tile(570, "Depth (left eye)", depthMat);
        tags.previewLeft = left.GetComponent<UnityEngine.UI.RawImage>();
        tags.previewRight = right.GetComponent<UnityEngine.UI.RawImage>();
        hud.tileLeft = left; hud.tileRight = right; hud.edgesLeft = Edges(left); hud.edgesRight = Edges(right);
        hud.depthNote = Txt(depth, "", 0, 100, 250, 60, 24, Color.white, FontStyle.Normal, TextAnchor.UpperCenter);

        // legend: what each colour means, with counts (two columns)
        Text Row(float x, float y, Color dot)
        {
            Box(root, "dot", x, y + 8, 26, 26, dot);
            return Txt(root, "", x + 40, y, 340, 80, 25, new Color(0.85f, 0.88f, 0.92f));
        }
        hud.cactusText = Row(40, 456, new Color(0.25f, 0.70f, 0.50f));
        hud.reloadedText = Row(450, 456, new Color(0.23f, 0.51f, 0.96f));
        hud.tagText = Row(40, 528, new Color(0.75f, 0.35f, 1f));
        hud.roomText = Row(450, 528, new Color(0.55f, 0.6f, 0.68f));

        hud.stats = Txt(root, "", 40, 612, 780, 70, 22, new Color(0.62f, 0.68f, 0.75f));

        // five buttons: name on top, current state below
        Text Button(float x, string name, UnityEngine.Events.UnityAction action)
        {
            const float w = 148;
            var b = Box(root, "Button " + name, x, 690, w, 120, new Color(0.15f, 0.19f, 0.24f));
            var c = b.gameObject.AddComponent<BoxCollider>();                         // WorldButton needs it first
            c.size = new Vector3(w, 120, 20); c.center = new Vector3(w / 2, -60, -14);   // in front of the background
            var wb = b.gameObject.AddComponent<WorldButton>(); wb.face = b.GetComponent<UnityEngine.UI.Image>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(wb.onPress, action);
            Txt(b, name, 0, 16, w, 36, 25, Color.white, FontStyle.Bold, TextAnchor.UpperCenter);
            return Txt(b, "", 4, 56, w - 8, 60, 21, new Color(0.30f, 0.80f, 0.90f), FontStyle.Normal, TextAnchor.UpperCenter);
        }
        hud.occlusionState = Button(40, "Occlusion", sandbox.CycleOcclusion);
        hud.biasState = Button(198, "Depth bias", sandbox.CycleBias);
        hud.roomState = Button(356, "Room", sandbox.ToggleRoomLabels);
        hud.stereoState = Button(514, "Stereo", tags.ToggleStereo);
        Button(672, "Clear all", sandbox.ClearAll).text = "anchors";

        Txt(root, "Point and pinch (hands) or pull the trigger (controllers).\nControllers: A occlusion · right stick click: bias · B room · X stereo · Y clear",
            40, 836, 780, 80, 22, new Color(0.62f, 0.68f, 0.75f));
        return hud;
    }

    static void Place(RectTransform r, Transform parent, float x, float y, float w, float h)
    {
        r.SetParent(parent, false); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
    }

    static RectTransform Box(Transform parent, string name, float x, float y, float w, float h, Color c)
    {
        var r = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)).GetComponent<RectTransform>();
        Place(r, parent, x, y, w, h);
        r.GetComponent<UnityEngine.UI.Image>().color = c;
        return r;
    }

    static UnityEngine.UI.Text Txt(Transform parent, string s, float x, float y, float w, float h, int size, Color c,
                                   FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.UpperLeft)
    {
        var r = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text)).GetComponent<RectTransform>();
        Place(r, parent, x, y, w, h);
        var t = r.GetComponent<UnityEngine.UI.Text>();
        t.font = uiFont; t.fontSize = size; t.color = c; t.fontStyle = style; t.alignment = align; t.text = s;
        t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // the same TRELLIS cactus as the WebXR example (imported by glTFast), on an occlusion material, 18 cm tall,
    // standing on a ring whose colour says what kind of anchor holds it
    static GameObject CactusPrefab(Shader occluded)
    {
        const string glb = Root + "/Models/cactus.glb";
        AssetDatabase.ImportAsset(glb);
        Mesh mesh = null; Texture2D tex = null;
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(glb)) if (o is Mesh m && !mesh) mesh = m;
        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Models/cactus_color.png");   // a real asset (glTFast's own textures don't save into materials)
        Debug.Log($"[MR] cactus: mesh {(mesh ? mesh.name : "missing")}, texture {(tex ? tex.name : "missing")}");
        var root = new GameObject("Cactus", typeof(Cactus));
        var model = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer)); model.transform.SetParent(root.transform, false);
        var mat = Mat("Cactus", occluded, Color.white, tex);
        if (mesh)
        {
            model.GetComponent<MeshFilter>().sharedMesh = mesh;
            var b = mesh.bounds; float k = 0.18f / b.size.y;
            model.transform.localScale = Vector3.one * k;
            model.transform.localPosition = new Vector3(-b.center.x * k, -b.min.y * k, -b.center.z * k);
        }
        model.GetComponent<MeshRenderer>().sharedMaterial = mat;
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder); ring.name = "Ring";
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.002f, 0f); ring.transform.localScale = new Vector3(0.13f, 0.002f, 0.13f);
        ring.GetComponent<Renderer>().sharedMaterial = Mat("Cactus ring", occluded, Color.white);
        return SavePrefab(root, "Cactus");
    }

    static GameObject PlanePrefab(Material overlay)
    {
        var go = new GameObject("AR Plane", typeof(ARPlane), typeof(ARPlaneMeshVisualizer), typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
        go.GetComponent<MeshRenderer>().sharedMaterial = overlay;
        var look = go.AddComponent<SceneLook>();
        look.look = go.GetComponent<MeshRenderer>();
        look.label = Label(go.transform, 0.006f, TextAnchor.MiddleCenter);
        return SavePrefab(go, "AR Plane");
    }

    static GameObject BoxPrefab(Material overlay)
    {
        var go = new GameObject("AR Bounding Box", typeof(ARBoundingBox), typeof(BoxCollider));
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.transform.SetParent(go.transform, false);
        cube.GetComponent<Renderer>().sharedMaterial = overlay;
        var look = go.AddComponent<SceneLook>();
        look.look = cube.GetComponent<Renderer>();
        look.label = Label(go.transform, 0.006f, TextAnchor.LowerCenter);
        return SavePrefab(go, "AR Bounding Box");
    }

    static GameObject MeshPrefab()
    {
        // the room mesh is invisible: it gives the pointing rays something to hit (the couch, the bed)
        var go = new GameObject("Room Mesh Chunk", typeof(MeshFilter), typeof(MeshCollider));
        return SavePrefab(go, "Room Mesh Chunk");
    }

    static TextMesh Label(Transform parent, float size, TextAnchor anchor)
    {
        var go = new GameObject("Label"); go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMesh>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = t.font.material;
        t.fontSize = 48; t.characterSize = size; t.anchor = anchor; t.alignment = TextAlignment.Center; t.color = Color.white;
        return t;
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        System.IO.Directory.CreateDirectory(Root + "/Prefabs");
        var p = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/Prefabs/{name}.prefab");
        Object.DestroyImmediate(go);
        return p;
    }

    static Material Mat(string name, Shader shader, Color c, Texture tex = null)
    {
        System.IO.Directory.CreateDirectory(Root + "/Materials");
        var m = new Material(shader) { name = name };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        if (tex) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }   // before CreateAsset: later changes were not saved
        AssetDatabase.CreateAsset(m, $"{Root}/Materials/{name}.mat");
        return m;
    }

    static Material Transparent(string name, Color? tint = null)
    {
        var m = Mat(name, Shader.Find("Universal Render Pipeline/Unlit"), tint ?? new Color(1f, 1f, 1f, 0.15f));
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f); m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ---------- batch build ----------

    [MenuItem("MR Demo/Build APK")]
    public static void BuildApk()
    {
        SetUpProject();
        BuildScene();
        System.IO.Directory.CreateDirectory("Builds");
        var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        { scenes = new[] { ScenePath }, locationPathName = ApkPath, target = BuildTarget.Android, options = BuildOptions.None });
        Debug.Log("[MR] build " + r.summary.result + " " + r.summary.totalSize + " bytes, " + r.summary.totalErrors + " errors");
        if (r.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
