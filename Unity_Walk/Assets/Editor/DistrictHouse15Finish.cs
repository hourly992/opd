using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Replace the isolated red rectangle and its surrounding asphalt with one connected profile.
public static class DistrictHouse15Finish
{
    private const string Root = "Assets/PhotoReconstruction/Stage11South";
    private const string GroupName = "Дом №15 · непрерывный подход";
    private const string Address = "15 ";
    private const float AxisZ = -107.20925f;
    private const float Near = -109.80f;
    private const float Far = -104.65f;
    private const float CrossingHalfWidth = .90f;
    private const string ReferencePhoto = "https://avatars.mds.yandex.net/get-altay/18116908/2a0000019e4a3c0752c03f837c748d226820/orig";
    private const string Panorama = "https://yandex.ru/maps/57/khanty-mansiysk/house/ulitsa_dunina_gorkavicha_15/YkAYdgFjT0cHQFhqfXV4eXtlZQ==/panorama/?panorama%5Bpoint%5D=69.063256%2C60.998531&panorama%5Bdirection%5D=148.390169%2C-15.142857";
    private static readonly float[] CrossSection =
    {
        71.10f, 71.54f, 71.64f, 72.66f, 72.74f, 72.79f,
        72.91f, 73.45f, 74.16f, 74.21f, 74.29f, 74.34f
    };
    private static readonly float[] EdgeHeights =
    {
        0, 0, -.018f, -.018f, -.010f, .016f, 0, 0, 0, .024f, .024f, 0
    };

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before updating No.15.");
        if (GameObject.Find(GroupName))
            return;

        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var street = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        if (!access || !street)
            throw new InvalidOperationException("The saved street surfaces and access routes are required.");
        var route = access.routes.Single(r => r.address.StartsWith(Address, StringComparison.Ordinal) &&
            r.address.EndsWith("к крыльцу 10", StringComparison.Ordinal));
        if (Mathf.Abs(route.points[0].z - AxisZ) > .015f || Mathf.Abs(route.points[0].x - 72.81f) > .03f ||
            !route.points.All(point => Mathf.Abs(point.z - AxisZ) < .02f))
            throw new InvalidOperationException("The No.15 entrance and its saved access route must agree with the edited apron.");

        var asphalt = Surface("Объединённый асфальт проездов и въездов");
        var paving = Surface("Скруглённое мощение · 2");
        var oldFloors = new[] { asphalt, paving };
        Physics.SyncTransforms();
        var red = paving.GetComponent<MeshRenderer>().sharedMaterials.Single(material => material.name == "PhotoRedRunningBond");
        var kerb = SceneMaterial("PhotoWhitewashedKerb");
        var lawn = SceneMaterial("RoadVergeSurface");
        var concrete = SceneMaterial("EntranceConcrete");

        // Sample the old apron instead of assigning it an absolute elevation.
        var rows = Stations();
        var vertices = new Vector3[rows.Length, CrossSection.Length];
        for (int row = 0; row < rows.Length; row++)
        {
            float z = rows[row];
            float endFade = Mathf.Clamp01(Mathf.Min(z - Near, Far - z) / .38f);
            float entranceFade = Mathf.Clamp01((Mathf.Abs(z - AxisZ) - CrossingHalfWidth) / .42f);
            for (int col = 0; col < CrossSection.Length; col++)
            {
                float x = CrossSection[col];
                if (!Highest(oldFloors, x, z, out float height))
                    throw new InvalidOperationException($"Missing saved No.15 support at ({x:F3}, {z:F3}).");
                vertices[row, col] = new Vector3(x, height + EdgeHeights[col] * endFade * entranceFade, z);
            }
        }

