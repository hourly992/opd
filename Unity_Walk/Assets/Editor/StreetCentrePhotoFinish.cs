using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetCentrePhotoFinish
{
    private const string AssetRoot = "Assets/PhotoReconstruction/Stage07Centre";
    private const string GroupName = "Двор №7 · белые скамьи по фотографии";
    private const float HalfWidth = .88f;
    private const float HalfDepth = .30f;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before adding courtyard benches.");
        if (GameObject.Find(GroupName)) return;

        var paving = GameObject.Find("Серое прямоугольное мощение · внутренний двор")?.GetComponent<MeshCollider>();
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if (!paving || !paving.enabled || !access || access.routes == null)
            throw new InvalidOperationException("The saved No.7 paving and pedestrian routes are required.");

        Physics.SyncTransforms();
        // Both benches face the main grey passage from the long side of the front island.
        // X/Z are visual estimates. The pavement collider supplies each level.
        var first = Locate(new Vector2(30.25f, 35.98f), paving, access);
        var second = Locate(new Vector2(34.20f, 35.98f), paving, access);

        Directory.CreateDirectory(AssetRoot + "/Materials");
        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        var support = Finish("No7BenchWhiteConcrete", "concrete_wall_006", new Color(.79f, .77f, .72f), .6f);
        var painted = Finish("No7BenchFadedBlue", "fine_grained_wood", new Color(.60f, .73f, .78f), .74f);
        var pale = Finish("No7BenchPaleSlat", "fine_grained_wood", new Color(.74f, .81f, .81f), .8f);

        var mesh = new EnvironmentMesh();
        foreach (float x in new[] { -.70f, .70f })
        {
            mesh.RoundedBox(support, new Vector3(x, .051f, 0), new Vector3(.22f, .102f, .55f), .021f);
            mesh.RoundedBox(support, new Vector3(x, .263f, 0), new Vector3(.18f, .424f, .49f), .017f);
            mesh.RoundedBox(support, new Vector3(x, .61f, -.204f), new Vector3(.16f, .30f, .12f), .014f);
        }
        for (int i = 0; i < 4; i++)
        {
            mesh.RoundedBox(i == 1 ? painted : pale, new Vector3(0, .486f, -.18f + i * .117f),
                new Vector3(1.76f, .052f, .108f), .014f);
        }
        var rake = Quaternion.Euler(-9f, 0, 0);
        mesh.Box(painted, new Vector3(0, .672f, -.203f), new Vector3(1.76f, .090f, .04f), rake);
        mesh.Box(pale, new Vector3(0, .791f, -.223f), new Vector3(1.76f, .117f, .046f), rake);

        var group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "Add photographed No.7 courtyard benches");
        group.transform.SetParent(paving.transform.parent, false);
        var bench = mesh.Create("No7WhiteSlattedBench", "Белая реечная скамья 1", AssetRoot, true);
        bench.transform.SetParent(group.transform, false);
        bench.transform.SetPositionAndRotation(first, Quaternion.identity);
        AddBox(bench, new Vector3(0, .256f, 0), new Vector3(1.76f, .512f, .55f));
        AddBox(bench, new Vector3(0, .683f, -.218f), new Vector3(1.76f, .344f, .13f));
        GameObjectUtility.SetStaticEditorFlags(bench, StaticEditorFlags.BatchingStatic);
        var other = UnityEngine.Object.Instantiate(bench, group.transform);
        other.name = "Белая реечная скамья 2";
        other.transform.SetPositionAndRotation(second, Quaternion.identity);

        var evidence = new GameObject("Фотоориентир · размещение оценочное");
        evidence.tag = "EditorOnly";
        evidence.transform.SetParent(group.transform, false);
        var marker = evidence.AddComponent<PhotoSurveyMarker>();
        marker.category = "Скамьи двора №7";
        marker.observation = "Две светлые реечные скамьи на бетонных опорах стоят рядом с бортиком поднятого газона на уровне серого мощения.";
        marker.uncertainty = "Тип и число скамей видны на фотографии. Размеры и координаты оценочные, положение подогнано к существующему острову и свободному проходу.";
        marker.sourceUrls = new[] {
            "https://realty.yandex.ru/offer/4006141392816993274/",
            "https://avatars.mds.yandex.net/get-realty-offers/15038303/2d86a840-819c-4ef2-a67e-6098b9d2a49e/app_large"
        };
        marker.studyBounds = new Bounds((first + second) * .5f + Vector3.up * .43f, new Vector3(5.72f, .86f, .60f));
        marker.geometryProvisional = true;

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STREET_CENTRE_PHOTO two benches at {first:F3} and {second:F3}, supported by courtyard paving with clear saved routes.");
    }

    private static Vector3 Locate(Vector2 anchor, MeshCollider paving, ResidentialAccess access)
    {
        float low = float.PositiveInfinity, high = float.NegativeInfinity;
        foreach (float dx in new[] { -HalfWidth, 0, HalfWidth })
        foreach (float dz in new[] { -HalfDepth, 0, HalfDepth })
        {
            var ray = new Ray(new Vector3(anchor.x + dx, 1.6f, anchor.y + dz), Vector3.down);
            if (!paving.Raycast(ray, out var hit, 2.2f) || hit.normal.y < .95f)
                throw new InvalidOperationException($"No level grey paving under the No.7 bench at {anchor:F3}.");
            low = Mathf.Min(low, hit.point.y);
            high = Mathf.Max(high, hit.point.y);
        }
        if (high - low > .02f)
            throw new InvalidOperationException($"The paving under the No.7 bench at {anchor:F3} is uneven.");

        const float routeReserve = 1.60f;
        float clearance = routeReserve + new Vector2(HalfWidth, HalfDepth).magnitude;
        foreach (var route in access.routes)
        {
            if (route?.points == null) continue;
            for (int i = 1; i < route.points.Length; i++)
            {
                Vector3 a = route.points[i - 1], b = route.points[i];
                var start = new Vector2(a.x, a.z);
                var direction = new Vector2(b.x, b.z) - start;
                float length = direction.sqrMagnitude;
                float t = length > .00001f ? Mathf.Clamp01(Vector2.Dot(anchor - start, direction) / length) : 0;
                if ((anchor - start - direction * t).sqrMagnitude < clearance * clearance)
                    throw new InvalidOperationException($"The No.7 bench at {anchor:F3} would narrow the route {route.address}.");
            }
        }

        var position = new Vector3(anchor.x, low, anchor.y);
        var center = position + Vector3.up * .46f;
        var extent = new Vector3(HalfWidth, .43f, HalfDepth);
        foreach (var collider in Physics.OverlapBox(center, extent, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
            if (collider != paving)
                throw new InvalidOperationException($"The No.7 bench at {anchor:F3} overlaps {collider.name}.");
        return position;
    }

    private static Material Finish(string name, string texture, Color tint, float recolour)
    {
        string path = $"{AssetRoot}/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Street/TexturedSurface")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        StreetSurfaceLibrary.Apply(material, texture, .75f, tint, .24f);
        material.SetFloat("_Colorize", recolour);
        material.SetFloat("_RoughnessBias", .12f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void AddBox(GameObject bench, Vector3 center, Vector3 size)
    {
        var collider = bench.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
    }
}
