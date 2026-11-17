using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal static class StreetAutumnRaisedLawns
{
    private const string RootName = "Трава · поднятые газоны двора №7";
    private const string SourceDirectory = "Assets/PhotoReconstruction/Stage05Civil/Meshes";
    private const string LawnMaterialGuid = "d0b09ee0afd727e44bba47649cd0971c";
    private const float SiteSpacing = .21f;
    private const float BoundaryClearance = .12f;
    private static readonly float[] LodThresholds = { .43f, .21f, .10f };

    internal static int Apply(Scene scene, Material material, string assetFolder)
    {
        if (!scene.IsValid() || !scene.isLoaded || !material || !AssetDatabase.IsValidFolder(assetFolder))
            throw new ArgumentException("The loaded Street scene, saved grass material and output folder are required.");

        var lawn = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(LawnMaterialGuid));
        if (!lawn)
            throw new InvalidOperationException("The saved painted lawn material was not found.");

        var sceneFilters = UnityEngine.Object.FindObjectsByType<MeshFilter>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(filter => filter.gameObject.scene == scene).ToArray();
        Physics.SyncTransforms();
        var physics = scene.GetPhysicsScene();
        var sites = new List<Vector3>[3];
        for (int island = 0; island < sites.Length; island++)
        {
            string path = $"{SourceDirectory}/IslandRelief05_{island}.asset";
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!source)
                throw new InvalidOperationException($"Saved raised lawn mesh missing at {path}.");

            var filter = sceneFilters.Single(candidate => candidate.sharedMesh == source);
            var collider = filter.GetComponent<MeshCollider>();
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!collider || !collider.enabled || collider.sharedMesh != source || !renderer || !renderer.enabled)
                throw new InvalidOperationException($"Saved raised lawn {filter.name} lacks its expected visible mesh collider.");

            int submesh = Array.IndexOf(renderer.sharedMaterials, lawn);
            if (submesh < 0 || submesh >= source.subMeshCount || source.GetIndexCount(submesh) < 3)
                throw new InvalidOperationException($"The painted lawn submesh is missing on {filter.name}.");

            sites[island] = SampleLawn(physics, collider, submesh, island);
            if (sites[island].Count < 100)
                throw new InvalidOperationException($"Only {sites[island].Count} valid planting sites on {filter.name}.");
        }

        var previous = scene.GetRootGameObjects().SingleOrDefault(candidate => candidate.name == RootName);
        if (previous)
            UnityEngine.Object.DestroyImmediate(previous);
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        for (int island = 0; island < sites.Length; island++)
            CreateIslandGrass(root.transform, material, assetFolder, island, sites[island]);

        return sites.Sum(list => list.Count);
    }

    private static List<Vector3> SampleLawn(PhysicsScene physics, MeshCollider collider, int submesh, int island)
    {
        var mesh = collider.sharedMesh;
        var vertices = mesh.vertices;
        var triangles = mesh.GetTriangles(submesh);
        var start = collider.transform.TransformPoint(vertices[triangles[0]]);
        var bounds = new Bounds(start, Vector3.zero);
        foreach (int vertex in triangles)
            bounds.Encapsulate(collider.transform.TransformPoint(vertices[vertex]));

        int firstGrassTriangle = 0;
        for (int slot = 0; slot < submesh; slot++)
            firstGrassTriangle += (int)mesh.GetIndexCount(slot) / 3;
        int endGrassTriangle = firstGrassTriangle + triangles.Length / 3;
        float rayStart = collider.bounds.max.y + 1f;
        float rayDistance = rayStart - bounds.min.y + .05f;

        bool OnLawn(float x, float z, out RaycastHit hit)
        {
            var origin = new Vector3(x, rayStart, z);
            return physics.Raycast(origin, Vector3.down, out hit, rayDistance, Physics.DefaultRaycastLayers,
                       QueryTriggerInteraction.Ignore)
                   && hit.collider == collider && hit.normal.y > .88f
                   && hit.triangleIndex >= firstGrassTriangle && hit.triangleIndex < endGrassTriangle;
        }

        // The original source forms each top from a convex hull. Requiring the eight points
        // of this circumscribed octagon to stay on that grass top leaves a 10 cm clear border.
        var clearance = new Vector2[8];
        float cornerRadius = (BoundaryClearance + .002f) / Mathf.Cos(Mathf.PI / 8f);
        for (int i = 0; i < clearance.Length; i++)
        {
            float angle = i * Mathf.PI / 4f;
            clearance[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cornerRadius;
        }

        var random = new System.Random(51791 + island * 263);
        var roots = new List<Vector3>();
        for (float z = bounds.min.z; z < bounds.max.z; z += SiteSpacing)
        for (float x = bounds.min.x; x < bounds.max.x; x += SiteSpacing)
        {
            float sampleX = x + SiteSpacing * Mathf.Lerp(.15f, .85f, (float)random.NextDouble());
            float sampleZ = z + SiteSpacing * Mathf.Lerp(.15f, .85f, (float)random.NextDouble());
            float patch = Mathf.PerlinNoise(sampleX * .41f + 37f, sampleZ * .41f + 51f);
            if (random.NextDouble() > Mathf.Lerp(.66f, .97f, patch)
                || !OnLawn(sampleX, sampleZ, out var hit))
                continue;

            bool safe = true;
            foreach (var offset in clearance)
            {
                if (OnLawn(sampleX + offset.x, sampleZ + offset.y, out _))
                    continue;
                safe = false;
                break;
            }
            if (safe)
                roots.Add(hit.point + Vector3.up * .002f);
        }
        return roots;
    }

    private static void CreateIslandGrass(Transform parent, Material material, string assetFolder, int island,
        IReadOnlyList<Vector3> worldRoots)
    {
        var bounds = new Bounds(worldRoots[0], Vector3.zero);
        foreach (var point in worldRoots)
            bounds.Encapsulate(point);

        var group = new GameObject($"Поднятый газон {island + 1}");
        group.transform.SetParent(parent, false);
        group.transform.position = bounds.center;

        var localRoots = worldRoots.Select(group.transform.InverseTransformPoint).ToArray();
        var levels = new LOD[LodThresholds.Length];
        var bladeCounts = new int[levels.Length];
        for (int level = 0; level < levels.Length; level++)
        {
            string name = $"RaisedLawn_{island + 1}_LOD{level}";
            int stride = level == 0 ? 1 : level == 1 ? 3 : 8;
            var chosen = localRoots.Where((_, index) => index % stride == 0).ToArray();
            var generated = StreetAutumnGrassBlades.BuildStandalone(name, chosen, group.transform, level, out bladeCounts[level]);
            var mesh = SaveMesh(generated, $"{assetFolder}/{name}.asset");

            var part = new GameObject($"LOD {level}");
            part.transform.SetParent(group.transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.rayTracingMode = RayTracingMode.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            levels[level] = new LOD(LodThresholds[level], new Renderer[] { renderer });
        }

        var lodGroup = group.AddComponent<LODGroup>();
        lodGroup.SetLODs(levels);
        lodGroup.RecalculateBounds();
        Debug.Log($"STREET_AUTUMN_RAISED_LAWN island={island + 1} sites={worldRoots.Count} blades={string.Join(",", bladeCounts)} clearance={BoundaryClearance:F2}");
    }

    private static Mesh SaveMesh(Mesh mesh, string assetPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (!existing)
        {
            AssetDatabase.CreateAsset(mesh, assetPath);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        UnityEngine.Object.DestroyImmediate(mesh);
        return existing;
    }
}
