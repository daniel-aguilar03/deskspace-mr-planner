using System.IO;
using System.Xml;
using UnityEditor.Android;

/// <summary>Lets the app use tracked hands as well as controllers (the Meta OpenXR package adds everything else).</summary>
class HandTrackingManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 20;
    const string Android = "http://schemas.android.com/apk/res/android";

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        var doc = new XmlDocument(); doc.Load(file);
        Add(doc, "uses-permission", "com.oculus.permission.HAND_TRACKING", null);
        Add(doc, "uses-feature", "oculus.software.handtracking", "false");
        doc.Save(file);
    }

    static void Add(XmlDocument doc, string tag, string name, string required)
    {
        var manifest = doc.DocumentElement;
        foreach (XmlElement e in manifest.GetElementsByTagName(tag))
            if (e.GetAttribute("name", Android) == name) return;
        var el = doc.CreateElement(tag);
        el.SetAttribute("name", Android, name);
        if (required != null) el.SetAttribute("required", Android, required);
        manifest.AppendChild(el);
    }
}
