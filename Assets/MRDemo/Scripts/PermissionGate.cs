using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

/// <summary>
/// Quest asks the user before an app may read the room (planes, furniture boxes, the room mesh and the depth map:
/// USE_SCENE) or the passthrough cameras (HEADSET_CAMERA). The behaviours listed here stay off until the matching
/// permission is granted.
/// </summary>
public class PermissionGate : MonoBehaviour
{
    public const string Scene = "com.oculus.permission.USE_SCENE";
    public const string Camera = "horizonos.permission.HEADSET_CAMERA";
    public Behaviour[] needScene;
    public Behaviour[] needCamera;

    public static bool HasCamera => Application.isEditor || Permission.HasUserAuthorizedPermission(Camera);

    void Start()
    {
        var ask = new List<string>();
        foreach (var (p, list) in new[] { (Scene, needScene), (Camera, needCamera) })
        {
            if (list == null || list.Length == 0) continue; // Do not ask for unused raw-camera access.
            if (Application.isEditor || Permission.HasUserAuthorizedPermission(p)) Enable(list);
            else { Disable(list); ask.Add(p); }
        }
        if (ask.Count == 0) return;
        var cb = new PermissionCallbacks();
        cb.PermissionGranted += p => { Debug.Log("[MR] granted " + p); Enable(p == Scene ? needScene : needCamera); };
        cb.PermissionDenied += p => Debug.LogWarning("[MR] denied " + p);
        Permission.RequestUserPermissions(ask.ToArray(), cb);
    }

    static void Enable(Behaviour[] list) { foreach (var b in list) if (b) b.enabled = true; }
    static void Disable(Behaviour[] list) { foreach (var b in list) if (b) b.enabled = false; }
}
