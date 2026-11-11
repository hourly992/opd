using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Shared, editable parked-car prefabs. All geometry is saved before the Windows build.
internal static class StreetVehicleBuilder
{
    private static readonly Dictionary<int, GameObject> prefabs = new Dictionary<int, GameObject>();
    private static GameObject parking;

    internal static void Initialize() { prefabs.Clear(); parking = null; }

    internal static GameObject Place(Transform parent, Vector3 position, Vector3 forward, int style, bool createParkingSurface = true)
    {
        if (!prefabs.TryGetValue(style, out var prefab)) prefabs[style] = prefab = Build(style);
        var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        car.transform.SetLocalPositionAndRotation(position + Vector3.up * .045f, Quaternion.LookRotation(forward));
        if (!createParkingSurface) return car;
        if (!parking)
        {
            var paving = EnvironmentMesh.Material("ParkingAsphalt", Color.white);
            StreetSurfaceLibrary.ApplyNamed(paving, "Asphalt");
            var mesh = new EnvironmentMesh();
            mesh.Box(paving, new Vector3(0, .015f, 0), new Vector3(2.75f, .05f, 5.25f));
            var bay = mesh.Create("ParkingBay", "Асфальтовый карман дворовой парковки");
            EnvironmentMesh.Collider(bay, new Vector3(0, .015f, 0), new Vector3(2.75f, .05f, 5.25f));
            parking = EnvironmentMesh.SavePrefab(bay, "ParkingBay");
        }
        var surface = (GameObject)PrefabUtility.InstantiatePrefab(parking, parent);
        surface.transform.SetLocalPositionAndRotation(position, Quaternion.LookRotation(forward));
        return car;
    }

