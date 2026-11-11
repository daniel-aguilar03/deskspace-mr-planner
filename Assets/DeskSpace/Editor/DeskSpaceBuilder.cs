using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using Object = UnityEngine.Object;

public static class DeskSpaceBuilder
{
    const string Scene = "Assets/DeskSpace/DeskSpace.unity";
    [MenuItem("Portfolio 2/Demo 6 — Prepare desk planner")]
    public static void Prepare()
    {
        MRDemoBuilder.SetUpProject(); MRDemoBuilder.BuildScene();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var sandbox = Object.FindFirstObjectByType<MRSandbox>();
        var anchors = sandbox.anchors; var left = sandbox.left; var right = sandbox.right;
        var occlusion = sandbox.occlusion; var depth = sandbox.occlusionManager;
        Object.DestroyImmediate(GameObject.Find("Panel"));
        Object.DestroyImmediate(sandbox.gameObject);
        Object.DestroyImmediate(GameObject.Find("Marker Anchors (AprilTag)"));
        var gate = Object.FindFirstObjectByType<PermissionGate>(); gate.needCamera = Array.Empty<Behaviour>();
        // Live depth supplies tabletop hits and occlusion. Unused saved-room readers otherwise
        // repeatedly hit the headset's blocked discovery service, even without saved layouts.
        foreach (var reader in gate.needScene)
            if (reader is ARPlaneManager || reader is ARBoundingBoxManager || reader is ARMeshManager) Object.DestroyImmediate(reader);
        gate.needScene = Array.FindAll(gate.needScene, reader => reader);
        var camFeature = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android).GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARCameraFeature>();
        if (camFeature) { camFeature.cameraImageSupportEnabled = false; EditorUtility.SetDirty(camFeature); }

