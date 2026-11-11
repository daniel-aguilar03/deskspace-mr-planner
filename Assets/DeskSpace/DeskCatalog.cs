using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Small authored, life-size catalog; stylized approximations, not manufacturer CAD or fit guarantees.</summary>
public static class DeskCatalog
{
    public static readonly string[] Names = { "27-inch monitor + stand", "Keyboard", "Mouse", "PS5-sized console", "Desk lamp", "Pencil holder" };
    // x width, y height, z depth, in the upright orientation shown in the preview.
    // Original PS5 enclosure: 104 mm wide x 390 mm tall x 260 mm deep, excluding base/protrusions (Sony 2020 specs).
    public static readonly Vector3[] Sizes = {
        new Vector3(.62f, .46f, .21f), new Vector3(.44f, .025f, .14f), new Vector3(.065f, .04f, .115f),
        new Vector3(.104f, .39f, .26f), new Vector3(.20f, .43f, .20f), new Vector3(.085f, .18f, .085f)
    };
    public static GameObject Create(int kind, Material[] m)
    {
        var root = new GameObject(Names[kind]);
        void Shape(PrimitiveType type, string name, Vector3 p, Vector3 s, Material mat, Vector3 euler = default)
        {
            var o = GameObject.CreatePrimitive(type); o.name = name; o.transform.SetParent(root.transform, false);
            o.transform.localPosition = p; o.transform.localScale = s; o.transform.localEulerAngles = euler;
            Object.Destroy(o.GetComponent<Collider>()); o.GetComponent<Renderer>().sharedMaterial = mat;
        }
        void Box(string n, Vector3 p, Vector3 s, Material mat) => Shape(PrimitiveType.Cube, n, p, s, mat);
        switch (kind)
        {
            case 0:
                Box("Stand base", new Vector3(0, .008f, 0), new Vector3(.22f, .016f, .20f), m[1]);
                Box("Stand neck", new Vector3(0, .115f, .055f), new Vector3(.045f, .20f, .04f), m[2]);
                Box("Display bezel", new Vector3(0, .285f, .025f), new Vector3(.62f, .35f, .025f), m[1]);
                Box("Display", new Vector3(0, .289f, .010f), new Vector3(.59f, .322f, .003f), m[3]);
                Box("Screen task bar", new Vector3(0, .14f, .007f), new Vector3(.59f, .018f, .002f), m[2]);
                for (int i = 0; i < 3; i++) Box("Screen window", new Vector3(-.18f + i * .18f, .30f, .006f), new Vector3(.15f, .18f, .002f), i == 1 ? m[4] : m[2]);
                break;
            case 1:
                Box("Keyboard base", new Vector3(0, .009f, 0), new Vector3(.44f, .018f, .14f), m[1]);
                for (int row = 0; row < 5; row++) for (int col = 0; col < 16; col++)
                    Box("Key", new Vector3(-.205f + col * .027f, .022f, -.055f + row * .027f), new Vector3(.022f, .006f, .021f), row == 0 ? m[3] : m[0]);
                break;
            case 2:
                Shape(PrimitiveType.Sphere, "Mouse body", new Vector3(0, .02f, 0), Sizes[2], m[0]);
                Box("Mouse split", new Vector3(0, .039f, -.015f), new Vector3(.0015f, .002f, .045f), m[1]);
                Shape(PrimitiveType.Cylinder, "Scroll wheel", new Vector3(0, .039f, -.02f), new Vector3(.009f, .01f, .009f), m[1], new Vector3(90, 0, 0));
                break;
            case 3:
                Box("Console core", new Vector3(0, .195f, 0), new Vector3(.074f, .38f, .24f), m[1]);
                foreach (int side in new[] { -1, 1 })
                {
                    Box("White shell", new Vector3(side * .045f, .195f, 0), new Vector3(.014f, .39f, .26f), m[0]);
                    Box("Blue status strip", new Vector3(side * .036f, .30f, -.122f), new Vector3(.002f, .14f, .002f), m[3]);
                }
                Box("Disc slot", new Vector3(.043f, .12f, -.132f), new Vector3(.002f, .12f, .001f), m[1]);
                break;
            case 4:
                Shape(PrimitiveType.Cylinder, "Lamp base", new Vector3(0, .009f, 0), new Vector3(.18f, .009f, .18f), m[1]);
                Shape(PrimitiveType.Cylinder, "Lamp arm", new Vector3(0, .21f, .025f), new Vector3(.016f, .20f, .016f), m[2]);
                Shape(PrimitiveType.Sphere, "Lamp shade", new Vector3(0, .39f, -.025f), new Vector3(.17f, .08f, .17f), m[4]);
                Shape(PrimitiveType.Cylinder, "Lamp diffuser", new Vector3(0, .365f, -.025f), new Vector3(.135f, .004f, .135f), m[0]);
                break;
            case 5:
                Shape(PrimitiveType.Cylinder, "Pencil cup", new Vector3(0, .052f, 0), new Vector3(.085f, .052f, .085f), m[3]);
                Shape(PrimitiveType.Cylinder, "Cup opening", new Vector3(0, .105f, 0), new Vector3(.074f, .001f, .074f), m[1]);
                for (int i = 0; i < 5; i++)
                    Shape(PrimitiveType.Cylinder, "Pencil", new Vector3(-.024f + i * .012f, .12f, -.008f + (i % 2) * .02f), new Vector3(.006f, .06f, .006f), i % 2 == 0 ? m[4] : m[0]);
                break;
        }
        // Combine parts by shared material: 80 keys should not mean 80 separate draw calls.
        var groups = new Dictionary<Material, List<CombineInstance>>();
        foreach (var f in root.GetComponentsInChildren<MeshFilter>())
        {
            var r = f.GetComponent<Renderer>();
            if (!groups.TryGetValue(r.sharedMaterial, out var parts)) groups[r.sharedMaterial] = parts = new List<CombineInstance>();
            parts.Add(new CombineInstance { mesh = f.sharedMesh, transform = root.transform.worldToLocalMatrix * f.transform.localToWorldMatrix });
            r.enabled = false;
        }
        foreach (var kv in groups)
        {
            var o = new GameObject("Batched model", typeof(MeshFilter), typeof(MeshRenderer)); o.transform.SetParent(root.transform, false);
            var mesh = new Mesh { name = Names[kind], indexFormat = IndexFormat.UInt32 }; mesh.CombineMeshes(kv.Value.ToArray());
            o.GetComponent<MeshFilter>().sharedMesh = mesh; o.GetComponent<Renderer>().sharedMaterial = kv.Key;
        }
        var collider = root.AddComponent<BoxCollider>(); collider.center = new Vector3(0, Sizes[kind].y * .5f, 0); collider.size = Sizes[kind];
        var item = root.AddComponent<DeskItem>(); item.kind = kind; return root;
    }
}
