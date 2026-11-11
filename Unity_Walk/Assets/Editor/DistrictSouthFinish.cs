using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictSouthFinish
{
    private const string AssetRoot = "Assets/PhotoReconstruction/Stage10South";
    private const string GroupName = "Южные дворы · стыки покрытий Stage10";
    private const string AsphaltObject = "Объединённый асфальт проездов и въездов";
    private const string PavingObject = "Скруглённое мощение · 2";
    private const string ShopPhoto = "https://avatars.mds.yandex.net/get-altay/18116908/2a0000019e4a3c0752c03f837c748d226820/orig";
    private const string Panorama = "https://yandex.ru/maps/57/khanty-mansiysk/house/ulitsa_dunina_gorkavicha_15/YkAYdgFjT0cHQFhqfXV4eXtlZQ==/panorama/?panorama%5Bpoint%5D=69.063256%2C60.998531&panorama%5Bdirection%5D=148.390169%2C-15.142857";

    private static readonly Site[] Sites =
    {
        new Site("8", Rect.MinMaxRect(-21.35f, -105.03f, -18.70f, -103.00f),
            "https://yandex.ru/maps/org/51912465174/"),
        new Site("10", Rect.MinMaxRect(14.00f, -175.82f, 16.33f, -173.65f),
            "https://avatars.mds.yandex.net/get-altay/15283141/2a0000019526cc983b626cdbb2c36b02fe1b/orig"),
        new Site("15", Rect.MinMaxRect(72.70f, -108.10f, 74.42f, -106.30f), ShopPhoto)
    };

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before repairing the southern approaches.");
        if (GameObject.Find(GroupName))
            return;

        var asphalt = Surface(AsphaltObject);
        var paving = Surface(PavingObject);
        var original = asphalt.sharedMesh;
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if (!access || !original || !asphalt.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m && m.name == "ReferenceServiceAsphalt"))
            throw new InvalidOperationException("The saved secondary asphalt and residential checks are required.");

        Physics.SyncTransforms();
        var polygons = new List<List<Vector2>>();
        var overlapChecks = new List<Vector3>();
        var sources = paving.sharedMesh.vertices;
        var indices = paving.sharedMesh.triangles;
        int[] counts = new int[Sites.Length];
        for (int siteIndex = 0; siteIndex < Sites.Length; siteIndex++)
        {
            var area = Sites[siteIndex].Area;
            for (int index = 0; index < indices.Length; index += 3)
            {
                Vector3 a = paving.transform.TransformPoint(sources[indices[index]]);
                Vector3 b = paving.transform.TransformPoint(sources[indices[index + 1]]);
                Vector3 c = paving.transform.TransformPoint(sources[indices[index + 2]]);
                if (!area.Contains(XZ(a)) || !area.Contains(XZ(b)) || !area.Contains(XZ(c)))
                    continue;
                // The clipper accepts counterclockwise XZ polygons. An upwards Unity
                // triangle has the reverse winding when projected into this plane.
                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.y < .00006f || normal.normalized.y < .9f)
                    continue;
                var center = (a + b + c) / 3;
                if (!Cast(asphalt, center, out var asphaltHit) || Mathf.Abs(asphaltHit.point.y - center.y) > .028f)
                    continue;
                polygons.Add(new List<Vector2> { XZ(a), XZ(c), XZ(b) });
                overlapChecks.Add(center);
                counts[siteIndex]++;
            }
            if (counts[siteIndex] == 0)
                throw new InvalidOperationException($"No saved near-coplanar asphalt overlap was found at house {Sites[siteIndex].Address}.");
        }

        // Keep the existing red triangles, entrance locations, slopes and level ties.
        // Only subtract their bounded visible footprints from the duplicate asphalt.
        var routeChecks = access.surfaceChecks.Where(point => Sites.Any(site => Expanded(site.Area, 2).Contains(XZ(point))))
            .Where(point => IsPaved(access.surfaces, point)).ToArray();
        var verge = new Vector3(44.36f, .17f, -129.45f);
        var street = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        var vergeFloor = street ? street.surfaces.FirstOrDefault(collider =>
            collider && collider.GetComponent<MeshRenderer>() &&
            collider.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m && m.name == "RoadVergeSurface")) : null;
        bool vergePresent = vergeFloor && Cast(vergeFloor, verge, out _);

        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        var filter = asphalt.GetComponent<MeshFilter>();
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(new[] { filter }, polygons, AssetRoot);
        Physics.SyncTransforms();
        if (filter.sharedMesh == original || filter.sharedMesh != asphalt.sharedMesh ||
            !AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(AssetRoot + "/Meshes/", StringComparison.Ordinal))
            throw new InvalidOperationException("The revised asphalt renderer and collider must use the same cloned mesh.");
        foreach (var point in overlapChecks)
            if (Cast(asphalt, point, out _) || !Cast(paving, point, out var hit) || Mathf.Abs(hit.point.y - point.y) > .002f)
                throw new InvalidOperationException($"Southern paving has an uncut overlap or a hole at {point:F3}.");
        if (routeChecks.Any(point => !IsPaved(access.surfaces, point)))
            throw new InvalidOperationException("A southern approach lost support after clipping the asphalt.");

        var parent = new GameObject(GroupName);
        parent.tag = "EditorOnly";
        foreach (var site in Sites)
            AddEvidence(parent.transform, site);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STREET_SOUTH_FINISH overlapTrianglesByAddress=8:{counts[0]},10:{counts[1]},15:{counts[2]}; " +
            $"retainedApproachChecks={routeChecks.Length}; savedRoadsideVerge={vergePresent}; originalElevations=true.");
    }

    private static MeshCollider Surface(string name)
    {
        var target = GameObject.Find(name);
        var collider = target ? target.GetComponent<MeshCollider>() : null;
        var filter = target ? target.GetComponent<MeshFilter>() : null;
        if (!collider || !filter || !collider.enabled || !filter.sharedMesh || collider.sharedMesh != filter.sharedMesh)
            throw new InvalidOperationException($"The saved surface '{name}' must share the renderer and collision mesh.");
        return collider;
    }

    private static bool Cast(MeshCollider mesh, Vector3 point, out RaycastHit hit) =>
        mesh.Raycast(new Ray(point + Vector3.up * .55f, Vector3.down), out hit, 1f) && hit.normal.y > .85f;

    private static bool IsPaved(MeshCollider[] surfaces, Vector3 point) =>
        surfaces.Any(surface => surface && Cast(surface, point, out var hit) && Mathf.Abs(hit.point.y - point.y) < .06f);

    private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);

    private static Rect Expanded(Rect rect, float distance) =>
        Rect.MinMaxRect(rect.xMin - distance, rect.yMin - distance, rect.xMax + distance, rect.yMax + distance);

    private static void AddEvidence(Transform parent, Site site)
    {
        var go = new GameObject("№" + site.Address + " · адресная сверка");
        go.transform.SetParent(parent, false);
        var note = go.AddComponent<PhotoSurveyMarker>();
        note.category = "Юг · стык асфальта с существующей брусчаткой";
        note.observation = site.Address == "15"
            ? "В адресном кадре видны красная плитка, светлая кромка, газон и серая отмостка. Существующий тротуар и примыкающая полоса газона сохранены. На модели устранено наложение асфальта на красный подход."
            : "Сохранённый подход проверен по коллайдерам. В выделенном месте красная плитка и асфальт занимали почти один уровень. Избыточная поверхность асфальта отсечена по границе плитки.";
        note.uncertainty = "Подрезка соответствует геометрии текущей сцены. Адресное фото не даёт привязки к конкретному порогу и не задаёт отметок грунта.";
        note.sourceUrls = site.Address == "15" ? new[] { site.Source, Panorama } : new[] { site.Source };
        note.geometryProvisional = true;
        note.studyBounds = new Bounds(new Vector3(site.Area.center.x, .1f, site.Area.center.y),
            new Vector3(site.Area.width, .4f, site.Area.height));
    }

    private readonly struct Site
    {
        public readonly string Address;
        public readonly Rect Area;
        public readonly string Source;

        public Site(string address, Rect area, string source)
        {
            Address = address;
            Area = area;
            Source = source;
        }
    }
}
