using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictHouse1Finish
{
    private const string Root = "Assets/PhotoReconstruction/Stage11North";
    private const string GroupName = "Север · ограда и зелень перед домом №1";
    private const string Prefabs = "Assets/StreetGenerated/Pilot/Prefabs";
    private const float South = 141.5f;
    private const float North = 172.8f;
    private const float FenceHeight = 1.12f;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before restoring the No.1 frontage.");
        if (GameObject.Find(GroupName))
            return;

        var house = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(item => item.address == "1");
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var route = access.routes.Single(item => item.address.StartsWith(house.address + " ", StringComparison.Ordinal)).points;
        var road = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        var sidewalk = road.surfaces.Single(collider => collider &&
            collider.name.StartsWith("Красная брусчатка,", StringComparison.Ordinal));
        var grass = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(collider =>
            collider.name == "Grass" && collider.sharedMesh && collider.bounds.size.x > 1000);
        Vector3 sidewalkEnd = route.Last();
        if (route.Length < 2 || Vector2.Distance(XZ(sidewalkEnd), new Vector2(-111.309f, 154.381f)) > .05f)
            throw new InvalidOperationException("The saved No.1 entry no longer matches its frontage.");

        Physics.SyncTransforms();
        Vector3 Foot(float z) => Base(grass, SidewalkEdge(sidewalk, sidewalkEnd, z) + .24f, z);
        float gapCenter = GapCenter(route, Foot);
        float nearGap = gapCenter - 1.18f, farGap = gapCenter + 1.18f;
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var paint = FencePaint();
        var group = new GameObject(GroupName);
        var fence = new EnvironmentMesh();
        AddRun(fence, paint, route, Foot, South, nearGap);
        AddRun(fence, paint, route, Foot, farGap, North);
        var rail = fence.Create("House1GreenFence", "Зелёная решётчатая ограда", Root, true);
        rail.transform.SetParent(group.transform, false);
        rail.AddComponent<MeshCollider>().sharedMesh = rail.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(rail, StaticEditorFlags.BatchingStatic);

        var planted = new List<Vector3>();
        int trees = 0;
        int shrubs = 0;
        foreach (var (prefab, z, inset, scale, yaw) in new[]
        {
            ("BroadleafV0H760", 144.4f, 3.1f, .92f, 68f),
            ("BroadleafV1H950", 148.6f, 3.0f, .84f, 146f),
            ("BroadleafV2H950", 159.9f, 3.7f, 1.01f, 210f),
            ("SpruceV1", 164.3f, 3.2f, .90f, 47f),
            ("BroadleafV3H950", 169.6f, 3.4f, .82f, 102f),
            ("Hedge2m", 142.8f, 1.18f, .75f, 32f),
            ("Hedge2m", 150.9f, 1.40f, .72f, 24f),
            ("Hedge2m", 157.7f, 1.35f, .70f, 26f),
            ("Hedge2m", 163.4f, 1.30f, .78f, 32f),
            ("Hedge2m", 170.5f, 1.35f, .80f, 34f)
        })
        {
            Vector3 front = Foot(z);
            bool isShrub = prefab == "Hedge2m";
            Vector3 position = Base(grass, front.x + inset, z);
            float radius = isShrub ? .72f : .55f;
            if (RouteDistance(route, position) < (isShrub ? 2f : 2.7f) ||
                Hits(sidewalk, position.x, z) ||
                planted.Any(point => Vector2.Distance(XZ(point), XZ(position)) < 1.65f) ||
                Physics.CheckCapsule(position + Vector3.up * 1.02f,
                    position + Vector3.up * (isShrub ? 1.02f : 3.7f), radius, ~0, QueryTriggerInteraction.Ignore))
                continue;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/{prefab}.prefab");
            if (!asset)
                throw new InvalidOperationException("Missing vegetation prefab " + prefab);
            var plant = (GameObject)PrefabUtility.InstantiatePrefab(asset, group.transform);
            plant.transform.SetPositionAndRotation(position, Quaternion.Euler(0, yaw, 0));
            plant.transform.localScale = Vector3.one * scale;
            ApplySeasonalFoliage(plant);
            planted.Add(position);
            if (isShrub) shrubs++;
            else trees++;
        }

        Physics.SyncTransforms();
        var collider = rail.GetComponent<MeshCollider>();
        Vector3 gate = Foot(gapCenter) + Vector3.up * .57f;
        if (collider.Raycast(new Ray(gate - Vector3.right, Vector3.right), out _, 2f) || trees < 2)
            throw new InvalidOperationException("The No.1 passage or planting failed the frontage checks.");
        var evidence = new GameObject("Фотоориентир · ограда перед №1") { tag = "EditorOnly" };
        evidence.transform.SetParent(group.transform, false);
        var note = evidence.AddComponent<PhotoSurveyMarker>();
        note.category = "№1 · зелёная ограда и посадки у красного тротуара";
        note.observation = "На панораме видны низкая зелёная ограда, проход к фасаду и деревья за решёткой.";
        note.uncertainty = "Высота секций и точки посадки оценены по панораме. Проход привязан к существующей дорожке в сцене.";
        note.sourceUrls = new[] {
            "https://yandex.ru/maps/57/khanty-mansiysk/house/ulitsa_dunina_gorkavicha_1/YkAYdgFgTkADQFhrfXxxcH1lYA==/panorama/?panorama%5Bpoint%5D=69.060451%2C61.001011&panorama%5Bdirection%5D=146.460156%2C-13.047619"
        };
        note.studyBounds = collider.bounds;
        note.geometryProvisional = true;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_HOUSE1 trees={trees} shrubs={shrubs} gateCenter={gate:F3}");
    }

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);

    private static bool Hits(MeshCollider surface, float x, float z) =>
        surface.Raycast(new Ray(new Vector3(x, 3, z), Vector3.down), out _, 5);

    private static Vector3 Base(MeshCollider grass, float x, float z)
    {
        if (!grass.Raycast(new Ray(new Vector3(x, 3, z), Vector3.down), out var hit, 5) || hit.normal.y < .85f)
            throw new InvalidOperationException($"No level ground for the No.1 frontage at {x:F2}, {z:F2}.");
        return hit.point;
    }

    private static float SidewalkEdge(MeshCollider sidewalk, Vector3 sidewalkEnd, float z)
    {
        float inside = sidewalkEnd.x + .54f * (sidewalkEnd.z - z);
        float outside = inside + 2.8f;
        if (!Hits(sidewalk, inside, z) || Hits(sidewalk, outside, z))
            throw new InvalidOperationException($"Cannot locate the inner edge of the No.1 sidewalk at z={z:F2}.");
        for (int i = 0; i < 13; i++)
        {
            float middle = (inside + outside) * .5f;
            if (Hits(sidewalk, middle, z)) inside = middle;
            else outside = middle;
        }
        return outside;
    }

    private static float GapCenter(Vector3[] route, Func<float, Vector3> foot)
    {
        for (int i = 1; i < route.Length; i++)
        {
            Vector3 a = route[i - 1], b = route[i];
            float aSide = a.x - foot(a.z).x, bSide = b.x - foot(b.z).x;
            if (aSide >= 0 && bSide < 0)
                return Vector3.Lerp(a, b, aSide / (aSide - bSide)).z;
        }
        throw new InvalidOperationException("The No.1 fence would not connect to the saved approach.");
    }

    private static float RouteDistance(Vector3[] route, Vector3 point)
    {
        float distance = float.PositiveInfinity;
        for (int i = 1; i < route.Length; i++)
        {
            Vector2 from = XZ(route[i - 1]), delta = XZ(route[i]) - from;
            float t = Mathf.Clamp01(Vector2.Dot(XZ(point) - from, delta) / Mathf.Max(.0001f, delta.sqrMagnitude));
            distance = Mathf.Min(distance, Vector2.Distance(XZ(point), from + t * delta));
        }
        return distance;
    }

    private static void AddRun(EnvironmentMesh mesh, Material paint, Vector3[] route,
        Func<float, Vector3> foot, float start, float end)
    {
        int sections = Mathf.CeilToInt((end - start) / 1.65f);
        Vector3 previous = foot(start);
        AddPost(mesh, paint, previous);
        for (int i = 1; i <= sections; i++)
        {
            Vector3 next = foot(Mathf.Lerp(start, end, i / (float)sections));
            float width = Vector3.Distance(previous, next);
            Vector3 mid = (previous + next) * .5f;
            if (width < .8f || width > 2.35f || Mathf.Abs(next.y - previous.y) > .16f ||
                Mathf.Min(RouteDistance(route, previous), RouteDistance(route, next), RouteDistance(route, mid)) < .89f)
                throw new InvalidOperationException("The No.1 fence section would narrow the existing approach.");
            AddPost(mesh, paint, next);
            Bar(mesh, paint, previous + Vector3.up * .20f, next + Vector3.up * .20f, .036f);
            Bar(mesh, paint, previous + Vector3.up * .98f, next + Vector3.up * .98f, .041f);
            int pickets = Mathf.RoundToInt(width / .126f);
            for (int p = 1; p < pickets; p++)
            {
                Vector3 stem = Vector3.Lerp(previous, next, p / (float)pickets);
                mesh.Box(paint, stem + Vector3.up * .56f, new Vector3(.017f, .87f, .017f));
                if (p % 2 == 1 && p + 1 < pickets)
                {
                    Vector3 top = stem + Vector3.up * .98f;
                    Vector3 step = (next - previous) / pickets;
                    Bar(mesh, paint, top, top + step * .22f + Vector3.up * .067f, .013f);
                    Bar(mesh, paint, top + step * .22f + Vector3.up * .067f, top + step * .5f + Vector3.up * .087f, .013f);
                    Bar(mesh, paint, top + step * .5f + Vector3.up * .087f, top + step * .78f + Vector3.up * .067f, .013f);
                    Bar(mesh, paint, top + step * .78f + Vector3.up * .067f, top + step, .013f);
                }
            }
            previous = next;
        }
    }

    private static void AddPost(EnvironmentMesh mesh, Material paint, Vector3 basePoint)
    {
        mesh.Box(paint, basePoint + Vector3.up * (FenceHeight * .5f - .02f), new Vector3(.059f, FenceHeight + .04f, .059f));
        mesh.Box(paint, basePoint + Vector3.up * FenceHeight, new Vector3(.072f, .026f, .072f));
    }

    private static void Bar(EnvironmentMesh mesh, Material paint, Vector3 a, Vector3 b, float size)
    {
        Vector3 direction = b - a;
        mesh.Box(paint, (a + b) * .5f, new Vector3(size, size, direction.magnitude + .005f), Quaternion.LookRotation(direction));
    }

    private static Material FencePaint()
    {
        string path = Root + "/FenceGreen.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material)
            return material;
        var source = AssetDatabase.LoadAssetAtPath<Material>("Assets/StreetGenerated/Pilot/Materials/GraphiteMetal.mat");
        if (!source || source.shader.name != "Street/TexturedSurface")
            throw new InvalidOperationException("Missing the painted metal surface for the No.1 fence.");
        material = new Material(source);
        material.SetColor("_Color", new Color(.22f, .43f, .30f));
        material.SetFloat("_Colorize", .9f);
        material.SetFloat("_Metallic", .18f);
        material.SetFloat("_RoughnessBias", .12f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void ApplySeasonalFoliage(GameObject plant)
    {
        const string autumn = "Assets/PhotoReconstruction/Stage08Autumn/Materials";
        foreach (var renderer in plant.GetComponentsInChildren<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i])
                    continue;
                string name = materials[i].name;
                if (name != "ModelLeaves" && name != "HedgeLeaves")
                    continue;
                string id = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(materials[i]));
                var seasonal = AssetDatabase.LoadAssetAtPath<Material>($"{autumn}/{name}_{id}.mat");
                if (seasonal)
                {
                    materials[i] = seasonal;
                    changed = true;
                }
            }
            if (!changed)
                continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
    }
}
