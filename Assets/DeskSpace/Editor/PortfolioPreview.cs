using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Editor-only, synthetic-pose visual smoke test. Not a headset/performance test and never records microphone audio.</summary>
[InitializeOnLoad]
public static class PortfolioPreview
{
    const string Key = "Portfolio2.Preview";
    static bool initialized;
    static int frames, errors;
    static double began;
    static Camera camera;
    static bool detective;
    static PortfolioPreview()
    {
        if (SessionState.GetBool(Key, false)) { began = EditorApplication.timeSinceStartup; EditorApplication.update += Tick; Application.logMessageReceived += Log; }
    }
    public static void Run()
    {
        detective = Directory.Exists("Assets/Detective");
        DeskSpaceBuilder.Prepare();
        EditorSceneManager.OpenScene(detective ? "Assets/Detective/Detective.unity" : "Assets/DeskSpace/DeskSpace.unity");
        SessionState.SetBool(Key, true); began = EditorApplication.timeSinceStartup;
        EditorApplication.update -= Tick; EditorApplication.update += Tick; Application.logMessageReceived += Log;
        EditorApplication.EnterPlaymode();
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type != LogType.Exception && type != LogType.Error) return;
        if (stack.Contains("UnityEditor.Search.SearchDatabase"))
        { Debug.LogWarning("PREVIEW_EDITOR_INDEX_WARNING (not game code): " + message); return; }
        errors++; Debug.LogWarning("PREVIEW_RUNTIME_ERROR: " + message);
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup - began > 100) { Finish(2, "Preview timeout"); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        detective = Directory.Exists("Assets/Detective");
        if (!initialized)
        {
            camera = Camera.main; if (!camera) return;
            foreach (var tpd in Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsSortMode.None)) tpd.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(0, 1.65f, 0), Quaternion.identity);
            camera.stereoTargetEye = StereoTargetEyeMask.None; camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            camera.fieldOfView = 68; camera.aspect = 16f / 9f;
            ConfigureSyntheticScene(); initialized = true; frames = 0;
        }
        frames++;
        var game=Object.FindFirstObjectByType<DeskPlanner>();
        if(frames==25)camera.transform.LookAt(game.panel.position);
        if(frames==30){Capture("Validation/setup-introduction.png");CheckText();}
        if(frames==35)
        {
            if(game.RecordSetupPoint(true,new Vector3(0,.75f,0),Vector3.up))throw new Exception("Setup must require BEGIN SETUP");
            game.SetupAction();
        }
        if(frames==45){Capture("Validation/setup-near-left.png");CheckText();}
        if(frames==50)game.RecordSetupPoint(true,new Vector3(0,.75f,0),Vector3.up);
        if(frames==60){Capture("Validation/setup-near-right.png");CheckText();}
        if(frames==65)game.RecordSetupPoint(true,new Vector3(1.2f,.75f,0),Vector3.up);
        if(frames==75){Capture("Validation/setup-far-edge.png");CheckText();}
        if(frames==80)
        {
            if(game.RecordSetupPoint(true,new Vector3(.6f,.75f,.1f),Vector3.up)||game.SetupPointCount!=2)throw new Exception("Invalid far point must preserve previous two");
            game.RecordSetupPoint(true,new Vector3(.6f,.75f,.6f),Vector3.up);
        }
        if(frames==90){Capture("Validation/setup-review.png");CheckText();}
        if(frames==95)
        {
            if(game.SetupPointCount!=3||game.layoutRoot.activeSelf)throw new Exception("Review required before accessories");
            var guides=GameObject.Find("Desk setup / measured points only");
            if(guides.GetComponentsInChildren<Collider>(true).Length!=0)throw new Exception("Visual markers must not intercept rays");
            game.UndoSetupPoint();if(game.SetupPointCount!=2)throw new Exception("Undo one point");
            game.ShowSetupHelp();game.SetupAction();if(game.SetupPointCount!=2)throw new Exception("Help must preserve points");
            game.ResetSetupPoints();if(game.SetupPointCount!=0||!game.SetupStarted)throw new Exception("Reset only in-progress points");
            foreach(var text in new[]{DeskSetupGuide.Intro,DeskSetupGuide.Body(0),DeskSetupGuide.Body(1),DeskSetupGuide.Body(2),DeskSetupGuide.Body(3)})
            {
                game.setupBody.text=text;
                if(game.setupBody.preferredHeight>game.setupBody.rectTransform.rect.height+3)throw new Exception("Setup body overflow");
            }
            game.NewDesk();
            if(game.SetupStarted||game.SetupPointCount!=0)throw new Exception("New desk must return unfinished setup to introduction");
            foreach(var button in Object.FindObjectsByType<WorldButton>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                for(int i=0;i<button.onPress.GetPersistentEventCount();i++)
                    if(button.onPress.GetPersistentMethodName(i)=="Save")throw new Exception("Save control must not ship in session-only app");
            if(!Object.FindObjectsByType<Text>(FindObjectsInactive.Include,FindObjectsSortMode.None).Any(t=>t.text==DeskPlanner.SessionNotice))throw new Exception("Session-only notice missing");
            Debug.Log("SESSION_ONLY_TESTS: no save control, visible lifetime notice, live-depth-only room readers and unfinished-setup reset passed; no persistent anchor API was invoked");
            Debug.Log("SETUP_FLOW_TESTS: begin gate, point order, invalid-point recovery, review gate, undo, non-resetting help, reset and collider-free markers passed; no anchor service was invoked");
            game.enabled=false;game.setupRoot.SetActive(false);game.layoutRoot.SetActive(true);guides.SetActive(false);
        }
        if(frames==105){Capture("Validation/desktop-smoke.png");CheckText();}
        if(frames==115)
        {
            game.panel.gameObject.SetActive(false);
            camera.transform.SetPositionAndRotation(new Vector3(1.2f,1.6f,-1.8f),Quaternion.LookRotation(new Vector3(-1.2f,-.6f,2f)));
        }
        if(frames==150){Capture("Validation/catalog-preview.png");Debug.Log("PREVIEW_SMOKE: synthetic desktop render complete; runtime errors="+errors+"; this is not Quest validation");Finish(errors==0?0:1,"Preview complete");}
    }
    static void CheckText()
    {
        var overflow=Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Where(t=>t.gameObject.activeInHierarchy&&t.preferredHeight>t.rectTransform.rect.height+3).Select(t=>t.name).ToArray();
        Debug.Log("PREVIEW_TEXT_OVERFLOW: "+string.Join(" | ",overflow));if(overflow.Length>0)errors++;
    }
    static void ConfigureSyntheticScene()
    {
        var planner = Object.FindFirstObjectByType<DeskPlanner>();
        planner.occlusion.enabled=false;
        Shader.SetGlobalInteger("_IsOcclusionOn", 0); // Match AR Foundation's integer property type.
        camera.transform.SetPositionAndRotation(new Vector3(1.2f,1.6f,-1.8f), Quaternion.LookRotation(new Vector3(-1.2f,-.6f,2f)));
        camera.fieldOfView = 60;
        var table = GameObject.CreatePrimitive(PrimitiveType.Cube); table.name = "SYNTHETIC DESK / NOT REAL ROOM DATA";
        table.transform.position = new Vector3(0,.725f,.25f); table.transform.localScale = new Vector3(1.8f,.05f,.8f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color = new Color(.32f,.21f,.13f);
        table.GetComponent<Renderer>().sharedMaterial = mat;
        Vector3[] positions = { new Vector3(-.25f,.75f,.38f), new Vector3(-.25f,.75f,.04f), new Vector3(.08f,.75f,.02f),
            new Vector3(.65f,.75f,.35f), new Vector3(-.72f,.75f,.43f), new Vector3(.3f,.75f,.43f) };
        for(int i=0;i<DeskCatalog.Names.Length;i++)
        {
            var model=DeskCatalog.Create(i,planner.objectMaterials); model.transform.position=positions[i];
            var renderers=model.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
            if(renderers.Length>5)throw new Exception("Catalog batching regression");
            Debug.Log("CATALOG_RENDER_CHECK: "+DeskCatalog.Names[i]+" / "+renderers.Length+" material batches");
        }
        Debug.Log("PREVIEW_NOTE: synthetic table/catalog only, NOT evidence of anchors, passthrough or occlusion working");
    }
    static void Capture(string file)
    {
        Directory.CreateDirectory("Validation");
        var rt = new RenderTexture(1600, 900, 24); rt.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        var old = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); tex.Apply();
        File.WriteAllBytes(file, tex.EncodeToPNG()); RenderTexture.active = old;
        Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
    }
    static void Finish(int code, string why)
    {
        SessionState.SetBool(Key, false); EditorApplication.update -= Tick; Application.logMessageReceived -= Log;
        Debug.Log(why); EditorApplication.Exit(code);
    }
}
