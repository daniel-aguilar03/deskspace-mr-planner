using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// A button on the panel: point at it with either hand's ray and pinch (hands) or pull the trigger (controllers).
/// It has a 3D collider so the same rays that place cacti can find it.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WorldButton : MonoBehaviour
{
    public UnityEvent onPress = new();
    public Graphic face;
    public Color idle = new(0.15f, 0.19f, 0.24f), pressed = new(0.01f, 0.54f, 0.65f);
    float flash;

    public void Press() { onPress.Invoke(); flash = 0.2f; }

    void Update()
    {
        flash -= Time.deltaTime;
        if (face) face.color = flash > 0 ? pressed : idle;
    }
}
