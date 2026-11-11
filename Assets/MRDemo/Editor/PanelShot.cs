using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the panel to a PNG (layout check without a headset): -executeMethod PanelShot.Render -shotOut <file>
public static class PanelShot
{
    public static void Render()
    {
        string outFile = "/tmp/panel.png";
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-shotOut") outFile = a[i + 1];
        EditorSceneManager.OpenScene("Assets/MRDemo/MRDemo.unity");
        var panel = GameObject.Find("Panel").transform;
        panel.position = new Vector3(0, 0, 0.7f); panel.rotation = Quaternion.identity;
        var hud = panel.GetComponent<Hud>(); if (hud) hud.enabled = false;
        foreach (var t in panel.GetComponentsInChildren<UnityEngine.UI.Text>()) if (t.text == "") t.text = t.fontSize > 24 ? "<b>Example: 2</b>\nexample description" : "on";
        var cam = new GameObject("shot").AddComponent<Camera>();
        cam.transform.position = Vector3.zero; cam.fieldOfView = 40; cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.5f, 0.45f);   // stands in for a room
        var rt = new RenderTexture(1000, 1100, 24) { antiAliasing = 4 }; cam.targetTexture = rt; cam.aspect = 1000f / 1100f;
        cam.Render(); RenderTexture.active = rt;
        var tex = new Texture2D(1000, 1100, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1000, 1100), 0, 0); tex.Apply();
        File.WriteAllBytes(outFile, tex.EncodeToPNG());
    }
}
