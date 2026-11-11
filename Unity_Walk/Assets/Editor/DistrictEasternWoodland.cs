using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictEasternWoodland
{
    private const string GroupName = "Восток · опушка за дворами №13";
    private const string BacklineName = "Восток · дальняя часть опушки №13";
    private const string PrefabFolder = "Assets/StreetGenerated/Pilot/Prefabs";
    private const string AutumnLeaves =
        "Assets/PhotoReconstruction/Stage08Autumn/Materials/ModelLeaves_4f978f52c39ddb0409adbd0902169c30.mat";

    // The front edge follows the observed canopy on the rectified satellite
    // frame. The rear limit is inside that frame's coverage.
    private static readonly Vector3[] Boundary =
    {
        Point(97, 141), Point(110, 140), Point(127, 139), Point(137, 137),
        Point(162, 136), Point(180, 140), Point(196, 137), Point(196, 163),
        Point(112, 163), Point(97, 153)
    };

    // Rear canopy visible north of the first strip on the same satellite frame.
    private static readonly Vector3[] Backline =
    {
        Point(115, 163), Point(149, 163), Point(176, 164), Point(196, 163),
        Point(204, 165), Point(202, 184), Point(170, 187), Point(125, 185), Point(115, 178)
    };

    private static readonly string[] PrefabNames =
    {
        "Spruce", "SpruceV1", "SpruceV2", "SpruceV3",
        "BroadleafV0H950", "BroadleafV1H950", "BroadleafV2H950", "BroadleafV3H950"
    };

    public static void Apply() => Plant(GroupName, Boundary, 90, 3.4f, 56, 130711);

    internal static void ApplyBackdrop()
    {
        Plant(BacklineName, Backline, 46, 4.5f, 26, 131109);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = .0023f;
        RenderSettings.fogColor = new Color(.57f, .68f, .70f);
    }

    private static void Plant(string groupName, Vector3[] boundary, int target, float spacing, int minimum, int seed)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before restoring the eastern woodland.");
        if (GameObject.Find(groupName))
            return;

        bool backline = groupName == BacklineName;
        var ground = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(collider =>
            collider.enabled && collider.name == "Grass" && collider.sharedMesh && collider.bounds.size.x > 1000);
        var leaves = AssetDatabase.LoadAssetAtPath<Material>(AutumnLeaves);
        var prefabs = PrefabNames.Select(name => AssetDatabase.LoadAssetAtPath<GameObject>(
            $"{PrefabFolder}/{name}.prefab")).ToArray();
        if (!leaves || prefabs.Any(prefab => !prefab || !prefab.GetComponent<LODGroup>()))
            throw new InvalidOperationException("The saved seasonal tree materials and LOD prefabs are required.");

        Physics.SyncTransforms();
        var existing = backline
            ? UnityEngine.Object.FindObjectsByType<LODGroup>().Select(lod => lod.transform.position)
                .Where(point => point.x > 105 && point.x < 214 && point.z > 155 && point.z < 197).ToArray()
            : Array.Empty<Vector3>();
        var sites = FindSites(ground, UnityEngine.Object.FindObjectsByType<PlaygroundSite>(), boundary,
            existing, target, spacing, seed);
        if (sites.Count < minimum || !backline && Enumerable.Range(0, 4).Any(band =>
                sites.Count(point => point.x >= 97 + 25 * band && point.x < 122 + 25 * band) < 9))
            throw new InvalidOperationException("There is not enough clear ground along the photographed woodland edge.");

        var group = new GameObject(groupName);
        var style = new System.Random(backline ? 81339 : 81338);
        int evergreens = 0;
        foreach (var point in sites)
        {
            bool conifer = style.NextDouble() < .58;
            int variant = style.Next(4);
            var tree = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[variant + (conifer ? 0 : 4)], group.transform);
            float scale = Mathf.Lerp(.74f, 1.16f, (float)style.NextDouble());
            tree.transform.SetPositionAndRotation(point, Quaternion.Euler(0, style.Next(360), 0));
            tree.transform.localScale = Vector3.one * scale;
            if (conifer)
                evergreens++;
            else
                ApplyAutumnLeaves(tree, leaves);

            var lodGroup = tree.GetComponent<LODGroup>();
            var lods = lodGroup.GetLODs();
            float[] thresholds = lods.Length == 4 ? new[] { .55f, .27f, .11f, .014f } : new[] { .36f, .12f, .014f };
            for (int i = 0; i < lods.Length; i++)
                lods[i].screenRelativeTransitionHeight = thresholds[Mathf.Min(i, thresholds.Length - 1)];
            lodGroup.SetLODs(lods);
            PrefabUtility.RecordPrefabInstancePropertyModifications(lodGroup);
        }

        var evidence = new GameObject("Фотоориентир · восточная опушка") { tag = "EditorOnly" };
        evidence.transform.SetParent(group.transform, false);
        var marker = evidence.AddComponent<PhotoSurveyMarker>();
        marker.category = backline ? "Восточный сектор · второй ряд леса" : "Восточный сектор · лес за дворами";
        marker.observation = backline
            ? "На снимке лес продолжается вглубь севернее первой полосы. Посадка ограничена видимым пологом."
            : "На спутниковом кадре опушка проходит за открытой площадкой и северными проездами домов №13.";
        marker.uncertainty = "Граница приближённо перенесена по спутниковому кадру. Положение отдельных деревьев, высоты и породный состав оценочные.";
        marker.sourceUrls = new[]
        {
            "https://yandex.ru/maps/57/khanty-mansiysk/?l=sat&ll=69.065496%2C61.000576&z=19",
            "https://img.geliophoto.com/khanty2019/54_khanty2019.jpg"
        };
        marker.studyBounds = backline
            ? new Bounds(new Vector3(159.5f, 5, 175), new Vector3(89, 20, 24))
            : new Bounds(new Vector3(146.5f, 5, 149.5f), new Vector3(99, 20, 27));
        marker.geometryProvisional = true;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"{(backline ? "STAGE13_BACKLINE" : "DISTRICT_EAST_WOODLAND")} trees={sites.Count} conifers={evergreens}");
    }

    private static List<Vector3> FindSites(MeshCollider ground, PlaygroundSite[] playgrounds, Vector3[] boundary,
        Vector3[] existing, int target, float spacing, int seed)
    {
        var result = new List<Vector3>(target);
        var random = new System.Random(seed);
        float west = boundary.Min(p => p.x), east = boundary.Max(p => p.x);
        float south = boundary.Min(p => p.z), north = boundary.Max(p => p.z);
        for (int attempt = 0; attempt < 4800 && result.Count < target; attempt++)
        {
            var point = Point(Mathf.Lerp(west, east, (float)random.NextDouble()),
                Mathf.Lerp(south, north, (float)random.NextDouble()));
            if (!GeographicLandscape.Contains(boundary, point) ||
                result.Concat(existing).Any(other => (new Vector2(other.x - point.x, other.z - point.z)).sqrMagnitude < spacing * spacing) ||
                playgrounds.Any(playground => playground.footprint != null && playground.footprint.Length > 2 &&
                    GeographicLandscape.Contains(playground.footprint, point)))
                continue;

            if (IsClearGround(ground, point, out var hit) &&
                IsClearGround(ground, point + Vector3.left * 1.4f, out _) &&
                IsClearGround(ground, point + Vector3.right * 1.4f, out _) &&
                IsClearGround(ground, point + Vector3.forward * 1.4f, out _) &&
                IsClearGround(ground, point + Vector3.back * 1.4f, out _))
                result.Add(hit.point);
        }
        return result;
    }

    private static bool IsClearGround(MeshCollider ground, Vector3 point, out RaycastHit hit) =>
        Physics.Raycast(point + Vector3.up * 35, Vector3.down, out hit, 40, ~0, QueryTriggerInteraction.Ignore) &&
        hit.collider == ground && hit.normal.y > .82f;

    private static void ApplyAutumnLeaves(GameObject tree, Material leaves)
    {
        foreach (var renderer in tree.GetComponentsInChildren<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            int index = Array.FindIndex(materials, material => material && material.name == "ModelLeaves");
            if (index < 0)
                continue;
            materials[index] = leaves;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
    }

    private static Vector3 Point(float x, float z) => new Vector3(x, 0, z);
}
