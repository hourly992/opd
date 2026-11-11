using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class DistrictWalkthroughPlaygrounds
{
    private const string Root = "Assets/PhotoReconstruction/Stage13Playgrounds";
    private const string MaterialPath = Root + "/PlaygroundCompactedSoil.mat";
    private const string OriginalMeshRoot = "Assets/StreetGenerated/Pilot/Meshes";
    private const string SoilMeshVersion = "PlaygroundIntegratedSoilPreserveGround_";
    private const float EdgeLift = .004f;
    private const float EdgeDepth = .90f;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (Application.isPlaying || scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before updating playground surfaces.");

        var sites = UnityEngine.Object.FindObjectsByType<PlaygroundSite>()
            .OrderBy(site => site.osmId).ToArray();
        var terrain = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(collider =>
            collider.enabled && collider.sharedMesh && collider.gameObject.name == "Grass" && collider.bounds.size.x > 1000);
        if (sites.Length != 5 || sites.Any(site => !site.ground || !site.ground.sharedMesh || site.footprint == null ||
                site.footprint.Length < 3 || site.equipment == null || site.equipment.Length == 0))
            throw new InvalidOperationException("The five saved playgrounds and their equipment are required.");

        Physics.SyncTransforms();
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var material = GetSoilMaterial(sites);
        foreach (var site in sites)
        {
            var filter = site.GetComponent<MeshFilter>();
            var renderer = site.GetComponent<MeshRenderer>();
            if (!filter || !renderer || filter.sharedMesh != site.ground.sharedMesh)
                throw new InvalidOperationException($"Playground {site.osmId} has mismatched source collision.");
            bool alreadyIntegrated = AssetDatabase.GetAssetPath(filter.sharedMesh).StartsWith(Root + "/", StringComparison.Ordinal);
            if (alreadyIntegrated && filter.sharedMesh.name == SoilMeshVersion + site.osmId)
                continue;

            var source = filter.sharedMesh;
            int soil = Array.FindIndex(renderer.sharedMaterials, m => m && m.name == "PlaygroundSand");
            if (alreadyIntegrated)
            {
                // Rerun from the original footprint triangles. Their elevation retains the
                // distinction between the sand which was visible and the ground above it.
                source = AssetDatabase.LoadAssetAtPath<Mesh>($"{OriginalMeshRoot}/MappedPlayground_{site.osmId}.asset");
                soil = 0;
            }
            if (!source || soil < 0 || soil >= source.subMeshCount ||
                source.GetIndexCount(soil) != (site.footprint.Length - 2) * 3)
                throw new InvalidOperationException($"Missing saved sand triangles on playground {site.osmId}.");

            var mesh = BuildGround(site, filter.transform, source, soil, terrain);
            try
            {
                ValidateGround(site, terrain, mesh);
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
            mesh = SaveGround(mesh, site.osmId);
            filter.sharedMesh = mesh;
            renderer.sharedMaterials = new[] { material };
            site.ground.sharedMesh = null;
            site.ground.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(site.ground);
        }
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"PLAYGROUND_WALKTHROUGH_COMPLETE sites={sites.Length} retainedEquipment={sites.Sum(site => site.equipment.Length)} confirmedEquipmentSites={sites.Count(site => site.equipmentPhotoConfirmed)}");
    }

    private static Mesh BuildGround(PlaygroundSite site, Transform transform, Mesh source, int soil, MeshCollider terrain)
    {
        var original = source.vertices.Select(transform.TransformPoint).ToArray();
        var triangles = source.GetTriangles(soil);
        var boundary = site.footprint.Select(XZ).ToArray();
        float longest = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            var a = original[triangles[i]];
            var b = original[triangles[i + 1]];
            var c = original[triangles[i + 2]];
            longest = Mathf.Max(longest, Vector3.Distance(a, b), Vector3.Distance(b, c), Vector3.Distance(c, a));
        }
        int divisions = Mathf.Clamp(Mathf.CeilToInt(longest / .50f), 4, 90);
        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var colors = new List<Color>();
        var indices = new List<int>();
        var weld = new Dictionary<Vector2Int, int>();

        int Vertex(Vector3 world)
        {
            var flat = XZ(world);
            var key = new Vector2Int(Mathf.RoundToInt(flat.x * 10000), Mathf.RoundToInt(flat.y * 10000));
            if (weld.TryGetValue(key, out int existing))
                return existing;
            float distance = EdgeDistance(flat, boundary);
            float ground = GroundY(terrain, flat);
            float sandClearance = world.y - ground;
            // Keep the exact old interior datum for equipment where the original floor is
            // visible. Blend down to the measured adjacent soil inside the saved footprint.
            float innerHeight = Mathf.Max(world.y, ground + EdgeLift);
            float edgeToInterior = Mathf.SmoothStep(0, 1, Mathf.Clamp01(distance / EdgeDepth));
            world.y = Mathf.Lerp(ground + EdgeLift, innerHeight, edgeToInterior);
            int index = vertices.Count;
            vertices.Add(transform.InverseTransformPoint(world));
            uv.Add(new Vector2(flat.x, -flat.y) / 1.7f);
            // Keep soil that was above the old sand plane visibly exposed. The green
            // channel preserves the sign of the old gap, which is independent of
            // the shared render/physics mesh's seam-safe new height.
            colors.Add(new Color(Mathf.Clamp01(distance * .5f), Mathf.Clamp01((sandClearance + .10f) * 5), 0, 1));
            weld.Add(key, index);
            return index;
        }

        void Triangle(int a, int b, int c)
        {
            if (a == b || b == c || a == c)
                return;
            var normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            if (normal.sqrMagnitude < 1e-12f)
                return;
            if (normal.y < 0)
                (b, c) = (c, b);
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        // Split the already approved floor triangles at the same interval along all shared
        // edges. This retains the original concave footprint and prevents T-junctions.
        for (int triangle = 0; triangle < triangles.Length; triangle += 3)
        {
            var a = original[triangles[triangle]];
            var ab = original[triangles[triangle + 1]] - a;
            var ac = original[triangles[triangle + 2]] - a;
            var grid = new int[divisions + 1, divisions + 1];
            for (int i = 0; i <= divisions; i++)
                for (int j = 0; j <= divisions - i; j++)
                    grid[i, j] = Vertex(a + (ab * i + ac * j) / divisions);
            for (int i = 0; i < divisions; i++)
                for (int j = 0; j < divisions - i; j++)
                {
                    Triangle(grid[i, j], grid[i + 1, j], grid[i, j + 1]);
                    if (j + i < divisions - 1)
                        Triangle(grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1]);
                }
        }
        var mesh = new Mesh
        {
            name = SoilMeshVersion + site.osmId,
            indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetColors(colors);
        mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static void ValidateGround(PlaygroundSite site, MeshCollider terrain, Mesh mesh)
    {
        var probe = new GameObject("Temporary playground seam audit") { hideFlags = HideFlags.HideAndDontSave };
        probe.transform.SetParent(site.transform, false);
        var proposed = probe.AddComponent<MeshCollider>();
        proposed.sharedMesh = mesh;
        Physics.SyncTransforms();
        int seams = 0;
        float largestSeam = 0;
        float largestSupportDelta = 0;
        try
        {
            var boundary = site.footprint.Select(XZ).ToArray();
            for (int i = 0; i < boundary.Length; i++)
            {
                var a = boundary[i];
                var b = boundary[(i + 1) % boundary.Length];
                var edge = b - a;
                if (edge.sqrMagnitude < .01f)
                    continue;
                var normal = new Vector2(-edge.y, edge.x).normalized * .08f;
                int count = Mathf.Max(2, Mathf.CeilToInt(edge.magnitude / .75f));
                for (int j = 0; j < count; j++)
                {
                    var middle = Vector2.Lerp(a, b, (j + .5f) / count);
                    var point = middle + normal;
                    if (!TryHeight(site.ground, point, out _))
                        point = middle - normal;
                    if (!TryHeight(site.ground, point, out _) || !TryHeight(proposed, point, out float after))
                        throw new InvalidOperationException($"Missing floor beside playground {site.osmId} edge {i} at {point}.");
                    largestSeam = Mathf.Max(largestSeam, Mathf.Abs(after - GroundY(terrain, point)));
                    seams++;
                }
            }
            foreach (var item in site.equipment)
            {
                var point = XZ(item.position);
                if (!TryHeight(site.ground, point, out float before) || !TryHeight(proposed, point, out float after))
                    throw new InvalidOperationException($"Equipment {item.name} has no ground on playground {site.osmId}.");
                largestSupportDelta = Mathf.Max(largestSupportDelta, Mathf.Abs(after - before));
            }
            if (seams < 8 || largestSeam > .025f || largestSupportDelta > .018f)
                throw new InvalidOperationException($"Playground {site.osmId} has a seam of {largestSeam:F3} m or equipment support change of {largestSupportDelta:F3} m.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(probe);
        }
        Debug.Log($"PLAYGROUND_WALKTHROUGH_SITE id={site.osmId} edgeChecks={seams} maximumSeam={largestSeam:F3} equipmentChecks={site.equipment.Length} maximumSupportDelta={largestSupportDelta:F3}");
    }

    private static Material GetSoilMaterial(PlaygroundSite[] sites)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material)
            return material;
        var source = sites.SelectMany(site => site.GetComponent<MeshRenderer>().sharedMaterials)
            .FirstOrDefault(m => m && m.name == "PlaygroundSand");
        var shader = Shader.Find("Street/PlaygroundSoil");
        if (!source || !shader)
            throw new InvalidOperationException("The saved sand textures and playground soil shader are required.");
        material = new Material(shader) { name = "PlaygroundCompactedSoil", enableInstancing = true };
        foreach (string property in new[] { "_MainTex", "_BumpMap", "_MaskMap" })
            material.SetTexture(property, source.GetTexture(property));
        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static Mesh SaveGround(Mesh mesh, long siteId)
    {
        string path = $"{Root}/Meshes/PlaygroundIntegratedSoil_{siteId}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!saved)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, saved);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(saved);
        return saved;
    }

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);

    private static float GroundY(MeshCollider terrain, Vector2 point)
    {
        if (!TryHeight(terrain, point, out float height))
            throw new InvalidOperationException($"No saved ground under playground vertex {point}.");
        return height;
    }

    private static bool TryHeight(MeshCollider collider, Vector2 point, out float height)
    {
        float y = collider.bounds.max.y + 1;
        var ray = new Ray(new Vector3(point.x, y, point.y), Vector3.down);
        if (collider.Raycast(ray, out var hit, Mathf.Max(2, collider.bounds.size.y + 2)) && hit.normal.y > .7f)
        {
            height = hit.point.y;
            return true;
        }
        height = 0;
        return false;
    }

    private static float EdgeDistance(Vector2 point, Vector2[] boundary)
    {
        float squared = float.PositiveInfinity;
        for (int i = 0; i < boundary.Length; i++)
        {
            var from = boundary[i];
            var edge = boundary[(i + 1) % boundary.Length] - from;
            float along = Mathf.Clamp01(Vector2.Dot(point - from, edge) / Mathf.Max(.000001f, edge.sqrMagnitude));
            squared = Mathf.Min(squared, (point - from - edge * along).sqrMagnitude);
        }
        return Mathf.Sqrt(squared);
    }
}
