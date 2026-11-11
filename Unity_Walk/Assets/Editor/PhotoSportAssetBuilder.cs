using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Saved equipment meshes from address-linked municipal photographs, not a site generator.
/// The photos document the appearance at Stroiteley 57 in September 2024.
/// Dimensions are inferred from people in the photographs, not surveyed measurements.
/// This deliberately never opens, edits, or places an object in Street.unity.
/// </summary>
public static class PhotoSportAssetBuilder
{
    public const string AssetRoot = "Assets/PhotoReconstruction/Sport2024";
    public const string RampPrefabPath = AssetRoot + "/Prefabs/SkateRamp_Stroiteley57_Approx.prefab";
    public const string WorkoutPrefabPath = AssetRoot + "/Prefabs/WorkoutBars_Stroiteley57_Approx.prefab";
    private const string Article = "https://news-hm.ru/news/gorod/v-khanty-mansiyske-poyavilos-novoe-mesto-dlya-otdykha-detey-i-molodyezhi/";
    private const string RampPhoto = "https://news-hm.ru/upload/iblock/12e/rftz96dv58u63esi04k7jzdnce1qks38.jpg";
    private const string WorkoutPhoto = "https://news-hm.ru/upload/iblock/93b/mtb10jiwgs9iygvguk2i7lfj5d3cycin.jpg";
    private static Material brown, green, steel, red, dark, fastener;

    [MenuItem("Street / Photo reconstruction / Generate photographed sport equipment library")]
    public static void Generate()
    {
        foreach (string folder in new[] { AssetRoot + "/Meshes", AssetRoot + "/Materials", AssetRoot + "/Prefabs" })
            EnsureFolder(folder);
        brown = OwnMaterial("RampPhenolicBrown", new Color(.19f, .135f, .115f), .17f, .02f);
        green = OwnMaterial("RampPaintGreen", new Color(.035f, .22f, .13f), .31f, .24f);
        steel = OwnMaterial("WorkoutPowderCoatGrey", new Color(.52f, .55f, .56f), .38f, .23f);
        red = OwnMaterial("WorkoutPaintRed", new Color(.65f, .055f, .025f), .34f, .20f);
        dark = OwnMaterial("FootCapBlack", new Color(.035f, .037f, .038f), .12f, .02f);
        fastener = OwnMaterial("ZincFasteners", new Color(.58f, .61f, .63f), .52f, .78f);
        SaveEquipment("SkateRamp_Stroiteley57_Approx", "Скейт-рампа · Строителей 57 · размеры приблизительные",
            RampPhoto, "Размеры приблизительные: ширина 4.0 м, длина 3.9 м, настил 1.70 / 1.45 м; размещение не подтверждено",
            BuildRamp, true);
        SaveEquipment("WorkoutBars_Stroiteley57_Approx", "Рукоход · Строителей 57 · размеры приблизительные",
            WorkoutPhoto, "Размеры приблизительные: 1.30 × 2.60 м; высота 2.35 м; размещение не подтверждено",
            BuildWorkout, false);
        AssetDatabase.SaveAssets();
        Debug.Log("PHOTO_SPORT_LIBRARY_READY two unplaced prefabs; appearance photographed 2024, dimensions approximate; " + AssetRoot);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }

