using UnityEngine;

/// <summary>Desk-space geometry. All dimensions are metres, not headset-relative offsets.</summary>
public static class DeskGeometry
{
    public static bool TryDesk(Vector3 frontLeft, Vector3 frontRight, Vector3 back, out Pose pose, out Vector2 size)
    {
        pose = default; size = default;
        if (!Finite(frontLeft) || !Finite(frontRight) || !Finite(back)) return false;
        if (Mathf.Abs(frontLeft.y - frontRight.y) > .035f || Mathf.Abs(frontLeft.y - back.y) > .035f) return false;
        Vector3 across = frontRight - frontLeft; across.y = 0;
        float w = across.magnitude; if (w < .35f || w > 3f) return false;
        across /= w;
        Vector3 into = Vector3.Cross(across, Vector3.up);
        float depth = Vector3.Dot(back - frontLeft, into);
        if (depth < 0) { into = -into; depth = -depth; across = -across; }
        if (depth < .25f || depth > 1.5f) return false;
        Vector3 center = (frontLeft + frontRight) * .5f + into * depth * .5f;
        center.y = (frontLeft.y + frontRight.y + back.y) / 3f;
        pose = new Pose(center, Quaternion.LookRotation(into, Vector3.up)); size = new Vector2(w, depth); return true;
    }
    public static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    public static Vector2[] Corners(Vector2 center, Vector2 size, float yaw)
    {
        var q = Quaternion.Euler(0, yaw, 0); var r = new Vector2[4];
        for (int i = 0; i < 4; i++)
        { Vector3 p = q * new Vector3((i == 0 || i == 3 ? -.5f : .5f) * size.x, 0, (i < 2 ? -.5f : .5f) * size.y); r[i] = center + new Vector2(p.x, p.z); }
        return r;
    }
    public static bool Fits(Vector2 desk, Vector2 center, Vector2 size, float yaw)
    {
        if (!Finite(new Vector3(center.x, center.y, yaw)) || desk.x <= 0 || desk.y <= 0 || size.x <= 0 || size.y <= 0) return false;
        foreach (var p in Corners(center, size, yaw))
            if (Mathf.Abs(p.x) > desk.x * .5f - .005f || Mathf.Abs(p.y) > desk.y * .5f - .005f) return false;
        return true;
    }
    public static bool Overlaps(Vector2 a, Vector2 asize, float ayaw, Vector2 b, Vector2 bsize, float byaw)
    {
        var aa = Corners(a, asize, ayaw); var bb = Corners(b, bsize, byaw);
        foreach (var poly in new[] { aa, bb })
            for (int i = 0; i < 2; i++)
            {
                Vector2 edge = poly[(i + 1) % 4] - poly[i]; Vector2 axis = new Vector2(-edge.y, edge.x).normalized;
                Project(aa, axis, out float alo, out float ahi); Project(bb, axis, out float blo, out float bhi);
                if (ahi <= blo + .001f || bhi <= alo + .001f) return false;
            }
        return true;
    }
    static void Project(Vector2[] p, Vector2 axis, out float lo, out float hi)
    { lo = hi = Vector2.Dot(p[0], axis); foreach (var v in p) { float d = Vector2.Dot(v, axis); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); } }
}
