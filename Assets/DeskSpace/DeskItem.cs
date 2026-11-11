using UnityEngine;

public sealed class DeskItem : MonoBehaviour
{
    public int kind;
    public Vector2 Footprint => new Vector2(DeskCatalog.Sizes[kind].x, DeskCatalog.Sizes[kind].z);
    public Vector2 Center => new Vector2(transform.localPosition.x, transform.localPosition.z);
    public float Yaw => transform.localEulerAngles.y;
    void OnDestroy()
    {
        foreach (var f in GetComponentsInChildren<MeshFilter>(true))
            if (f.gameObject.name == "Batched model" && f.sharedMesh) Destroy(f.sharedMesh);
    }
}
