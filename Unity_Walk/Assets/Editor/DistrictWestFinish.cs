using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictWestFinish
{
    private const string Root = "Assets/PhotoReconstruction/Stage10West";
    private const string Group = "Запад · подход к библиотеке №6";
    private const string EntranceName = "Access · торцевой вход 4";
    private const string Photo = "https://avatars.mds.yandex.net/get-altay/986332/2a00000163b68406656e7a5f1e6fb3210d56/orig";
    private const float Front = -49.62f;
    private const float Back = -48.72f;
    private const float East = -42.88f;
    private const float ApronY = .018f;
    private const int Sections = 14;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before repairing the library approach.");
        if (GameObject.Find(Group))
            return;

        var house = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b => b.address == "6");
        var entrance = house.GetComponentsInChildren<Transform>(true).Single(t => t.name == EntranceName);
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var route = access.routes.Single(r => r.address.StartsWith("6 ", StringComparison.Ordinal) &&
            r.address.EndsWith(EntranceName, StringComparison.Ordinal));
        if ((entrance.position - new Vector3(-46.623085f, 0, -47.697f)).sqrMagnitude > .001f ||
            Vector3.Dot(entrance.forward, Vector3.back) < .999f)
            throw new InvalidOperationException("The library approach no longer matches its photographed end facade.");

        Physics.SyncTransforms();
        var red = GameObject.Find("Скруглённое мощение · 1").GetComponent<MeshCollider>();
        var asphalt = UnityEngine.Object.FindObjectsByType<MeshCollider>()
            .Single(c => c.GetComponent<MeshRenderer>()?.sharedMaterials.Any(m =>
                m && m.name == "ReferenceServiceAsphalt") == true &&
                Hit(c, -45, -48.83f, out var h) && Mathf.Abs(h.point.y - ApronY) < .006f);
        var asphaltFilter = asphalt.GetComponent<MeshFilter>();
        if (!red || !asphaltFilter || asphaltFilter.sharedMesh != asphalt.sharedMesh)
            throw new InvalidOperationException("The saved red path and asphalt apron require matching physics surfaces.");
        var before = route.points.Select(SurfaceLevel).ToArray();

        // A short concrete apron joins the exposed east edge of the existing red path.
        // Snap each section to the actual red surface, with the old asphalt trimmed underneath.
        var west = new Vector3[Sections + 1];
        var east = new Vector3[Sections + 1];
        for (int i = 0; i <= Sections; i++)
        {
            float z = Mathf.Lerp(Front, Back, (float)i / Sections);
            float lo = RouteX(route.points, z), hi = lo + 2.25f;
            if (!Hit(red, lo, z, out _) || Hit(red, hi, z, out _))
                throw new InvalidOperationException($"Could not isolate the library red paving edge at z={z:F3}.");
            for (int step = 0; step < 14; step++)
            {
                float mid = (lo + hi) * .5f;
                if (Hit(red, mid, z, out _)) lo = mid;
                else hi = mid;
            }
            if (!Hit(red, lo - .003f, z, out var lip) || Mathf.Abs(lip.point.y - ApronY) > .05f)
                throw new InvalidOperationException("The red paving edge cannot be joined safely to the apron.");
            west[i] = new Vector3(hi, lip.point.y, z);
            east[i] = new Vector3(East, ApronY, z);
        }

        const float wallFront = -49.23f, wallBack = -48.99f, wallRight = East - .10f;
        float wallLeft = west.Where(v => v.z > wallFront - .10f && v.z < wallBack + .10f).Max(v => v.x) + .08f;
        CheckClearance(route.points, wallLeft, wallRight, wallFront, wallBack);

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var concrete = Finish("LibraryApronConcrete", new Color(.84f, .85f, .82f), 1.45f);
        var paintedBody = Finish("LibraryParapetConcrete", new Color(.93f, .93f, .89f), 1.15f);
        var paintedTop = Finish("LibraryParapetDarkEdge", new Color(.32f, .34f, .33f), .85f);
        var mesh = new EnvironmentMesh();
        var cuts = new List<List<Vector2>>();
        for (int i = 1; i < west.Length; i++)
        {
            var a = west[i - 1]; var b = east[i - 1]; var c = east[i]; var d = west[i];
            mesh.Face(concrete, Vector3.Cross(d - a, b - a).normalized, a, b, c);
            mesh.Face(concrete, Vector3.Cross(d - a, c - a).normalized, a, c, d);
            mesh.Face(concrete, Vector3.right, b, c, Bottom(c), Bottom(b));
            cuts.Add(new List<Vector2> { XZ(a), XZ(b), XZ(c), XZ(d) });
        }
        mesh.Face(concrete, Vector3.back, west[0], east[0], Bottom(east[0]), Bottom(west[0]));
        mesh.Face(concrete, Vector3.forward, east[Sections], west[Sections], Bottom(west[Sections]), Bottom(east[Sections]));

        var oldMesh = asphalt.sharedMesh;
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(new[] { asphaltFilter }, cuts, Root);
        if (asphaltFilter.sharedMesh == oldMesh || asphalt.sharedMesh != asphaltFilter.sharedMesh)
            throw new InvalidOperationException("The old overlapping asphalt was not cut under the library apron.");

        var root = new GameObject(Group);
        var apron = Create(mesh, "Library6ConcreteApron", "Бетонная полоса у парапета", root.transform);
        var wallMesh = new EnvironmentMesh();
        float width = wallRight - wallLeft, depth = wallBack - wallFront;
        float x = (wallLeft + wallRight) * .5f, zWall = (wallFront + wallBack) * .5f;
        const float foundation = -.035f, paintLine = .32f, wallTop = .44f;
        wallMesh.Box(paintedBody, new Vector3(x, (foundation + paintLine) * .5f, zWall),
            new Vector3(width, paintLine - foundation, depth));
        wallMesh.Box(paintedTop, new Vector3(x, (paintLine + wallTop) * .5f, zWall),
            new Vector3(width, wallTop - paintLine, depth));
        Create(wallMesh, "Library6Parapet", "Бетонный парапет · видимая часть", root.transform);

        access.surfaces = access.surfaces.Append(apron).ToArray();
        EditorUtility.SetDirty(access);
        var road = UnityEngine.Object.FindAnyObjectByType<StreetRoadNetwork>();
        if (road && road.surfaces.Contains(asphalt))
        {
            road.surfaces = road.surfaces.Append(apron).ToArray();
            EditorUtility.SetDirty(road);
        }

        Physics.SyncTransforms();
        float maxJoint = CheckApron(apron, red, asphalt, west, east);
        for (int i = 0; i < route.points.Length; i++)
            if (Mathf.Abs(SurfaceLevel(route.points[i]) - before[i]) > .025f)
                throw new InvalidOperationException("Library access changed its existing traversable profile.");

        var evidence = new GameObject("Фотоориентир · библиотека №6") { tag = "EditorOnly" };
        evidence.transform.SetParent(root.transform, false);
        var marker = evidence.AddComponent<PhotoSurveyMarker>();
        marker.category = "Дом №6 · библиотечный подход и парапет";
        marker.observation = "Красная дорожка примыкает слева к бетонной полосе перед светлым парапетом с тёмной кромкой.";
        marker.uncertainty = "Габариты видимой полосы и стенки оценены по фото и подогнаны к существующей дорожке. " +
            "Скрытые ступени и профиль спуска за парапетом не восстановлены.";
        marker.sourceUrls = new[] { "https://yandex.ru/maps/org/1334869298/", Photo,
            "https://avatars.mds.yandex.net/get-altay/15148849/2a0000019551f0e13c5150a00c7a3401f914/orig" };
        marker.studyBounds = new Bounds(new Vector3(x, .22f, zWall), new Vector3(width + 1, .7f, 1.1f));
        marker.geometryProvisional = true;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_WEST applied sections={Sections} largestPavingJoint={maxJoint:F4} routePoints={route.points.Length}");
    }

    private static Vector2 XZ(Vector3 p) => new Vector2(p.x, p.z);
    private static Vector3 Bottom(Vector3 p) => new Vector3(p.x, -.033f, p.z);

    private static bool Hit(MeshCollider collider, float x, float z, out RaycastHit hit) =>
        collider.Raycast(new Ray(new Vector3(x, .65f, z), Vector3.down), out hit, .85f) && hit.normal.y > .78f;

    private static float SurfaceLevel(Vector3 p)
    {
        if (!Physics.Raycast(new Vector3(p.x, .66f, p.z), Vector3.down, out var hit, .85f, ~0, QueryTriggerInteraction.Ignore) ||
            hit.normal.y < .78f)
            throw new InvalidOperationException("No walkable surface below the existing library approach.");
        return hit.point.y;
    }

    private static float RouteX(Vector3[] path, float z)
    {
        for (int i = 1; i < path.Length; i++)
            if (path[i - 1].z >= z && path[i].z <= z)
                return Mathf.Lerp(path[i - 1].x, path[i].x, Mathf.InverseLerp(path[i - 1].z, path[i].z, z));
        throw new InvalidOperationException("The library edge no longer follows the saved approach.");
    }

    private static void CheckClearance(Vector3[] path, float x0, float x1, float z0, float z1)
    {
        for (int i = 1; i < path.Length; i++)
            for (int t = 0; t <= 12; t++)
            {
                var point = Vector3.Lerp(path[i - 1], path[i], t / 12f);
                float dx = point.x - Mathf.Clamp(point.x, x0, x1);
                float dz = point.z - Mathf.Clamp(point.z, z0, z1);
                if (dx * dx + dz * dz < .52f * .52f)
                    throw new InvalidOperationException("The library parapet would obstruct the existing route.");
            }
    }

    private static float CheckApron(MeshCollider apron, MeshCollider red, MeshCollider asphalt, Vector3[] west, Vector3[] east)
    {
        float largest = 0;
        for (int i = 1; i < west.Length; i++)
        {
            Vector3 a = (west[i - 1] + west[i]) * .5f, b = (east[i - 1] + east[i]) * .5f;
            if (!Hit(apron, a.x + .02f, a.z, out var newEdge) || !Hit(red, a.x - .02f, a.z, out var oldEdge) ||
                !Hit(apron, (a.x + b.x) * .5f, a.z, out _))
                throw new InvalidOperationException("Missing physics coverage across the library apron or its joint.");
            if (Hit(asphalt, (a.x + b.x) * .5f, a.z, out _))
                throw new InvalidOperationException("Old asphalt remains stacked under the library apron.");
            largest = Mathf.Max(largest, Mathf.Abs(newEdge.point.y - oldEdge.point.y));
        }
        if (largest > .025f)
            throw new InvalidOperationException($"Library approach joint is too high: {largest:F3} m.");
        return largest;
    }

    private static Material Finish(string name, Color tint, float tile)
    {
        string path = $"{Root}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/PhotoReconstruction/Stage08Autumn/Materials/EntranceConcrete_11f4a63e99d58d742a27d41bee95e59e.mat");
            if (!source)
                throw new InvalidOperationException("The saved textured entrance concrete is missing.");
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_Color", tint);
        material.SetVector("_TileMeters", new Vector4(tile, tile, 0, 0));
        material.SetFloat("_Weathering", .38f);
        material.SetFloat("_RoughnessBias", .20f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static MeshCollider Create(EnvironmentMesh mesh, string asset, string label, Transform parent)
    {
        var part = mesh.Create(asset, label, Root, true);
        part.transform.SetParent(parent, false);
        GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        var collider = part.AddComponent<MeshCollider>();
        collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
        return collider;
    }
}
