using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetLibraryPhotoFinish
{
    private const string AssetRoot = "Assets/PhotoReconstruction/Stage07Library";
    private const string DetailName = "Дом 6 · библиотечный вход по фото";
    private const string Photo = "https://avatars.mds.yandex.net/get-altay/986332/2a00000163b68406656e7a5f1e6fb3210d56/orig";
    private const string Address = "https://yandex.ru/maps/org/1334869298/";
    private static readonly Vector3 ExpectedEntrance = new Vector3(-46.623085f, 0, -47.697f);

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before applying the library detail.");

        var building = UnityEngine.Object.FindObjectsByType<StreetBuilding>(FindObjectsSortMode.None)
            .Single(b => b.address == "6" && b.osmId == 114788195);
        var existingDetail = building.transform.Find(DetailName);

        var entrance = building.GetComponentsInChildren<Transform>(true)
            .Single(t => t.name == "Access · торцевой вход 4");
        if (Vector3.Distance(entrance.position, ExpectedEntrance) > .03f ||
            Vector3.Dot(entrance.forward, Vector3.back) < .999f)
            throw new InvalidOperationException("The south-facing library entrance no longer matches the photo alignment.");

        // The saved end-entrance marker is 1.80 m in front of the facade plane.
        Vector3 wall = entrance.position - entrance.forward * 1.80f;
        Vector3 Point(float across, float height, float outward) =>
            wall + entrance.right * across + Vector3.up * height + entrance.forward * outward;

        var group = building.GetComponent<LODGroup>();
        var lods = group.GetLODs();
        var wallRenderers = lods.Select(lod => lod.renderers.Single(renderer =>
            !existingDetail || !renderer.transform.IsChildOf(existingDetail))).ToArray();
        var cuts = wallRenderers.Select(renderer => InspectCanopy(renderer, entrance, wall)).ToArray();
        if (existingDetail)
        {
            // An earlier pass used a narrower trim bound. Only finish the saved
            // canopy cut, and leave the new roof and pipe in their current LODs.
            if (cuts.Any(cut => cut.roofTriangles != 0))
                return;
            if (cuts.Any(cut => cut.trimTriangles > 10))
                throw new InvalidOperationException("Unexpected residual roof trim at the library entrance.");
            for (int i = 0; i < lods.Length; i++)
                if (cuts[i].trimTriangles > 0)
                    SaveFacadeCut(wallRenderers[i].GetComponent<MeshFilter>(), cuts[i].retained, i);
            Debug.Log($"STREET_LIBRARY_TRIM residualTriangles={string.Join(",", cuts.Select(cut => cut.trimTriangles))}");
            return;
        }
        bool replaceCanopy = cuts.All(cut => cut.roofTriangles == 6 && cut.trimTriangles >= 12 && cut.trimTriangles < 240);

        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        var paint = PaintedPipe();
        var root = new GameObject(DetailName);
        root.transform.SetParent(building.transform, false);
        for (int i = 0; i < lods.Length; i++)
        {
            var renderer = wallRenderers[i];
            var mesh = new EnvironmentMesh();
            const float left = -6.4f, right = -1.68f, height = 1.66f, projection = .13f;
            int sides = i == 0 ? 12 : i == 1 ? 8 : 6;
            mesh.Cylinder(paint, Point(left, height, projection), Point(right, height, projection), .027f, .027f, sides);
            mesh.Cylinder(paint, Point(right, height, projection), Point(right, height + .17f, projection), .027f, .027f, sides);
            mesh.Cylinder(paint, Point(right, height + .17f, projection), Point(right, height + .17f, .014f), .027f, .027f, sides);
            if (i < 2)
                for (float x = left + .28f; x < right - .1f; x += 1.15f)
                {
                    mesh.Box(paint, Point(x, height - .065f, .012f), new Vector3(.06f, .11f, .024f), entrance.rotation);
                    mesh.Cylinder(paint, Point(x, height - .085f, .025f), Point(x, height - .025f, projection), .013f, .013f, 6);
                }

            if (replaceCanopy)
            {
                SaveFacadeCut(renderer.GetComponent<MeshFilter>(), cuts[i].retained, i);
                var materials = renderer.sharedMaterials;
                var green = materials.Single(m => m && m.name == "GreenStandingSeam");
                var edge = materials.Single(m => m && m.name == "RoofEdge");
                AddShallowHood(mesh, green, edge, Point, entrance.forward, i == 0);
            }

            var part = mesh.Create("Library6_Detail_LOD" + i, "Библиотека · LOD " + i, AssetRoot, true);
            part.transform.SetParent(root.transform, true);
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic);
            lods[i].renderers = lods[i].renderers.Concat(new[] { part.GetComponent<Renderer>() }).ToArray();
        }

        group.SetLODs(lods);
        group.RecalculateBounds();
        PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        EditorUtility.SetDirty(group);

        var source = new GameObject("Привязка библиотечного входа · наблюдение");
        source.tag = "EditorOnly";
        source.transform.SetParent(root.transform, false);
        var survey = source.AddComponent<PhotoSurveyMarker>();
        survey.category = "Южный торец дома №6 · библиотека";
        survey.observation = "Летний кадр показывает жёлтую фасадную трубу и пологий зелёный навес. " +
            "Точка входа Яндекс 69.06164724, 60.99928883, азимут 180.957°. " +
            "Навес в сцене заменён: " + (replaceCanopy ? "да" : "нет, сохранилась исходная геометрия");
        survey.uncertainty = "Длины, высоты и узлы трубы оценочные. Заглубление за парапетом видно, точный профиль спуска закрыт. " +
            "Привязка детали сделана к существующему южному торцу и маршруту.";
        survey.sourceUrls = new[] { Address, Photo };
        survey.studyBounds = new Bounds(Point(-2.2f, 1.25f, .72f), new Vector3(8.2f, 2.6f, 1.9f));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STREET_LIBRARY_PHOTO house=6 canopyReplaced={replaceCanopy} " +
            $"roofTriangles={string.Join(",", cuts.Select(cut => cut.roofTriangles))} " +
            $"trimTriangles={string.Join(",", cuts.Select(cut => cut.trimTriangles))} facade={wall}");
    }

    private static (int[][] retained, int roofTriangles, int trimTriangles) InspectCanopy(
        Renderer renderer, Transform entrance, Vector3 wall)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        var mesh = filter.sharedMesh;
        var materials = renderer.sharedMaterials;
        var vertices = mesh.vertices.Select(filter.transform.TransformPoint).ToArray();
        var retained = new int[mesh.subMeshCount][];
        int roof = 0, trim = 0;
        bool InCanopy(Vector3 point)
        {
            Vector3 relative = point - wall;
            float outward = Vector3.Dot(relative, entrance.forward);
            return Mathf.Abs(Vector3.Dot(relative, entrance.right)) <= 1.49f &&
                relative.y >= 2.05f && relative.y <= 2.65f && outward >= -.02f && outward <= 1.76f;
        }

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            var indices = mesh.GetTriangles(submesh);
            var material = materials[submesh];
            bool isRoof = material && material.name == "GreenStandingSeam";
            bool isTrim = material && material.name == "RoofEdge";
            if (!isRoof && !isTrim)
            {
                retained[submesh] = indices;
                continue;
            }
            var kept = new System.Collections.Generic.List<int>(indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
                float upper = Mathf.Max(a.y, Mathf.Max(b.y, c.y)) - wall.y;
                // The horizontal support bars end at y=2.0675 m. Keep them under the new cover.
                if (InCanopy(a) && InCanopy(b) && InCanopy(c) && upper > 2.07f)
                {
                    if (isRoof) roof++;
                    else trim++;
                    continue;
                }
                kept.Add(indices[i]);
                kept.Add(indices[i + 1]);
                kept.Add(indices[i + 2]);
            }
            retained[submesh] = kept.ToArray();
        }
        return (retained, roof, trim);
    }

    private static void SaveFacadeCut(MeshFilter filter, int[][] retained, int lod)
    {
        var mesh = UnityEngine.Object.Instantiate(filter.sharedMesh);
        mesh.name = "Library6_Facade_LOD" + lod;
        for (int i = 0; i < retained.Length; i++)
            mesh.SetTriangles(retained[i], i, false);
        mesh.RecalculateBounds();
        string path = AssetRoot + "/Meshes/" + mesh.name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(mesh, saved);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, path);
            saved = mesh;
        }
        filter.sharedMesh = saved;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        EditorUtility.SetDirty(filter);
    }

    private static Material PaintedPipe()
    {
        const string path = AssetRoot + "/LibraryGasPipe.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material)
            return material;
        material = new Material(Shader.Find("Street/TexturedSurface")) { name = "LibraryGasPipe" };
        StreetSurfaceLibrary.Apply(material, "green_metal_rust", 1.2f, new Color(.71f, .49f, .065f), .18f);
        material.SetFloat("_Colorize", 1);
        material.SetFloat("_RoughnessBias", .08f);
        material.SetFloat("_Metallic", .12f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void AddShallowHood(EnvironmentMesh mesh, Material green, Material edge,
        Func<float, float, float, Vector3> point, Vector3 outward, bool near)
    {
        const float half = 1.45f, back = .02f, front = 1.70f, rearHeight = 2.22f, frontHeight = 2.11f;
        const float thickness = .042f;
        Vector3[] upper = { point(-half, rearHeight, back), point(-half, frontHeight, front),
            point(half, frontHeight, front), point(half, rearHeight, back) };
        Vector3[] lower = upper.Select(p => p - Vector3.up * thickness).ToArray();
        Vector3 normal = (Vector3.up + outward * ((rearHeight - frontHeight) / (front - back))).normalized;
        mesh.Face(green, normal, upper);
        mesh.Face(edge, -normal, lower);
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Vector3 side = Vector3.Cross(Vector3.down, upper[next] - upper[i]).normalized;
            mesh.Face(edge, side, upper[i], upper[next], lower[next], lower[i]);
        }
        if (near)
            for (float x = -half + .16f; x < half; x += .20f)
                mesh.Cylinder(edge, point(x, rearHeight + .006f, back), point(x, frontHeight + .006f, front), .008f, .008f, 5);
    }
}
