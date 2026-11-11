using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One hand's input, the same for a Touch controller and a tracked hand (OpenXR Hand Interaction profile):
/// the aim ray, select (trigger or pinch), grab (grip or fist), and the two face buttons (controllers only).
/// Hands report the aim pose through the Hand Interaction profile and pinches through it or Meta's hand-aim extension.
/// Built in code so there is no input asset to keep in sync.
/// </summary>
public class MRInput
{
    public readonly InputAction position, rotation, select, grab, primary, secondary, stick;

    public MRInput(string hand)   // "LeftHand" or "RightHand"
    {
        string c = "<XRController>{" + hand + "}", h = "<HandInteraction>{" + hand + "}", m = "<MetaAimHand>{" + hand + "}";
        position = new InputAction(hand + " aim position", InputActionType.Value, c + "/pointerPosition", expectedControlType: "Vector3");
        rotation = new InputAction(hand + " aim rotation", InputActionType.Value, c + "/pointerRotation", expectedControlType: "Quaternion");
        // select: trigger, or a pinch (two hand profiles; whichever the runtime provides)
        select = new InputAction(hand + " select", InputActionType.Button, c + "/triggerPressed");
        select.AddBinding(h + "/pointerActivated");
        select.AddBinding(m + "/indexPressed");
        grab = new InputAction(hand + " grab", InputActionType.Button, c + "/gripPressed");
        grab.AddBinding(h + "/graspFirm");
        primary = new InputAction(hand + " A/X", InputActionType.Button, c + "/primaryButton");
        secondary = new InputAction(hand + " B/Y", InputActionType.Button, c + "/secondaryButton");
        stick = new InputAction(hand + " stick click", InputActionType.Button, c + "/primary2DAxisClick");
        foreach (var a in new[] { position, rotation, select, grab, primary, secondary, stick }) a.Enable();
    }
}
