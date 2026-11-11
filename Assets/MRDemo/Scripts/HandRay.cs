using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// A pointer for one hand (controller or tracked hand): follows the aim pose, draws a ray, and finds what it points at.
/// First colliders (the panel, and the room's planes, boxes and mesh from Space Setup), then, if nothing, a depth
/// raycast against whatever the headset sees right now (no room scan needed). A ring marks where the ray lands.
/// </summary>
public class HandRay : MonoBehaviour
{
    public string hand = "RightHand";
    public float maxDistance = 6f;
    public ARRaycastManager raycasts;
    public Transform reticle;
    public MRInput input { get; private set; }

    public bool hasHit { get; private set; }
    public Vector3 point { get; private set; }
    public Vector3 normal { get; private set; }
    public Collider hitCollider { get; private set; }   // null for a depth hit
    public Ray ray => new Ray(transform.position, transform.forward);

    LineRenderer line;
    static readonly List<ARRaycastHit> arHits = new();

    bool DepthHit(Ray r, out Vector3 p)
    {
        p = default;
        if (!raycasts || !raycasts.enabled || !raycasts.Raycast(r, arHits, TrackableType.AllTypes) || arHits.Count == 0 || arHits[0].distance > maxDistance) return false;
        p = arHits[0].pose.position; return true;
    }

    void Awake() { input = new MRInput(hand); line = GetComponent<LineRenderer>(); }

    void Update()
    {
        // the aim pose is in tracking space; this object is a child of the XR Origin's camera offset
        transform.localPosition = input.position.ReadValue<Vector3>();
        transform.localRotation = input.rotation.ReadValue<Quaternion>();
        bool tracked = transform.localPosition != Vector3.zero;

        hasHit = false; hitCollider = null;
        if (tracked && Physics.Raycast(ray, out var h, maxDistance, ~0, QueryTriggerInteraction.Ignore))
        { hasHit = true; point = h.point; normal = h.normal; hitCollider = h.collider; }
        else if (tracked && DepthHit(ray, out var p0))
        {
            // the surface's direction, from two more depth rays a little to the side and above
            hasHit = true; point = p0; normal = -transform.forward;
            var q1 = Quaternion.AngleAxis(2f, transform.up); var q2 = Quaternion.AngleAxis(-2f, transform.right);
            if (DepthHit(new Ray(transform.position, q1 * transform.forward), out var p1) && DepthHit(new Ray(transform.position, q2 * transform.forward), out var p2))
            {
                var n = Vector3.Cross(p1 - p0, p2 - p0);
                if (n.sqrMagnitude > 1e-10f) { n.Normalize(); normal = Vector3.Dot(n, transform.forward) > 0 ? -n : n; }
            }
        }

        line.enabled = tracked;
        float d = hasHit ? Vector3.Distance(transform.position, point) : 0.6f;
        line.SetPosition(0, transform.position);
        line.SetPosition(1, transform.position + transform.forward * d);
        line.startColor = line.endColor = hasHit ? new Color(1f, 1f, 1f, 0.9f) : new Color(1f, 1f, 1f, 0.3f);

        // the ring: where a cactus would go (only on surfaces facing up, and not on the panel)
        if (reticle)
        {
            bool onSurface = hasHit && Vector3.Dot(normal, Vector3.up) > 0.6f && !(hitCollider && hitCollider.GetComponentInParent<Hud>());
            reticle.gameObject.SetActive(onSurface);
            if (onSurface) reticle.SetPositionAndRotation(point + normal * 0.002f, Quaternion.FromToRotation(Vector3.up, normal));
        }
    }
}