        var mesh = new EnvironmentMesh();
        float minimumNormal = 1;
        for (int row = 1; row < rows.Length; row++)
        {
            bool crossing = Mathf.Abs((rows[row - 1] + rows[row]) * .5f - AxisZ) < CrossingHalfWidth;
            for (int col = 1; col < CrossSection.Length; col++)
            {
                Material material = col >= 9 ? kerb : crossing ? red :
                    col == 1 ? concrete : col < 5 ? lawn : col < 7 ? kerb : red;
                Vector3 a = vertices[row - 1, col - 1];
                Vector3 b = vertices[row - 1, col];
                Vector3 c = vertices[row, col];
                Vector3 d = vertices[row, col - 1];
                AddTop(mesh, material, a, c, b, ref minimumNormal);
                AddTop(mesh, material, a, d, c, ref minimumNormal);
            }
        }
        if (minimumNormal < .85f)
            throw new InvalidOperationException($"The No.15 apron has an excessive surface angle: normal y={minimumNormal:F3}.");

        var before = route.points.Select(point =>
        {
            if (!Highest(oldFloors, point.x, point.z, out float height))
                throw new InvalidOperationException("The prior No.15 route no longer has hard support.");
            return height;
        }).ToArray();
        float west = CrossSection[0], east = CrossSection[CrossSection.Length - 1];
        var border = new List<Vector2>
        {
            new Vector2(west, Near), new Vector2(east, Near),
            new Vector2(east, Far), new Vector2(west, Far)
        };
        var probes = access.surfaceChecks.Where(point => point.x >= west && point.x <= east &&
            point.z >= Near && point.z <= Far && access.surfaces.Any(surface => FloorAt(surface, point))).ToArray();

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var oldAsphaltMesh = asphalt.sharedMesh;
        var oldPavingMesh = paving.sharedMesh;
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(
            oldFloors.Select(collider => collider.GetComponent<MeshFilter>()).ToArray(),
            new List<List<Vector2>> { border }, Root);
        if (asphalt.sharedMesh == oldAsphaltMesh || paving.sharedMesh == oldPavingMesh)
            throw new InvalidOperationException("The old asphalt and isolated red patch must both be clipped from the new footprint.");

        var root = new GameObject(GroupName);
        var apron = mesh.Create("House15Apron", "Асфальт, бордюр, брусчатка и отмостка · узел №15", Root);
        apron.transform.SetParent(root.transform, false);
        var collider = apron.AddComponent<MeshCollider>();
        collider.sharedMesh = apron.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(apron, StaticEditorFlags.BatchingStatic);
        access.surfaces = access.surfaces.Append(collider).ToArray();
        EditorUtility.SetDirty(access);
        if (street.surfaces.Contains(asphalt) || street.surfaces.Contains(paving))
        {
            street.surfaces = street.surfaces.Append(collider).ToArray();
            EditorUtility.SetDirty(street);
        }
        Physics.SyncTransforms();
        int profileSamples = CheckNewProfile(collider, oldFloors, vertices, rows);
        float largestChange = 0;
        for (int index = 0; index < route.points.Length; index++)
        {
            Vector3 point = route.points[index];
            if (!Highest(new[] { collider, asphalt, paving }, point.x, point.z, out float height))
                throw new InvalidOperationException("The updated No.15 route lost its hard support.");
            largestChange = Mathf.Max(largestChange, Mathf.Abs(height - before[index]));
        }
        if (largestChange > .012f || probes.Any(point => !access.surfaces.Any(surface => FloorAt(surface, point))))
            throw new InvalidOperationException("The updated No.15 apron changed a saved route or landing.");

