using System.Collections.Generic;
using UnityEngine;

/// <summary>Authored, viewpoint-relative instructions; no guessed locations for unmeasured corners.</summary>
public static class DeskSetupGuide
{
    public const string Intro = "Face the desk's usual working edge.\nMark near-left, near-right, then far edge.\n\nUse the RIGHT ray + INDEX trigger.\n\nSESSION ONLY: layouts reset when the app closes.";
    public static string Title(int count) => count == 0 ? "1 / NEAR-LEFT CORNER" : count == 1 ? "2 / NEAR-RIGHT CORNER" : count == 2 ? "3 / FAR EDGE" : "CHECK YOUR DESK";
    public static string Body(int count) => count == 0 ?
        "Find the corner closest to you on your LEFT.\n\nAim the RIGHT controller at the flat desktop just inside that corner. When the dot is green, pull the RIGHT INDEX trigger once." : count == 1 ?
        "Now find the RIGHT end of that SAME near edge.\n\nKeep the point on the flat desktop, at the same height as point 1. Aim for a green dot and pull the RIGHT INDEX trigger once." : count == 2 ?
        "Aim straight across the desktop at its FAR edge, away from you. Any point on that opposite edge works.\n\nKeep it on the same flat surface. Green dot, then RIGHT INDEX trigger once." :
        "Does the green rectangle follow your real desktop?\n\nIf yes, select USE THIS DESK. If not, UNDO LAST POINT or RESET POINTS. Accessories appear only after you confirm.";
    public static bool CanMark(IReadOnlyList<Vector3> points, bool hasHit, Vector3 point, Vector3 normal, out string reason)
    {
        reason = "";
        if (points.Count >= 3) { reason = "Check the outline, then select USE THIS DESK."; return false; }
        if (!hasHit || !DeskGeometry.Finite(point) || !DeskGeometry.Finite(normal) || Vector3.Dot(normal, Vector3.up) < .92f)
        { reason = "Aim at the flat TOP of the real desk, not its vertical side. If no surface is found, complete Quest Space Setup."; return false; }
        if (point.y < .3f || point.y > 1.5f) { reason = "Choose the desktop, not the floor or ceiling."; return false; }
        if (points.Count > 0 && Mathf.Abs(point.y - points[0].y) > .035f)
        { reason = "Use the same desktop height as point 1."; return false; }
        if (points.Count == 1 && Vector3.Distance(points[0], point) < .35f)
        { reason = "Point 2 belongs at the OTHER end of the near edge, at least 35 cm from point 1."; return false; }
        if (points.Count == 2 && !DeskGeometry.TryDesk(points[0], points[1], point, out _, out _))
        { reason = "Aim farther across the desk at its opposite edge. The measured depth must be 25 cm to 1.5 m."; return false; }
        return true;
    }
}
