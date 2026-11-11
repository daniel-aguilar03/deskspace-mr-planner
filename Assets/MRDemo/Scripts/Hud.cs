using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// The panel: what the demo shows, the left camera live with tags boxed, what each colour means, live numbers, and
/// four buttons that show their state. It stays where it is while you look around and re-centres in front of you
/// when you turn well away from it.
/// </summary>
public class Hud : MonoBehaviour
{
    public ARPlaneManager planes;
    public ARBoundingBoxManager boxes;
    public ARShaderOcclusion occlusion;
    public MRSandbox sandbox;
    public TagAnchors tags;
    public Text cactusText, reloadedText, tagText, roomText, stats;
    public Text occlusionState, biasState, roomState, stereoState;
    public RectTransform tileLeft, tileRight;
    public RectTransform[] edgesLeft, edgesRight;   // 4 tags x 4 edges per camera tile
    public Text depthNote;
    public float distance = 0.7f, drop = 0.18f;

    bool moving = true;
    readonly List<Vector2[]> quads = new();

    void LateUpdate()
    {
        var cam = Camera.main.transform;
        var fwd = cam.forward; fwd.y = 0;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();
        var target = cam.position + fwd * distance + Vector3.down * drop;
        var toPanel = transform.position - cam.position; toPanel.y = 0;
        if (Vector3.Angle(fwd, toPanel) > 40f || Mathf.Abs(toPanel.magnitude - distance) > 0.35f) moving = true;
        if (moving)
        {
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
            if (Vector3.Distance(transform.position, target) < 0.02f) moving = false;
        }
        transform.rotation = Quaternion.LookRotation(transform.position - cam.position);

        string mode = occlusion.occlusionShaderMode switch
        {
            AROcclusionShaderMode.HardOcclusion => "hard", AROcclusionShaderMode.SoftOcclusion => "soft", _ => "off"
        };
        occlusionState.text = mode;
        biasState.text = sandbox.biasLevel == 0 ? "none" : $"{sandbox.biasName} · {sandbox.BiasAt(2f) * 100:0} cm at 2 m";
        roomState.text = SceneLook.visible ? "shown" : "hidden";
        stereoState.text = !tags.stereoAvailable ? "n/a" : tags.stereo ? "on" : "off";

        cactusText.text = $"<b>Spatial anchors: {sandbox.cactusCount}</b>\nPinch or trigger on a table or floor";
        reloadedText.text = $"<b>Reloaded: {sandbox.reloadedCount}</b>\nSaved anchors found again at launch";
        tagText.text = $"<b>Marker anchors: {tags.anchoredCount}</b>\nPrinted AprilTags; the shape follows the tag";
        roomText.text = $"<b>Room: {Count(planes)} planes, {Count(boxes)} boxes</b>\nFrom Space Setup (Room button)";
        stats.text = $"camera {tags.cameraFps:0} fps   ·   tags {tags.detectHz:0} Hz, {tags.windowMs:0.0} ms tracked, {tags.fullMs:0} ms full scan\n" +
                     (tags.jitterText.Length > 0 ? tags.jitterText : "show a printed tag to the headset") +
                     (sandbox.status.Length > 0 ? "   ·   " + sandbox.status : "");

        // each tag's outline (its four detected corners) on each camera's image
        Outline(0, tileLeft, edgesLeft);
        Outline(1, tileRight, edgesRight);
        depthNote.text = occlusion.enabled && occlusion.occlusionShaderMode != AROcclusionShaderMode.None ? "" : "depth is off\n(Occlusion button)";
    }

    void Outline(int cam, RectTransform tile, RectTransform[] edges)
    {
        tags.GetPreviewQuads(cam, quads);
        var size = tile.rect.size;
        for (int i = 0; i < edges.Length; i++)
        {
            int q = i / 4, k = i % 4;
            bool on = q < quads.Count;
            edges[i].gameObject.SetActive(on);
            if (!on) continue;
            var a = quads[q][k]; var b = quads[q][(k + 1) % 4];
            var pa = new Vector2(a.x * size.x, -a.y * size.y); var pb = new Vector2(b.x * size.x, -b.y * size.y);
            var d = pb - pa;
            edges[i].anchoredPosition = pa;
            edges[i].sizeDelta = new Vector2(d.magnitude, 4);
            edges[i].localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }
    }

    static int Count(Behaviour m) =>
        m is ARPlaneManager p ? p.trackables.count : m is ARBoundingBoxManager b ? b.trackables.count : 0;
}
