using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Sits on the plane and bounding-box prefabs. Colors each one by what Space Setup says it is (floor, wall, table,
/// couch...) and labels it, so you can see what the app knows about your room. B/Y hides the overlay; the
/// colliders stay, so balls still land on the table.
/// </summary>
public class SceneLook : MonoBehaviour
{
    public static bool visible = true;
    public Renderer look;
    public TextMesh label;
    ARPlane plane; ARBoundingBox box;

    void Awake() { plane = GetComponent<ARPlane>(); box = GetComponent<ARBoundingBox>(); }

    void Update()
    {
        string kind = plane ? plane.classifications.ToString() : box ? box.classifications.ToString() : "";
        if (box && look) { look.transform.localScale = box.size; GetComponent<BoxCollider>().size = box.size; }
        if (look)
        {
            look.enabled = visible;
            var c = Tint(kind); c.a = box ? 0.22f : 0.14f;
            look.material.color = c;
        }
        if (label)
        {
            label.gameObject.SetActive(visible);
            label.text = kind;
            if (box) label.transform.localPosition = new Vector3(0f, box.size.y * 0.5f + 0.05f, 0f);
            if (Camera.main) label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position);
        }
    }

    static Color Tint(string k) =>
        k.Contains("Floor") ? new Color(0.3f, 0.6f, 1f) :
        k.Contains("Wall") ? new Color(0.7f, 0.7f, 0.75f) :
        k.Contains("Ceiling") ? new Color(0.6f, 0.5f, 0.9f) :
        k.Contains("Table") ? new Color(1f, 0.6f, 0.2f) :
        k.Contains("Couch") || k.Contains("Seat") || k.Contains("Bed") ? new Color(0.3f, 0.85f, 0.5f) :
        k.Contains("Door") || k.Contains("Window") ? new Color(1f, 0.9f, 0.3f) : new Color(1f, 1f, 1f);
}
