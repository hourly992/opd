using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictLibraryRecess
{
    private const string Root = "Assets/PhotoReconstruction/Stage11West";
    private const string Group = "Библиотека №6 · пониженный вход";
    private const string DoorName = "Дверь, откосы и закрытый порог";
    private const string Photo = "https://avatars.mds.yandex.net/get-altay/986332/2a00000163b68406656e7a5f1e6fb3210d56/orig";
    private const float DoorX = -46.623085f;
    private const float WallZ = -45.897f;
    private const float StartZ = -47.65f;
    private const float EndSlopeZ = -46.24f;
    private const float LowerY = -.18f;
    private const float RampLeft = DoorX - .93f;
    private const float RampRight = DoorX + .93f;
    private const float RecessRight = -42.98f;
    private const float ParapetRearZ = -48.99f;
    private const float DoorHalfWidth = .675f;
    private const float DoorTop = 1.67f;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene.");
        var house = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b => b.address == "6");
        var lodGroup = house.GetComponent<LODGroup>();
        var lods = lodGroup.GetLODs();
        var facades = lods.Select(lod => lod.renderers.Single(renderer =>
        {
            var mesh = renderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
            return mesh && (mesh.name.StartsWith("Library6_Facade_LOD", StringComparison.Ordinal) ||
                mesh.name.StartsWith("Library6RecessFacadeLOD", StringComparison.Ordinal));
        }))
            .Select(renderer => renderer.GetComponent<MeshFilter>()).ToArray();
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var route = access.routes.Single(r => r.address.StartsWith("6 ", StringComparison.Ordinal) &&
            r.address.EndsWith("Access · торцевой вход 4", StringComparison.Ordinal));
        if (Mathf.Abs(route.points[0].x - DoorX) > .01f || Mathf.Abs(route.points[0].z + 47.697f) > .01f)
            throw new InvalidOperationException("The library route no longer matches the photographed entrance.");
        if (!GameObject.Find("Запад · подход к библиотеке №6"))
            throw new InvalidOperationException("The saved library apron and parapet are required.");

        Physics.SyncTransforms();
        float startY = Floor(DoorX, StartZ - .012f).point.y;
        if (startY < -.02f || startY > .10f)
            throw new InvalidOperationException("Unexpected elevation at the top of the library approach.");
        float slope = (startY - LowerY) / (EndSlopeZ - StartZ);
        if (slope <= 0 || slope > .20f)
            throw new InvalidOperationException("The modelled library descent would be too steep.");

        var cuts = new[] { Rect.MinMaxRect(RampLeft, StartZ, RampRight, WallZ),
            Rect.MinMaxRect(RampRight, ParapetRearZ, RecessRight, WallZ) };
        var existing = GameObject.Find(Group);
        if (existing)
        {
            var currentFloor = existing.GetComponentsInChildren<MeshCollider>().Single(collider =>
                collider.sharedMesh && collider.sharedMesh.name == "Library6RecessPaving");
            if (!existing.transform.Find(DoorName))
                BuildDoor(existing.transform, lodGroup, facades);
            var refreshed = RefreshChecks(access, cuts, startY, currentFloor);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"LIBRARY_RECESS existing paving checked; refreshedChecks={refreshed.route}/{refreshed.entrance}");
            return;
        }

        var oldLevels = route.points.Select(p => Floor(p.x, p.z).point.y).ToArray();
        bool ExtraFloor(MeshFilter filter) => filter.name == "Grass" ||
            filter.sharedMesh.name == "Library6ConcreteApron";
        var sources = UnityEngine.Object.FindObjectsByType<MeshCollider>()
            .Where(collider => collider.enabled && collider.sharedMesh && !collider.isTrigger)
            .Where(collider => cuts.Any(cut => Overlaps(collider.bounds, cut)))
            .Select(collider => collider.GetComponent<MeshFilter>())
            .Where(filter => filter && filter.GetComponent<MeshRenderer>() &&
                filter.sharedMesh == filter.GetComponent<MeshCollider>().sharedMesh)
            .Where(filter => ExtraFloor(filter) || filter.GetComponent<MeshRenderer>().sharedMaterials
                .Any(material => material && (material.name.Contains("Asphalt") ||
                    material.HasProperty("_SurfaceClass") && material.GetFloat("_SurfaceClass") > 5.5f)))
            .ToArray();
        if (!sources.Any(filter => filter.name == "Grass") || sources.Length < 2)
            throw new InvalidOperationException("Could not identify the saved library ground and paving.");

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var concrete = AssetDatabase.LoadAssetAtPath<Material>("Assets/PhotoReconstruction/Stage10West/LibraryApronConcrete.mat");
        if (!concrete)
            throw new InvalidOperationException("Missing the saved library concrete materials.");
        var group = new GameObject(Group);
        var ground = new EnvironmentMesh();
        Surface(ground, concrete, RampLeft, RampRight, StartZ, EndSlopeZ, startY, LowerY);
        Surface(ground, concrete, RampLeft, RampRight, EndSlopeZ, WallZ, LowerY, LowerY);
        Surface(ground, concrete, RampRight, RecessRight, ParapetRearZ, WallZ, LowerY, LowerY);
        Side(ground, concrete, new Vector3(RampLeft, startY, StartZ), new Vector3(RampLeft, LowerY, EndSlopeZ), Vector3.right);
        Side(ground, concrete, new Vector3(RampLeft, LowerY, EndSlopeZ), new Vector3(RampLeft, LowerY, WallZ), Vector3.right);
        Side(ground, concrete, new Vector3(RecessRight, LowerY, WallZ), new Vector3(RecessRight, LowerY, ParapetRearZ), Vector3.left);
        ground.Face(concrete, Vector3.left, new Vector3(RampRight, LowerY, ParapetRearZ),
            new Vector3(RampRight, LowerY, StartZ), new Vector3(RampRight, startY, StartZ), new Vector3(RampRight, startY, ParapetRearZ));
        ground.Face(concrete, Vector3.left, new Vector3(RampRight, LowerY, StartZ),
            new Vector3(RampRight, LowerY, EndSlopeZ), new Vector3(RampRight, startY, StartZ));
        ground.Face(concrete, Vector3.forward, new Vector3(RampRight, LowerY, ParapetRearZ),
            new Vector3(RecessRight, LowerY, ParapetRearZ), new Vector3(RecessRight, .02f, ParapetRearZ),
            new Vector3(RampRight, .02f, ParapetRearZ));

        var polygons = cuts.Select(cut => new List<Vector2> { new(cut.xMin, cut.yMin), new(cut.xMax, cut.yMin),
            new(cut.xMax, cut.yMax), new(cut.xMin, cut.yMax) }).ToList();
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(sources, polygons, Root, ExtraFloor);
        var landing = Create(ground, "Library6RecessPaving", "Спуск и бетонная площадка", group.transform);
        var floor = landing.AddComponent<MeshCollider>();
        floor.sharedMesh = landing.GetComponent<MeshFilter>().sharedMesh;
        access.surfaces = access.surfaces.Append(floor).ToArray();
        EditorUtility.SetDirty(access);

        BuildDoor(group.transform, lodGroup, facades);

        Physics.SyncTransforms();
        int checkedPoints = 0;
        for (int row = 1; row <= 8; row++)
        {
            float z = Mathf.Lerp(StartZ, WallZ, row / 9f);
            foreach (float x in new[] { RampLeft + .12f, DoorX, RampRight - .12f, RecessRight - .25f })
            {
                var hit = Floor(x, z);
                float y = x < RampRight ? Grade(z, startY) : LowerY;
                if (hit.collider != floor || Mathf.Abs(hit.point.y - y) > .008f || hit.normal.y < .97f)
                    throw new InvalidOperationException($"The library descent is blocked or has a gap at {hit.point:F3}.");
                checkedPoints++;
            }
        }
        for (int i = 0; i < route.points.Length; i++)
            if (Mathf.Abs(Floor(route.points[i].x, route.points[i].z).point.y - oldLevels[i]) > .025f)
                throw new InvalidOperationException("The original library route height changed.");

        var refreshedChecks = RefreshChecks(access, cuts, startY, floor);

        var evidence = new GameObject("Фотоориентир · понижение у библиотеки №6") { tag = "EditorOnly" };
        evidence.transform.SetParent(group.transform, false);
        var marker = evidence.AddComponent<PhotoSurveyMarker>();
        marker.category = "Библиотека №6 · заглублённый порог";
        marker.observation = "Летний и зимний кадры показывают тёмную дверь под навесом за парапетом и проход с его левой стороны.";
        marker.uncertainty = "Понижение 18 см относительно нуля модели и непрерывный уклон оценочные. Ступени и глубина за парапетом закрыты.";
        marker.geometryProvisional = true;
        marker.sourceUrls = new[] { Photo, "https://cbs-hmrn.ru/o-biblioteke/svedeniya/svedeniya.php" };
        marker.studyBounds = new Bounds(new Vector3(-45.2f, .65f, -47.46f), new Vector3(4.9f, 2.1f, 3.2f));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"LIBRARY_RECESS floor={LowerY:F3} start={startY:F3} slope={slope:P1} support={checkedPoints} " +
            $"retainedRoute={route.points.Length} refreshedChecks={refreshedChecks.route}/{refreshedChecks.entrance}");
    }

    private static (int route, int entrance) RefreshChecks(ResidentialAccess access, Rect[] cuts, float startY, MeshCollider floor)
    {
        int Refresh(Vector3[] checks)
        {
            int updated = 0;
            for (int i = 0; i < checks.Length; i++)
            {
                var point = checks[i];
                if (!cuts.Any(cut => cut.Contains(new Vector2(point.x, point.z))))
                    continue;
                float expected = point.x < RampRight ? Grade(point.z, startY) : LowerY;
                var hit = Floor(point.x, point.z);
                if (hit.collider != floor || Mathf.Abs(hit.point.y - expected) > .008f)
                    throw new InvalidOperationException($"The modelled library profile does not support the previous inspection point {point:F3}.");
                checks[i] = new Vector3(point.x, expected, point.z);
                updated++;
            }
            return updated;
        }
        var result = (Refresh(access.surfaceChecks), Refresh(access.entranceLandingChecks));
        EditorUtility.SetDirty(access);
        return result;
    }

    private static void BuildDoor(Transform parent, LODGroup houseLod, MeshFilter[] facades)
    {
        var former = parent.Find("Дверь, откосы и цоколь под навесом");
        if (former)
        {
            var renderer = former.GetComponent<Renderer>();
            var levels = houseLod.GetLODs();
            for (int i = 0; i < levels.Length; i++)
                levels[i].renderers = levels[i].renderers.Where(r => r != renderer).ToArray();
            houseLod.SetLODs(levels);
            PrefabUtility.RecordPrefabInstancePropertyModifications(houseLod);
            UnityEngine.Object.DestroyImmediate(former.gameObject);
        }
        var architecture = new EnvironmentMesh();
        var reference = facades[0].GetComponent<Renderer>().sharedMaterials;
        var plinth = reference.First(material => material && material.name == "TurquoisePlinth");
        var brick = reference.First(material => material && material.name == "RedBrick");
        AddDoor(architecture, brick, plinth, DoorSteel());
        var door = Create(architecture, "Library6RecessDoor", DoorName, parent);
        var doorLod = door.AddComponent<LODGroup>();
        doorLod.SetLODs(new[] { new LOD(.012f, new[] { door.GetComponent<Renderer>() }) });
        doorLod.RecalculateBounds();
        for (int lod = 0; lod < facades.Length; lod++)
            CutSavedDoor(facades[lod], lod);
        houseLod.RecalculateBounds();
    }

    private static float Grade(float z, float startY) =>
        Mathf.Lerp(startY, LowerY, Mathf.InverseLerp(StartZ, EndSlopeZ, z));

    private static bool Overlaps(Bounds bounds, Rect cut) =>
        bounds.min.x < cut.xMax && bounds.max.x > cut.xMin && bounds.min.z < cut.yMax && bounds.max.z > cut.yMin;

    private static RaycastHit Floor(float x, float z)
    {
        foreach (var hit in Physics.RaycastAll(new Ray(new Vector3(x, .53f, z), Vector3.down), .90f)
                     .OrderByDescending(hit => hit.point.y))
            if (hit.normal.y > .78f && !hit.collider.GetComponentInParent<StreetVehicle>())
                return hit;
        throw new InvalidOperationException($"No floor at ({x:F3}, {z:F3}).");
    }

    private static void Surface(EnvironmentMesh mesh, Material material, float left, float right,
        float start, float end, float startY, float endY)
    {
        var normal = new Vector3(0, end - start, startY - endY).normalized;
        mesh.Face(material, normal, new Vector3(left, startY, start), new Vector3(right, startY, start),
            new Vector3(right, endY, end), new Vector3(left, endY, end));
    }

    private static void Side(EnvironmentMesh mesh, Material material, Vector3 bottomA, Vector3 bottomB, Vector3 normal)
    {
        Vector3 a = new Vector3(bottomA.x, Mathf.Max(.02f, bottomA.y), bottomA.z);
        Vector3 b = new Vector3(bottomB.x, Mathf.Max(.02f, bottomB.y), bottomB.z);
        mesh.Face(material, normal, bottomA, bottomB, b, a);
    }

    private static void AddDoor(EnvironmentMesh mesh, Material brick, Material plinth, Material dark)
    {
        void Panel(Material material, float left, float right, float lower, float upper, float z)
        {
            mesh.Face(material, Vector3.back, new Vector3(left, lower, z), new Vector3(right, lower, z),
                new Vector3(right, upper, z), new Vector3(left, upper, z));
        }

        float left = DoorX - DoorHalfWidth, right = DoorX + DoorHalfWidth;
        Panel(brick, left, right, DoorTop, 1.99f, WallZ);
        Panel(plinth, RampLeft, left, LowerY, 0, WallZ);
        Panel(plinth, right, RecessRight, LowerY, 0, WallZ);
        const float reveal = .09f, frame = .055f;
        float height = DoorTop - LowerY;
        mesh.Face(plinth, Vector3.right, new Vector3(left, LowerY, WallZ), new Vector3(left, LowerY, WallZ + reveal),
            new Vector3(left, DoorTop, WallZ + reveal), new Vector3(left, DoorTop, WallZ));
        mesh.Face(plinth, Vector3.left, new Vector3(right, LowerY, WallZ), new Vector3(right, LowerY, WallZ + reveal),
            new Vector3(right, DoorTop, WallZ + reveal), new Vector3(right, DoorTop, WallZ));
        mesh.Face(plinth, Vector3.down, new Vector3(left, DoorTop, WallZ), new Vector3(left, DoorTop, WallZ + reveal),
            new Vector3(right, DoorTop, WallZ + reveal), new Vector3(right, DoorTop, WallZ));
        mesh.Box(dark, new Vector3(DoorX, LowerY + height * .5f, WallZ + reveal),
            new Vector3(DoorHalfWidth * 2 - frame, height - .045f, .055f));
        foreach (float x in new[] { left + frame * .5f, right - frame * .5f })
            mesh.Box(dark, new Vector3(x, LowerY + height * .5f, WallZ + reveal * .56f),
                new Vector3(frame, height, .052f));
        foreach (float y in new[] { LowerY + .025f, DoorTop - .025f })
            mesh.Box(dark, new Vector3(DoorX, y, WallZ + reveal * .56f),
                new Vector3(DoorHalfWidth * 2, .05f, .052f));
        // Continue the landing into the reveal and close the lower edge behind the sill.
        mesh.Box(dark, new Vector3(DoorX, LowerY - .014f, WallZ + .059f),
            new Vector3(DoorHalfWidth * 2 + .008f, .032f, .14f));
        mesh.Box(dark, new Vector3(DoorX - .43f, LowerY + .98f, WallZ + .015f), new Vector3(.033f, .18f, .04f));
    }

    private static Material DoorSteel()
    {
        const string path = Root + "/LibraryEntranceDarkSteel.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/PhotoReconstruction/Stage08Autumn/Materials/EntranceSteel_420e95d293b32434dac116d873090ab4.mat");
            if (!source)
                throw new InvalidOperationException("Missing the saved steel door material.");
            material = new Material(source) { name = "LibraryEntranceDarkSteel" };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_Color", new Color(.17f, .175f, .17f));
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void CutSavedDoor(MeshFilter filter, int lod)
    {
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(
            $"Assets/PhotoReconstruction/Stage07Library/Meshes/Library6_Facade_LOD{lod}.asset");
        if (!source)
            throw new InvalidOperationException("Missing the original library facade.");
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = $"Library6RecessFacadeLOD{lod}";
        var points = source.vertices;
        bool InDoor(int index)
        {
            var p = filter.transform.TransformPoint(points[index]);
            return Mathf.Abs(p.x - DoorX) <= .725f && p.y >= -.00005f && p.y <= 1.9905f &&
                   p.z >= WallZ - .14f && p.z <= WallZ + .185f;
        }

        int removed = 0;
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            int[] indices = source.GetTriangles(sub);
            var retained = new List<int>(indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (InDoor(indices[i]) && InDoor(indices[i + 1]) && InDoor(indices[i + 2]))
                    removed++;
                else
                {
                    retained.Add(indices[i]);
                    retained.Add(indices[i + 1]);
                    retained.Add(indices[i + 2]);
                }
            }
            mesh.SetTriangles(retained, sub, false);
        }
        if (removed < 25 || removed > 150)
            throw new InvalidOperationException($"Unexpected door geometry at library LOD {lod}: {removed} triangles.");
        mesh.RecalculateBounds();
        string path = $"{Root}/Meshes/Library6RecessFacadeLOD{lod}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(mesh, saved);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(saved);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, path);
            saved = mesh;
        }
        filter.sharedMesh = saved;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
    }

    private static GameObject Create(EnvironmentMesh mesh, string name, string label, Transform parent)
    {
        var part = mesh.Create(name, label, Root, true);
        part.transform.SetParent(parent, false);
        GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        return part;
    }
}