        var markerObject = new GameObject("Фотоориентир · профиль покрытий №15") { tag = "EditorOnly" };
        markerObject.transform.SetParent(root.transform, false);
        var marker = markerObject.AddComponent<PhotoSurveyMarker>();
        marker.category = "Дом №15 · сопряжение покрытий у жилого крыльца";
        marker.observation = "Адресный кадр показывает порядок покрытий от проезда к фасаду: светлый бордюр, красная плитка, газон и серая отмостка. Изолированная плитка у крыльца соединена с продольной дорожкой.";
        marker.uncertainty = "Снимок Mr.Shkaf показывает отдельный низкий коммерческий вход. Профиль покрытий применён к существующему жилому подходу; его габариты оценочные и основаны на доступной площади текущей модели.";
        marker.sourceUrls = new[] { ReferencePhoto, Panorama };
        marker.studyBounds = collider.bounds;
        marker.geometryProvisional = true;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STREET_HOUSE15 profileSamples={profileSamples} accessSamples={probes.Length} largestRouteChange={largestChange:F4} materialBands=4");
    }

    private static float[] Stations()
    {
        var cuts = new[] { Near, Near + .38f, AxisZ - CrossingHalfWidth - .42f,
            AxisZ - CrossingHalfWidth, AxisZ, AxisZ + CrossingHalfWidth,
            AxisZ + CrossingHalfWidth + .42f, Far - .38f, Far };
        var rows = new List<float> { cuts[0] };
        for (int interval = 1; interval < cuts.Length; interval++)
        {
            int steps = Mathf.CeilToInt((cuts[interval] - cuts[interval - 1]) / .22f);
            for (int step = 1; step <= steps; step++)
                rows.Add(Mathf.Lerp(cuts[interval - 1], cuts[interval], (float)step / steps));
        }
        return rows.ToArray();
    }

    private static void AddTop(EnvironmentMesh mesh, Material material, Vector3 a, Vector3 b, Vector3 c,
        ref float minimumNormal)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        if (normal.y < 0)
            normal = -normal;
        minimumNormal = Mathf.Min(minimumNormal, normal.y);
        mesh.Face(material, normal, a, b, c);
    }

    private static int CheckNewProfile(MeshCollider apron, MeshCollider[] oldFloors, Vector3[,] vertices, float[] rows)
    {
        int samples = 0;
        for (int row = 1; row < rows.Length; row++)
        for (int col = 1; col < CrossSection.Length; col++)
        {
            Vector3 a = vertices[row - 1, col - 1], c = vertices[row, col];
            foreach (Vector3 opposite in new[] { vertices[row - 1, col], vertices[row, col - 1] })
            {
                Vector3 point = (a + c + opposite) / 3;
                if (!Cast(apron, point.x, point.z, out var hit) || Mathf.Abs(hit.point.y - point.y) > .002f ||
                    Highest(oldFloors, point.x, point.z, out _))
                    throw new InvalidOperationException($"The No.15 replacement has a hole or covered old geometry at {point:F3}.");
                samples++;
            }
        }
        return samples;
    }

    private static Material SceneMaterial(string name) =>
        UnityEngine.Object.FindObjectsByType<MeshRenderer>().SelectMany(renderer => renderer.sharedMaterials)
            .First(material => material && material.name == name);

    private static MeshCollider Surface(string name)
    {
        var target = GameObject.Find(name);
        var collider = target ? target.GetComponent<MeshCollider>() : null;
        var filter = target ? target.GetComponent<MeshFilter>() : null;
        if (!collider || !filter || !collider.enabled || !filter.sharedMesh || collider.sharedMesh != filter.sharedMesh)
            throw new InvalidOperationException($"The saved surface '{name}' needs a common renderer and collision mesh.");
        return collider;
    }

    private static bool Highest(IEnumerable<MeshCollider> surfaces, float x, float z, out float height)
    {
        height = float.NegativeInfinity;
        foreach (var surface in surfaces)
            if (Cast(surface, x, z, out var hit))
                height = Mathf.Max(height, hit.point.y);
        return float.IsFinite(height);
    }

    private static bool FloorAt(MeshCollider surface, Vector3 point) =>
        Cast(surface, point.x, point.z, out var hit) && Mathf.Abs(hit.point.y - point.y) < .06f;

    private static bool Cast(MeshCollider surface, float x, float z, out RaycastHit hit)
    {
        hit = default;
        return surface && surface.Raycast(new Ray(new Vector3(x, .5f, z), Vector3.down), out hit, .75f) &&
            hit.normal.y >= .82f;
    }
}