    // These assets are isolated from StreetGenerated/Pilot and its material library.
    private static Material OwnMaterial(string name, Color color, float smoothness, float metallic)
    {
        string path = AssetRoot + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find("Street/Surface");
        if (!shader) throw new InvalidOperationException("Photographed sport equipment requires the existing Street/Surface shader");
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetColor("_Color", color);
        material.SetFloat("_Kind", 0);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Cull", 2);
        material.SetFloat("_VertexTint", 0);
        material.SetColor("_EmissionColor", Color.black);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SaveEquipment(string assetName, string displayName, string photo, string dimensions,
        Func<int, EnvironmentMesh> build, bool useMeshCollider)
    {
        var root = new GameObject(displayName);
        try
        {
            // Empty transforms preserve evidence inside the prefab without a new runtime component.
            AddNote(root.transform, "Источник фото · Андрей Краснов · ГИЦ · 30.09.2024");
            AddNote(root.transform, "PHOTO " + photo);
            AddNote(root.transform, "ARTICLE " + Article);
            AddNote(root.transform, dimensions);
            AddNote(root.transform, "Библиотека · участок и ориентацию сверить перед размещением · не точная обмерная копия");
            var lods = new LOD[3];
            Mesh nearMesh = null;
            float[] transitions = { .20f, .075f, .018f };
            for (int level = 0; level < 3; level++)
            {
                var geometry = build(level).Create(assetName + "_LOD" + level, "LOD" + level,
                    AssetRoot, worldMappedOnly: true);
                geometry.transform.SetParent(root.transform, false);
                var renderer = geometry.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
                GameObjectUtility.SetStaticEditorFlags(geometry, StaticEditorFlags.ContributeGI |
                    StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                lods[level] = new LOD(transitions[level], new Renderer[] { renderer });
                if (level == 0) nearMesh = geometry.GetComponent<MeshFilter>().sharedMesh;
            }
            var group = root.AddComponent<LODGroup>();
            // No cross-fade shader dependency: standard mesh LOD transitions work with Street/Surface.
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(lods);
            group.RecalculateBounds();
            if (useMeshCollider)
            {
                var collision = new GameObject("Статический коллайдер настила и ограждений");
                collision.transform.SetParent(root.transform, false);
                collision.AddComponent<MeshCollider>().sharedMesh = nearMesh;
                collision.isStatic = true;
            }
            else AddWorkoutColliders(root.transform);
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AssetRoot + "/Prefabs/" + assetName + ".prefab");
            if (!prefab) throw new InvalidOperationException("Could not save photographed equipment " + assetName);
            AssetDatabase.SetLabels(prefab, new[] { "PhotoReference2024", "Stroiteley57", "DimensionsApproximate", "UnplacedLibrary" });
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void AddNote(Transform parent, string note)
    {
        var marker = new GameObject(note);
        marker.transform.SetParent(parent, false);
        marker.tag = "EditorOnly";
    }

    private static EnvironmentMesh BuildRamp(int lod)
    {
        var mesh = new EnvironmentMesh();
        int segments = lod == 0 ? 40 : lod == 1 ? 20 : 10;
        BuildRampLane(mesh, -2f, -.025f, 1.70f, segments, lod, true, false);
        BuildRampLane(mesh, .025f, 2f, 1.45f, segments, lod, false, true);
        return mesh;
    }

    private static void BuildRampLane(EnvironmentMesh mesh, float left, float right, float height,
        int segments, int lod, bool leftOutside, bool rightOutside)
    {
        const float run = 2.60f, platform = .95f, thickness = .038f;
        float radius = (run * run + height * height) / (2 * height);
        float angle = Mathf.Acos(1 - height / radius);
        Vector3 Point(float x, float t) => new Vector3(x, .035f + radius * (1 - Mathf.Cos(t)), -run + radius * Mathf.Sin(t));
        Vector3 Normal(float t) => new Vector3(0, Mathf.Cos(t), -Mathf.Sin(t));
        var profile = new List<Vector3>();
        for (int i = 0; i <= segments; i++) profile.Add(Point(0, angle * i / segments));
        for (int i = 0; i < segments; i++)
        {
            float a = angle * i / segments, b = angle * (i + 1) / segments;
            var points = new[] { Point(left, a), Point(right, a), Point(right, b), Point(left, b) };
            mesh.Surface(brown, points, new[] { Normal(a), Normal(a), Normal(b), Normal(b) });
            mesh.Face(brown, -(Normal(a) + Normal(b)).normalized, points[0] - Vector3.up * thickness,
                points[3] - Vector3.up * thickness, points[2] - Vector3.up * thickness, points[1] - Vector3.up * thickness);
            if (lod < 2 && i % (lod == 0 ? 5 : 4) == 0)
            {
                foreach (float x in new[] { left + .065f, right - .065f })
                {
                    Vector3 p = Point(x, a) + Normal(a) * .002f;
                    mesh.Cylinder(fastener, p, p + Normal(a) * .003f, .006f, sides: 6);
                }
            }
        }
        float deckY = .035f + height;
        mesh.Box(brown, new Vector3((left + right) * .5f, deckY - thickness * .5f, platform * .5f),
            new Vector3(right - left, thickness, platform));
        // Closed opaque side panels follow the exact transition profile rather than a wedge box.
        foreach (float x in new[] { left, right })
        {
            var outline = new List<Vector3> { new Vector3(x, 0, platform), new Vector3(x, deckY, platform) };
            for (int i = profile.Count - 1; i >= 0; i--) outline.Add(new Vector3(x, profile[i].y, profile[i].z));
            outline.Add(new Vector3(x, 0, -run));
            Vector3 outward = x == left ? Vector3.left : Vector3.right;
            mesh.Face(brown, outward, outline.ToArray());
            // Backside closes the thin side panel, so it is solid from internal inspection views.
            var inner = outline.ConvertAll(p => p - outward * .018f);
            mesh.Face(brown, -outward, inner.ToArray());
            for (int i = 0; i < outline.Count; i++)
            {
                int j = (i + 1) % outline.Count;
                mesh.Face(green, Vector3.Cross(outline[j] - outline[i], outward).normalized,
                    outline[i], outline[j], inner[j], inner[i]);
            }
        }
        mesh.Box(brown, new Vector3((left + right) * .5f, deckY * .5f, platform - .018f),
            new Vector3(right - left, deckY, .036f));
        // Steel threshold plate is lower than the first timber layer, avoiding an abrupt foot ledge.
        mesh.Box(green, new Vector3((left + right) * .5f, .012f, -run - .115f),
            new Vector3(right - left, .024f, .27f));
        mesh.Cylinder(green, new Vector3(left, deckY + .008f, 0), new Vector3(right, deckY + .008f, 0),
            .025f, sides: lod == 0 ? 14 : 8);
        foreach (float x in new[] { left + .018f, right - .018f })
        {
            // Square tube bottom frame, vertical back uprights and sloping transition edge.
            Beam(mesh, green, new Vector3(x, .055f, -run), new Vector3(x, .055f, platform), .042f);
            Beam(mesh, green, new Vector3(x, .055f, platform), new Vector3(x, deckY, platform), .042f);
            for (int i = 0; i < profile.Count - 1; i++)
                Beam(mesh, green, new Vector3(x, profile[i].y, profile[i].z),
                    new Vector3(x, profile[i + 1].y, profile[i + 1].z), .027f);
            if (lod == 0)
                for (float z = .16f; z < platform; z += .30f)
                    mesh.Cylinder(fastener, new Vector3(x, deckY, z), new Vector3(x, deckY + .003f, z), .006f, sides: 6);
        }
        GuardRail(mesh, left, right, deckY, platform, lod);
        if (leftOutside) SideGuard(mesh, left, deckY, platform, lod);
        if (rightOutside) SideGuard(mesh, right, deckY, platform, lod);
        if (lod == 0)
        {
            // Flush join seams at real modular sheet breaks, not painted texture stripes.
            foreach (float fraction in new[] { .33f, .66f })
            {
                float t = angle * fraction;
                Vector3 p = Point(left + .03f, t) + Normal(t) * .001f;
                mesh.Cylinder(dark, p, p + Vector3.right * (right - left - .06f), .0015f, sides: 4);
            }
            foreach (float x in new[] { left + .11f, right - .11f })
                FootPlate(mesh, new Vector3(x, 0, platform - .12f), green);
        }
    }

    private static void GuardRail(EnvironmentMesh mesh, float left, float right, float y, float z, int lod)
    {
        Beam(mesh, green, new Vector3(left, y + .95f, z), new Vector3(right, y + .95f, z), .045f);
        Beam(mesh, green, new Vector3(left, y + .10f, z), new Vector3(right, y + .10f, z), .035f);
        float step = lod == 2 ? .31f : .16f;
        for (float x = left; x <= right + .001f; x += step)
            Beam(mesh, green, new Vector3(x, y + .10f, z), new Vector3(x, y + .95f, z), .022f);
        foreach (float x in new[] { left, right })
            Beam(mesh, green, new Vector3(x, y - .03f, z), new Vector3(x, y + 1.0f, z), .045f);
    }

    private static void SideGuard(EnvironmentMesh mesh, float x, float y, float platform, int lod)
    {
        foreach (float z in new[] { .035f, platform })
            Beam(mesh, green, new Vector3(x, y - .03f, z), new Vector3(x, y + 1.0f, z), .045f);
        Beam(mesh, green, new Vector3(x, y + .95f, .035f), new Vector3(x, y + .95f, platform), .045f);
        Beam(mesh, green, new Vector3(x, y + .10f, .035f), new Vector3(x, y + .10f, platform), .035f);
        for (float z = .18f; z < platform; z += lod == 2 ? .30f : .16f)
            Beam(mesh, green, new Vector3(x, y + .10f, z), new Vector3(x, y + .95f, z), .022f);
    }

    private static EnvironmentMesh BuildWorkout(int lod)
    {
        var mesh = new EnvironmentMesh();
        int sides = lod == 0 ? 16 : lod == 1 ? 10 : 6;
        foreach (float x in new[] { -.65f, .65f })
            foreach (float z in new[] { -1.30f, 1.30f })
            {
                mesh.Cylinder(steel, new Vector3(x, .035f, z), new Vector3(x, 2.35f, z), .055f, sides: sides);
                mesh.Cylinder(dark, new Vector3(x, 2.35f, z), new Vector3(x, 2.38f, z), .058f, sides: sides);
                foreach (float y in new[] { .90f, 2.22f })
                    mesh.Cylinder(red, new Vector3(x, y - .045f, z), new Vector3(x, y + .045f, z), .068f, sides: sides);
                mesh.Cylinder(steel, new Vector3(x, .02f, z), new Vector3(x, .18f, z), .075f, .055f, sides);
                if (lod < 2) FootPlate(mesh, new Vector3(x, 0, z), steel);
            }
        // Red overhead ladder, four photographed grey support posts, separate clamp collars.
        foreach (float x in new[] { -.65f, .65f })
            mesh.Cylinder(red, new Vector3(x, 2.22f, -1.30f), new Vector3(x, 2.22f, 1.30f), .025f, sides: sides);
        for (int rung = 0; rung <= 8; rung++)
        {
            float z = -1.30f + rung * 2.60f / 8;
            mesh.Cylinder(red, new Vector3(-.65f, 2.22f, z), new Vector3(.65f, 2.22f, z), .018f, sides: sides);
        }
        // Entry crossbars visible below the red head frame; open space remains walkable.
        foreach (float z in new[] { -1.30f, 1.30f })
        {
            mesh.Cylinder(red, new Vector3(-.65f, .90f, z), new Vector3(.65f, .90f, z), .023f, sides: sides);
            if (lod == 0)
                foreach (float x in new[] { -.65f, .65f })
                    foreach (float y in new[] { .90f, 2.22f })
                        mesh.Cylinder(fastener, new Vector3(x, y, z + .064f), new Vector3(x, y, z + .077f), .012f, sides: 6);
        }
        return mesh;
    }

    private static void Beam(EnvironmentMesh mesh, Material material, Vector3 from, Vector3 to, float width)
    {
        Vector3 direction = to - from;
        if (direction.sqrMagnitude < .0000001f) return;
        mesh.Box(material, (from + to) * .5f, new Vector3(width, width, direction.magnitude),
            Quaternion.FromToRotation(Vector3.forward, direction.normalized));
    }

    private static void FootPlate(EnvironmentMesh mesh, Vector3 center, Material material)
    {
        mesh.Box(material, center + Vector3.up * .012f, new Vector3(.19f, .024f, .19f));
        foreach (float x in new[] { -.067f, .067f })
            foreach (float z in new[] { -.067f, .067f })
                mesh.Cylinder(fastener, center + new Vector3(x, .023f, z), center + new Vector3(x, .035f, z), .009f, sides: 6);
    }

    private static void AddWorkoutColliders(Transform root)
    {
        var collision = new GameObject("Коллайдеры стоек и перекладин · проходы открыты").transform;
        collision.SetParent(root, false);
        foreach (float x in new[] { -.65f, .65f })
            foreach (float z in new[] { -1.30f, 1.30f })
                Capsule(collision, new Vector3(x, .06f, z), new Vector3(x, 2.34f, z), .058f);
        foreach (float x in new[] { -.65f, .65f })
            Capsule(collision, new Vector3(x, 2.22f, -1.30f), new Vector3(x, 2.22f, 1.30f), .025f);
        for (int rung = 0; rung <= 8; rung++)
        {
            float z = -1.30f + rung * 2.60f / 8;
            Capsule(collision, new Vector3(-.65f, 2.22f, z), new Vector3(.65f, 2.22f, z), .018f);
        }
        foreach (float z in new[] { -1.30f, 1.30f })
            Capsule(collision, new Vector3(-.65f, .90f, z), new Vector3(.65f, .90f, z), .023f);
    }

    private static void Capsule(Transform parent, Vector3 from, Vector3 to, float radius)
    {
        var root = new GameObject("Стойка / перекладина");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = (from + to) * .5f;
        root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, to - from);
        root.isStatic = true;
        var collider = root.AddComponent<CapsuleCollider>();
        collider.direction = 1;
        collider.radius = radius;
        collider.height = Vector3.Distance(from, to) + radius * 2;
    }
}