    private static GameObject Build(int style)
    {
        string[] names = { "VehicleNavy", "VehicleSilver", "VehicleBurgundy" };
        Color[] colors = { new Color(.12f, .22f, .34f), new Color(.60f, .62f, .60f), new Color(.37f, .095f, .075f) };
        var paint = EnvironmentMesh.Material(names[style], colors[style], 0, .78f, .52f);
        var trim = EnvironmentMesh.Material("VehicleTrim", new Color(.035f, .043f, .047f), 0, .26f);
        var tire = EnvironmentMesh.Material("Rubber", new Color(.055f, .064f, .064f));
        var alloy = EnvironmentMesh.Material("VehicleAlloy", new Color(.59f, .61f, .62f), 0, .74f, .8f);
        var glass = EnvironmentMesh.Material("VehicleGlass", Color.white);
        var headlight = EnvironmentMesh.Material("VehicleHeadlight", new Color(.71f, .79f, .82f), 0, .88f, .35f);
        var red = EnvironmentMesh.Material("VehicleTailLight", new Color(.43f, .018f, .008f), 0, .8f, .2f);
        var plate = EnvironmentMesh.Material("VehiclePlate", new Color(.84f, .85f, .80f), 0, .35f);
        var root = new GameObject(style == 1 ? "Припаркованный хэтчбек" : style == 2 ? "Припаркованный универсал" : "Припаркованный седан");
        var renderers = new Renderer[3];
        for (int lod = 0; lod < 3; lod++)
        {
            var mesh = new EnvironmentMesh();
            Body(mesh, paint, trim, lod);
            Cabin(mesh, paint, trim, glass, style);
            Wheels(mesh, tire, alloy, trim, lod);
            foreach (int sign in new[] { -1, 1 })
            {
                mesh.RoundedBox(trim, new Vector3(0, .48f, sign * 2.14f), new Vector3(1.70f, .16f, .13f), .06f);
                mesh.RoundedBox(plate, new Vector3(0, .58f, sign * 2.218f), new Vector3(.49f, .115f, .012f), .015f);
                foreach (int side in new[] { -1, 1 })
                {
                    mesh.RoundedBox(sign == 1 ? headlight : red, new Vector3(side * .60f, .77f, sign * 2.14f),
                        new Vector3(.43f, .135f, .07f), .045f);
                    mesh.Box(trim, new Vector3(side * .898f, .37f, 0), new Vector3(.024f, .07f, 2));
                }
            }
            if (lod < 2)
            {
                for (int slat = -5; slat <= 5; slat++)
                    mesh.Box(trim, new Vector3(slat * .054f, .765f, 2.18f), new Vector3(.025f, .11f, .014f));
                foreach (int side in new[] { -1, 1 })
                {
                    foreach (float z in new[] { -.78f, .52f })
                        mesh.RoundedBox(alloy, new Vector3(side * .906f, .875f, z), new Vector3(.021f, .035f, .18f), .008f);
                    mesh.Cylinder(trim, new Vector3(side * .86f, 1.04f, .92f), new Vector3(side * 1.02f, 1.08f, .92f), .022f, .022f, 6);
                    mesh.Ellipsoid(paint, new Vector3(side * 1.035f, 1.10f, .90f), new Vector3(.11f, .07f, .16f), lod == 0 ? 12 : 6, 4);
                    mesh.Box(glass, new Vector3(side * 1.034f, 1.10f, .775f), new Vector3(.13f, .085f, .014f));
                    foreach (float z in new[] { -1.08f, .12f, 1.02f })
                        mesh.Cylinder(trim, new Vector3(side * .903f, .85f, z), new Vector3(side * .895f, .40f, z), .005f, .005f, 4);
                }
                // Windshield wipers, exhaust and the roof aerial remain small silhouettes at street level.
                for (int side = -1; side <= 1; side += 2)
                    mesh.Cylinder(trim, new Vector3(side * .12f, .999f, 1.18f), new Vector3(side * .55f, 1.035f, 1.12f), .008f, .007f, 5);
                mesh.Cylinder(alloy, new Vector3(-.57f, .27f, -1.98f), new Vector3(-.57f, .27f, -2.23f), .045f, .045f, 12);
                mesh.Cylinder(trim, new Vector3(0, 1.47f, -.66f), new Vector3(0, 1.66f, -.76f), .012f, .003f, 5);
            }
            var part = mesh.Create("ParkedVehicle" + style + "_LOD" + lod, "Кузов и колёса · LOD" + lod);
            part.transform.SetParent(root.transform, false);
            renderers[lod] = part.GetComponent<Renderer>();
        }
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(.09f, new[] { renderers[0] }), new LOD(.035f, new[] { renderers[1] }), new LOD(.009f, new[] { renderers[2] }) });
        group.RecalculateBounds();
        var vehicle = root.AddComponent<StreetVehicle>();
        vehicle.localBounds = new Bounds(new Vector3(0, .83f, 0), new Vector3(2.3f, 1.7f, 4.6f));
        vehicle.body = EnvironmentMesh.Collider(root, new Vector3(0, .8f, 0), new Vector3(1.8f, 1.4f, 4.3f));
        return EnvironmentMesh.SavePrefab(root, "ParkedVehicle" + style);
    }

    private static float Width(float z) => Mathf.Lerp(.77f, .90f, Mathf.Clamp01((2.18f - Mathf.Abs(z)) / .35f));
    private static float Top(float z) => .97f - Mathf.Pow(Mathf.Abs(z) / 2.2f, 5) * .18f;
    private static float Arch(float z)
    {
        float distance = Mathf.Min(Mathf.Abs(z - 1.36f), Mathf.Abs(z + 1.36f));
        return distance < .405f ? .38f + Mathf.Sqrt(.405f * .405f - distance * distance) : .34f;
    }

    private static void Body(EnvironmentMesh mesh, Material paint, Material trim, int lod)
    {
        int steps = lod == 0 ? 100 : lod == 1 ? 64 : 36;
        for (int i = 0; i < steps; i++)
        {
            float za = Mathf.Lerp(-2.17f, 2.17f, (float)i / steps), zb = Mathf.Lerp(-2.17f, 2.17f, (float)(i + 1) / steps);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 a = new Vector3(side * Width(za), Top(za) - .07f, za);
                Vector3 b = new Vector3(side * Width(zb), Top(zb) - .07f, zb);
                mesh.Face(paint, Vector3.right * side, new Vector3(a.x * .98f, Arch(za), za), new Vector3(b.x * .98f, Arch(zb), zb), b, a);
                mesh.Surface(paint, new[] { a, b, new Vector3(b.x * .88f, Top(zb), zb), new Vector3(a.x * .88f, Top(za), za) },
                    new[] { new Vector3(side, .5f, 0).normalized, new Vector3(side, .5f, 0).normalized, Vector3.up, Vector3.up });
            }
            if (za < -1.40f || za > 1.18f)
                mesh.Face(paint, Vector3.up, new Vector3(-Width(za) * .88f, Top(za), za), new Vector3(Width(za) * .88f, Top(za), za),
                    new Vector3(Width(zb) * .88f, Top(zb), zb), new Vector3(-Width(zb) * .88f, Top(zb), zb));
        }
        foreach (int end in new[] { -1, 1 })
            mesh.RoundedBox(paint, new Vector3(0, .65f, end * 2.11f), new Vector3(1.66f, .37f, .15f), .06f);
        mesh.Box(trim, new Vector3(0, .31f, 0), new Vector3(1.52f, .055f, 3.7f));
        foreach (int side in new[] { -1, 1 })
            foreach (float wheel in new[] { -1.36f, 1.36f })
            {
                int segments = lod == 0 ? 24 : 12;
                for (int i = 0; i < segments; i++)
                {
                    Vector3 P(float angle, float radius) => new Vector3(side * .908f, .38f + Mathf.Sin(angle) * radius, wheel + Mathf.Cos(angle) * radius);
                    float a = i * Mathf.PI / segments, b = (i + 1) * Mathf.PI / segments;
                    mesh.Face(paint, Vector3.right * side, P(a, .408f), P(b, .408f), P(b, .433f), P(a, .433f));
                }
            }
    }

    private static void Cabin(EnvironmentMesh mesh, Material paint, Material trim, Material glass, int style)
    {
        // Four sloping pillars and a gently crowned roof give each silhouette a real cabin volume.
        float rearRoof = style == 0 ? -.74f : style == 1 ? -1.02f : -1.18f;
        Vector3 backL = new Vector3(-.79f, .972f, -1.42f), backR = new Vector3(.79f, .972f, -1.42f);
        Vector3 frontL = new Vector3(-.79f, .972f, 1.20f), frontR = new Vector3(.79f, .972f, 1.20f);
        Vector3 roofBL = new Vector3(-.65f, 1.43f, rearRoof), roofBR = new Vector3(.65f, 1.43f, rearRoof);
        Vector3 roofFL = new Vector3(-.65f, 1.43f, .58f), roofFR = new Vector3(.65f, 1.43f, .58f);
        void Pane(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var points = new[] { a, b, c, d };
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 center = (a + b + c + d) * .25f;
            if (Vector3.Dot(normal, center - Vector3.up * 1.1f) < 0) normal = -normal;
            mesh.Face(glass, normal, points);
            for (int edge = 0; edge < 4; edge++)
                mesh.Cylinder(trim, points[edge], points[(edge + 1) % 4], .022f, .022f, 6);
        }
        Pane(frontL, frontR, roofFR, roofFL);
        Pane(backR, backL, roofBL, roofBR);
        foreach (int side in new[] { -1, 1 })
        {
            Vector3 bl = side < 0 ? backL : backR, fl = side < 0 ? frontL : frontR;
            Vector3 br = side < 0 ? roofBL : roofBR, fr = side < 0 ? roofFL : roofFR;
            Vector3 basePillar = Vector3.Lerp(bl, fl, .55f), topPillar = Vector3.Lerp(br, fr, .55f);
            Pane(bl, basePillar, topPillar, br);
            Pane(basePillar, fl, fr, topPillar);
            mesh.Cylinder(trim, basePillar, topPillar, .037f, .031f, 8);
            mesh.Cylinder(paint, bl, br, .042f, .038f, 8);
            mesh.Cylinder(paint, fl, fr, .042f, .038f, 8);
            mesh.Cylinder(paint, br, fr, .034f, .034f, 8);
        }
        for (int i = 0; i < 8; i++)
        {
            float xa = Mathf.Lerp(-.65f, .65f, i / 8f), xb = Mathf.Lerp(-.65f, .65f, (i + 1) / 8f);
            Vector3 P(float x, float z) => new Vector3(x, 1.43f + (1 - x * x / (.65f * .65f)) * .045f, z);
            Vector3 N(float x) => new Vector3(x * .21f, 1, 0).normalized;
            mesh.Surface(paint, new[] { P(xa, rearRoof), P(xb, rearRoof), P(xb, .58f), P(xa, .58f) }, new[] { N(xa), N(xb), N(xb), N(xa) });
        }
    }

    private static void Wheels(EnvironmentMesh mesh, Material tire, Material alloy, Material trim, int lod)
    {
        int segments = lod == 0 ? 32 : lod == 1 ? 20 : 12;
        foreach (int side in new[] { -1, 1 })
            foreach (float z in new[] { -1.36f, 1.36f })
            {
                var center = new Vector3(0, .36f, z);
                mesh.Cylinder(tire, center + Vector3.right * (side * .73f), center + Vector3.right * (side * .95f), .34f, .34f, segments);
                mesh.Cylinder(tire, center + Vector3.right * (side * .95f), center + Vector3.right * (side * .968f), .34f, .29f, segments);
                mesh.Cylinder(alloy, center + Vector3.right * (side * .97f), center + Vector3.right * (side * .976f), .235f, .235f, segments);
                mesh.Cylinder(trim, center + Vector3.right * (side * .978f), center + Vector3.right * (side * .980f), .202f, .202f, segments);
                mesh.Cylinder(alloy, center + Vector3.right * (side * .981f), center + Vector3.right * (side * .986f), .066f, .066f, 12);
                if (lod < 2)
                    for (int spoke = 0; spoke < 5; spoke++)
                    {
                        float angle = spoke * Mathf.PI * .4f;
                        Vector3 start = center + new Vector3(side * .986f, Mathf.Cos(angle) * .055f, Mathf.Sin(angle) * .055f);
                        Vector3 end = center + new Vector3(side * .986f, Mathf.Cos(angle) * .22f, Mathf.Sin(angle) * .22f);
                        mesh.Cylinder(alloy, start, end, .024f, .019f, 5);
                    }
            }
    }
}