        Directory.CreateDirectory("Assets/DeskSpace/Materials");
        var root = new GameObject("DeskSpace / layout planner"); var game = root.AddComponent<DeskPlanner>();
        game.anchors = anchors; game.left = left; game.right = right; game.occlusion = occlusion; game.depth = depth;
        game.session = Object.FindFirstObjectByType<ARSession>(); game.keyLight = Object.FindFirstObjectByType<Light>();
        game.keyLight.transform.rotation = Quaternion.Euler(55, -25, 0); game.keyLight.intensity = .8f;
        game.keyLight.shadows = LightShadows.Soft;
        QualitySettings.shadowDistance = 5f; QualitySettings.shadowResolution = UnityEngine.ShadowResolution.Medium;
        var rp = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (rp)
        {
            var so = new SerializedObject(rp); var res = so.FindProperty("m_MainLightShadowmapResolution"); if (res != null) res.intValue = 512;
            var shadows = so.FindProperty("m_MainLightShadowsSupported"); if (shadows != null) shadows.boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp);
        }
        var shader = Shader.Find("Occlusion/OcclusionSimpleLit");
        if (!shader) throw new Exception("Required occlusion shader missing");
        game.objectMaterials = new[] {
            Mat("Porcelain", shader, new Color(.88f,.9f,.91f)), Mat("Graphite",shader,new Color(.025f,.035f,.045f)),
            Mat("Aluminium",shader,new Color(.32f,.38f,.42f)), Mat("Teal",shader,new Color(.04f,.43f,.47f)), Mat("Ochre",shader,new Color(.82f,.55f,.20f))
        };
        game.validPreview = Mat("Valid placement", shader, new Color(.12f,.8f,.50f));
        game.invalidPreview = Mat("Invalid placement", shader, new Color(.92f,.16f,.12f));
        game.markerMaterial = Mat("Setup markers",Shader.Find("Universal Render Pipeline/Unlit"),new Color(.15f,.95f,.62f));
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        game.markerMesh = marker.GetComponent<MeshFilter>().sharedMesh; Object.DestroyImmediate(marker);
        game.shadowMaterial = Mat("Desk shadow", Shader.Find("DeskSpace/ShadowReceiver"), Color.black);
        game.shadowMaterial.SetFloat("_Opacity", .4f);
        var cam = Camera.main; cam.nearClipPlane = .05f; cam.farClipPlane = 15f;

        var panel = new GameObject("DeskSpace controls", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        var rt = (RectTransform)panel.transform; rt.sizeDelta = new Vector2(820, 1300); rt.localScale = Vector3.one * .00075f;
        panel.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace; panel.GetComponent<Canvas>().worldCamera = cam;
        panel.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2;
        rt.position = new Vector3(-.45f, 1.45f, .95f); game.panel = rt;
        var bg = panel.AddComponent<Image>(); bg.color = new Color(.045f,.08f,.10f,.98f);
        var bgc = panel.AddComponent<BoxCollider>(); bgc.size = new Vector3(820,1300,2); bgc.center = new Vector3(0,0,2);
        var layout=Rect(rt,"Layout controls / after desk confirmation",0,820,1300,0); game.layoutRoot=layout.gameObject;
        Text(layout, "DESKSPACE", 25, 760, 70, 48, Gold);
        Text(layout, "PLAN YOUR REAL DESK / QUEST 3", 95,760,45,26,Ink);
        game.info = Text(layout,"Set up your real desk",150,760,90,30,Ink);
        game.status = Text(layout,"Mark the front-left corner",245,760,130,28,Gold);
        for (int i=0;i<DeskCatalog.Names.Length;i++)
        {
            var b=Button(layout,DeskCatalog.Names[i],390+(i/2)*77,370,65,25+(i%2)*400);
            UnityEventTools.AddIntPersistentListener(b.onPress,game.Add,i);
        }
        game.selection=Text(layout,"Select an object to edit",630,760,120,28,Ink);
        Bind(Button(layout,"Move selected",760,370,65),game.MoveSelected);
        Bind(Button(layout,"Remove selected",760,370,65,425),game.RemoveSelected);
        Bind(Button(layout,"Turn −15°",835,235,65),game.RotateLeft);
        Bind(Button(layout,"Turn +15°",835,235,65,290),game.RotateRight);
        Bind(Button(layout,"Cancel move",835,235,65,555),game.Cancel);
        Text(layout,DeskPlanner.SessionNotice,910,370,70,25,Gold).alignment=TextAnchor.MiddleCenter;
        Bind(Button(layout,"New desk / clear",910,370,70,425),game.NewDesk);
        var occ=Button(layout,"Occlusion: Soft",995,370,65); Bind(occ,game.CycleOcclusion);
        game.occlusionLabel=occ.GetComponentInChildren<Text>();
        Bind(Button(layout,"Match room lighting",995,370,65,425),game.Lighting);
        Bind(Button(layout,"Bring panel here / Y",1070,370,60),game.RecenterPanel);
        Bind(Button(layout,"SETUP HELP",1070,370,60,425),game.ShowSetupHelp);
        Text(layout,"Right trigger: place. Either ray: buttons / select. B: hide controls. Y: bring controls here. Real walking only—respect your boundary. Approximate models; no camera recording.",1145,760,130,25,Ink);
        layout.gameObject.SetActive(false);
        BuildSetupGuide(rt,game);

        PlayerSettings.productName="DeskSpace"; PlayerSettings.companyName="Daniel Aguilar";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.danielaguilar.deskspace");
        PlayerSettings.Android.applicationEntry=AndroidApplicationEntry.Activity;
        PlayerSettings.bundleVersion="0.1.2"; PlayerSettings.Android.bundleVersionCode=3;
        EditorSceneManager.SaveScene(scene,Scene);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(Scene,true)}; AssetDatabase.SaveAssets(); Validate();
        Debug.Log("DESKSPACE_PREPARED: geometry/session-only checks passed; Quest validation pending");
    }
    static readonly Color Ink=new Color(.9f,.94f,.95f), Gold=new Color(.96f,.77f,.40f);
    static void BuildSetupGuide(RectTransform panel,DeskPlanner game)
    {
        var p=Rect(panel,"Guided desk setup",0,820,1300,0); game.setupRoot=p.gameObject;
        game.setupTitle=Text(p,"SET UP YOUR REAL DESK",25,760,95,44,Gold);
        game.setupProgress=Text(p,"0/3 points marked / right ray + index trigger",130,760,70,30,Ink);
        Text(p,"FAR EDGE / away from you",205,760,45,28,Ink).alignment=TextAnchor.MiddleCenter;
        var top=Rect(p,"Desktop / top-down diagram",270,480,190,170).gameObject.AddComponent<Image>(); top.color=new Color(.12f,.23f,.27f);
        Text(p,"YOU / edge closest to you",480,760,45,28,Ink).alignment=TextAnchor.MiddleCenter;
        game.setupDots=new Image[3];
        int[] x={145,625,385}, y={425,425,250};
        for(int i=0;i<3;i++)
        {
            var r=Rect(p,"Diagram point "+(i+1),y[i],50,50,x[i]); var image=r.gameObject.AddComponent<Image>(); image.color=Gold; game.setupDots[i]=image;
            var label=Text(r,(i+1).ToString(),0,50,50,36,new Color(.04f,.08f,.10f),0); label.alignment=TextAnchor.MiddleCenter;
        }
        game.setupBody=Text(p,DeskSetupGuide.Intro,545,760,290,32,Ink);
        game.setupFeedback=Text(p,"Select BEGIN SETUP when your desk is clear.",850,760,140,30,Gold);
        var primary=Button(p,"BEGIN SETUP",1010,760,80); Bind(primary,game.SetupAction);
        game.setupPrimary=primary.gameObject; game.setupPrimaryLabel=primary.GetComponentInChildren<Text>(); game.setupPrimaryLabel.fontSize=34;
        var undo=Button(p,"UNDO LAST POINT",1110,370,65); Bind(undo,game.UndoSetupPoint); game.setupUndo=undo.gameObject; undo.gameObject.SetActive(false);
        var reset=Button(p,"RESET POINTS",1110,370,65,425); Bind(reset,game.ResetSetupPoints); game.setupReset=reset.gameObject; reset.gameObject.SetActive(false);
        Text(p,"Panel blocking the desk? B hides it; Y brings it back. Look to the side and press Y to park it beside the desk. Stay inside your boundary.",1200,760,90,25,Ink);
    }
    static RectTransform Rect(Transform p,string n,float top,float w,float h,float left=25)
    {
        var r=new GameObject(n,typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(p,false);
        r.anchorMin=r.anchorMax=new Vector2(0,1); r.pivot=new Vector2(0,1); r.sizeDelta=new Vector2(w,h); r.anchoredPosition=new Vector2(left,-top); return r;
    }
    static Text Text(Transform p,string value,float top,float w,float h,int font,Color color,float left=25)
    {
        var r=Rect(p,value.Length>35?value.Substring(0,35):value,top,w,h,left); var t=r.gameObject.AddComponent<Text>();
        t.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.fontSize=font; t.color=color; t.text=value;
        t.supportRichText=false; t.raycastTarget=false; t.alignment=TextAnchor.UpperLeft; return t;
    }
    static WorldButton Button(Transform p,string value,float top,float w,float h,float left=25)
    {
        var r=Rect(p,value,top,w,h,left); var img=r.gameObject.AddComponent<Image>(); img.color=new Color(.11f,.20f,.25f);
        var col=r.gameObject.AddComponent<BoxCollider>(); col.size=new Vector3(w,h,3); col.center=new Vector3(w*.5f,-h*.5f,-3);
        var b=r.gameObject.AddComponent<WorldButton>(); b.face=img; b.idle=img.color;
        var t=Text(r,value,10,w-26,h-10,28,Ink,13); t.alignment=TextAnchor.MiddleCenter; return b;
    }
    static void Bind(WorldButton b,UnityAction action)=>UnityEventTools.AddPersistentListener(b.onPress,action);
    static Material Mat(string n,Shader s,Color c)
    {
        if(!s)throw new Exception("Shader missing for "+n);
        string p="Assets/DeskSpace/Materials/"+n+".mat"; var m=AssetDatabase.LoadAssetAtPath<Material>(p);
        if(!m){m=new Material(s);AssetDatabase.CreateAsset(m,p);}
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",c);m.enableInstancing=true;EditorUtility.SetDirty(m);return m;
    }
    public static void Validate()
    {
        int checks=0;
        void Check(bool value,string name){if(!value)throw new Exception("DeskSpace test failed: "+name);checks++;}
        Check(DeskGeometry.TryDesk(new Vector3(0,.75f,0),new Vector3(1.2f,.75f,0),new Vector3(.5f,.75f,.6f),out var pose,out var size),"three-point desk");
        Check(Mathf.Abs(size.x-1.2f)<.001f&&Mathf.Abs(size.y-.6f)<.001f&&Mathf.Abs(pose.position.y-.75f)<.001f,"measured dimensions");
        Check(!DeskGeometry.TryDesk(Vector3.zero,new Vector3(.1f,0,0),Vector3.forward,out _,out _),"tiny desk rejected");
        Check(!DeskGeometry.TryDesk(Vector3.zero,Vector3.right,new Vector3(0,.2f,.6f),out _,out _),"non-flat desk rejected");
        Check(DeskGeometry.Fits(size,Vector2.zero,new Vector2(.44f,.14f),0),"keyboard fits");
        Check(!DeskGeometry.Fits(size,new Vector2(.59f,0),new Vector2(.44f,.14f),0),"off-edge blocked");
        Check(DeskGeometry.Overlaps(Vector2.zero,Vector2.one,45,new Vector2(.2f,0),Vector2.one,0),"rotated overlap");
        Check(!DeskGeometry.Overlaps(Vector2.zero,new Vector2(.1f,.1f),0,new Vector2(.5f,0),new Vector2(.1f,.1f),45),"separate footprints");
        Check(!DeskGeometry.Fits(size,new Vector2(float.NaN,0),new Vector2(.44f,.14f),0),"NaN position blocked");
        Check(!DeskGeometry.Fits(size,Vector2.zero,new Vector2(.44f,.14f),float.NaN),"NaN rotation blocked");
        Check(DeskGeometry.Fits(size,Vector2.zero,new Vector2(.44f,.14f),90),"rotated keyboard fits");
        Check(typeof(DeskPlanner).GetMethod("Save")==null&&typeof(DeskPlanner).GetNestedType("LayoutSave")==null,"persistent save entry points removed");
        Check(DeskCatalog.Names.Length==6&&DeskCatalog.Sizes.Length==6,"six-item catalog");
        var points=new System.Collections.Generic.List<Vector3>();
        Check(!DeskSetupGuide.CanMark(points,false,new Vector3(0,.75f,0),Vector3.up,out _),"no surface rejected");
        Check(!DeskSetupGuide.CanMark(points,true,new Vector3(0,.75f,0),Vector3.right,out _),"vertical side rejected");
        Check(!DeskSetupGuide.CanMark(points,true,Vector3.zero,Vector3.up,out _),"floor rejected");
        Check(DeskSetupGuide.CanMark(points,true,new Vector3(0,.75f,0),Vector3.up,out _),"near-left accepted"); points.Add(new Vector3(0,.75f,0));
        Check(!DeskSetupGuide.CanMark(points,true,new Vector3(.1f,.75f,0),Vector3.up,out _),"duplicate corner rejected");
        Check(!DeskSetupGuide.CanMark(points,true,new Vector3(1.2f,.9f,0),Vector3.up,out _),"mixed heights rejected");
        Check(DeskSetupGuide.CanMark(points,true,new Vector3(1.2f,.75f,0),Vector3.up,out _),"near-right accepted"); points.Add(new Vector3(1.2f,.75f,0));
        Check(!DeskSetupGuide.CanMark(points,true,new Vector3(.6f,.75f,.1f),Vector3.up,out _),"near edge cannot substitute far edge");
        Check(DeskSetupGuide.CanMark(points,true,new Vector3(.6f,.75f,.6f),Vector3.up,out _),"far edge accepted"); points.Add(new Vector3(.6f,.75f,.6f));
        Check(!DeskSetupGuide.CanMark(points,true,new Vector3(.7f,.75f,.6f),Vector3.up,out _),"fourth point blocked");
        Check(!Object.FindFirstObjectByType<ARPlaneManager>()&&!Object.FindFirstObjectByType<ARBoundingBoxManager>()&&!Object.FindFirstObjectByType<ARMeshManager>(),"unused saved-room readers removed");
        Check(Object.FindFirstObjectByType<ARAnchorManager>()&&Object.FindFirstObjectByType<ARRaycastManager>()&&Object.FindFirstObjectByType<AROcclusionManager>(),"live anchors, depth raycasts and occlusion retained");
        Check(DeskSetupGuide.Intro.Contains("SESSION ONLY")&&DeskPlanner.SessionNotice.Contains("Resets when app closes"),"session lifetime disclosed");
        Debug.Log($"DESKSPACE_TESTS: {checks} desk, guided-point, boundary, rotation, overlap and session-only checks passed");
    }
    [MenuItem("Portfolio 2/Demo 6 — Build Quest APK")]
    public static void BuildApk()
    {
        Prepare();Directory.CreateDirectory("Builds");
        var r=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{Scene},locationPathName="Builds/DeskSpace-v0.1.2.apk",target=BuildTarget.Android,options=BuildOptions.None});
        if(r.summary.result!=BuildResult.Succeeded)throw new Exception("DeskSpace APK failed: "+r.summary.result);
        Debug.Log("DESKSPACE_APK_BUILT: "+r.summary.totalSize+" bytes; not yet headset-tested");
    }
}
