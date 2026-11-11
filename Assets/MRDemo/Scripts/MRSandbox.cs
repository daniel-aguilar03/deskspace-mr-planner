using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// The demo's rules, the same as the WebXR "Mixed reality anywhere" page:
///   Pinch or trigger, pointing at a real surface: put a cactus there on a spatial anchor, and save the anchor.
///     Ring colour: green = anchored and saved, blue = reloaded from a saved anchor at launch, orange = not saved.
///   Printed AprilTags get marker anchors on their own (see TagAnchors).
///   Pointing at a button on the panel: press it. Controllers also have A/B/X/Y for the same four buttons.
/// </summary>
public class MRSandbox : MonoBehaviour
{
    public HandRay left, right;
    public ARAnchorManager anchors;
    public ARShaderOcclusion occlusion;
    public AROcclusionManager occlusionManager;
    public TagAnchors tags;
    public GameObject cactusPrefab;
    public Color anchored = new(0.25f, 0.70f, 0.50f), reloaded = new(0.23f, 0.51f, 0.96f), unsaved = new(0.96f, 0.62f, 0.04f);

    const string SavedKey = "mr.savedAnchors";

    // depth bias levels: bias(z) = near + scale * z^2 metres (see OcclusionComputation.hlsl)
    static readonly (string name, float near, float scale)[] BiasLevels =
        { ("none", 0f, 0f), ("low", 0.01f, 0.02f), ("medium", 0.02f, 0.04f), ("high", 0.03f, 0.08f) };
    public int biasLevel = 2;
    public string biasName => BiasLevels[biasLevel].name;
    public float BiasAt(float z) => BiasLevels[biasLevel].near + BiasLevels[biasLevel].scale * z * z;
    readonly Dictionary<ARAnchor, SerializableGuid> cacti = new();
    public int cactusCount => cacti.Count;
    public int reloadedCount { get; private set; }
    public string status = "";

    async void Start()
    {
        ApplyBias();
        // anchors saved by an earlier run: ask the runtime to find them again in this room
        foreach (var s in PlayerPrefs.GetString(SavedKey, "").Split(','))
        {
            if (!System.Guid.TryParse(s, out var g)) continue;
            var r = await anchors.TryLoadAnchorAsync(new SerializableGuid(g));
            if (!r.status.IsSuccess()) continue;
            AddCactus(r.value, reloaded);
            cacti[r.value] = new SerializableGuid(g);
            reloadedCount++;
        }
    }

    void Update()
    {
        foreach (var h in new[] { left, right })
        {
            if (!h.input.select.WasPressedThisFrame()) continue;
            Debug.Log($"[MR] select {h.hand}: hit {h.hasHit} {(h.hitCollider ? h.hitCollider.name : h.hasHit ? "depth" : "-")} up {Vector3.Dot(h.normal, Vector3.up):0.00} at {h.point}");
            if (!h.hasHit) { status = "nothing under the ray"; continue; }
            var c = h.hitCollider;
            if (c && c.GetComponent<WorldButton>()) c.GetComponent<WorldButton>().Press();
            else if (c && (c.GetComponentInParent<Hud>() || c.GetComponentInParent<Cactus>())) { }   // the panel or a cactus: nothing
            else Place(h.point, h.normal);
        }
        if (right.input.primary.WasPressedThisFrame()) CycleOcclusion();      // A
        if (right.input.secondary.WasPressedThisFrame()) ToggleRoomLabels();  // B
        if (left.input.primary.WasPressedThisFrame()) tags.ToggleStereo();    // X
        if (left.input.secondary.WasPressedThisFrame()) ClearAll();           // Y
        if (right.input.stick.WasPressedThisFrame()) CycleBias();             // right thumbstick click
    }

    async void Place(Vector3 at, Vector3 normal)
    {
        if (Vector3.Dot(normal, Vector3.up) < 0.6f) { status = "point at a table or the floor"; Debug.Log("[MR] place: surface not facing up"); return; }
        // upright, turned toward you
        var toYou = Camera.main.transform.position - at; toYou.y = 0;
        var pose = new Pose(at, toYou.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toYou) : Quaternion.identity);
        // an anchor is a pose the runtime keeps fixed to the real world as its map of the room improves
        var r = await anchors.TryAddAnchorAsync(pose);
        if (!r.status.IsSuccess()) { status = "anchor failed: " + r.status; Debug.Log("[MR] place: " + status); return; }
        Debug.Log("[MR] place: anchored at " + at);
        var ring = AddCactus(r.value, unsaved);
        var saved = await anchors.TrySaveAnchorAsync(r.value);
        if (saved.status.IsSuccess())
        {
            cacti[r.value] = saved.value; Remember();
            ring.material.SetColor("_BaseColor", anchored); status = "anchor saved";
        }
        else { cacti[r.value] = default; status = "anchor not saved: " + saved.status; }
    }

    Renderer AddCactus(ARAnchor anchor, Color ringColor)
    {
        var c = Instantiate(cactusPrefab, anchor.transform, false);
        var ring = c.transform.Find("Ring").GetComponent<Renderer>();
        ring.material.SetColor("_BaseColor", ringColor);
        Axes.Add(anchor.transform, 0.05f);   // where the anchor itself is
        return ring;
    }

    public void CycleOcclusion()
    {
        var next = (AROcclusionShaderMode)(((int)occlusion.occlusionShaderMode + 1) % 3);
        occlusion.occlusionShaderMode = next;
        occlusionManager.enabled = next != AROcclusionShaderMode.None;
    }

    public void CycleBias() { biasLevel = (biasLevel + 1) % BiasLevels.Length; ApplyBias(); }

    void ApplyBias()
    {
        Shader.SetGlobalFloat("_OcclusionBiasNear", BiasLevels[biasLevel].near);
        Shader.SetGlobalFloat("_OcclusionBiasScale", BiasLevels[biasLevel].scale);
    }

    public void ToggleRoomLabels() => SceneLook.visible = !SceneLook.visible;

    public void ClearAll() => ClearAllAsync();

    async void ClearAllAsync()
    {
        foreach (var kv in cacti)
        {
            if (kv.Value != default) await anchors.TryEraseAnchorAsync(kv.Value);
            if (kv.Key) anchors.TryRemoveAnchor(kv.Key);
        }
        cacti.Clear(); reloadedCount = 0;
        PlayerPrefs.DeleteKey(SavedKey);
        tags.ClearAll();
        status = "cleared";
    }

    void Remember()
    {
        var ids = new List<string>();
        foreach (var g in cacti.Values) if (g != default) ids.Add(g.guid.ToString());
        PlayerPrefs.SetString(SavedKey, string.Join(",", ids));
        PlayerPrefs.Save();
    }
}

