using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Import only the three reduced CC0 meshes; the 4.6M-polygon Blender source stays outside Assets/Git.
internal static class OptimizedTreeBuilder
{
    private static readonly System.Collections.Generic.Dictionary<int, Mesh> sources = new System.Collections.Generic.Dictionary<int, Mesh>();
    private static readonly System.Collections.Generic.Dictionary<int, Mesh> variants = new System.Collections.Generic.Dictionary<int, Mesh>();
    internal static void Initialize() { sources.Clear(); variants.Clear(); }

    internal static GameObject Create(string asset, float height, int seed = 185)
    {
        var root = new GameObject("Лиственное дерево · естественная разветвлённая крона");
        var renderers = new Renderer[4];
        for (int lod = 0; lod < 4; lod++)
        {
            int variant = ((seed % 4) + 4) % 4;
            var mesh = Variant(lod, variant);
            var part = new GameObject("LOD " + lod);
            part.transform.SetParent(root.transform, false);
            // Decimated leaf cards are enlarged for coverage; their bounds differ between LODs.
            // Scaling each LOD by its own bounds used to shrink the whole trunk/branches at a switch.
            part.transform.localScale = Vector3.one * (height / sources[0].bounds.size.y);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { StreetSurfaceLibrary.ModelBark(true), StreetSurfaceLibrary.ModelLeaves(), StreetSurfaceLibrary.ModelBark(false) };
            renderer.shadowCastingMode = lod == 3 ? ShadowCastingMode.Off : ShadowCastingMode.TwoSided;
            renderers[lod] = renderer;
        }
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(.24f, new[] { renderers[0] }), new LOD(.11f, new[] { renderers[1] }),
            new LOD(.045f, new[] { renderers[2] }), new LOD(.012f, new[] { renderers[3] }) });
        group.RecalculateBounds();
        var trunk = root.AddComponent<CapsuleCollider>();
        trunk.height = Mathf.Min(3.8f, height * .43f); trunk.center = Vector3.up * (trunk.height * .5f);
        trunk.radius = height * .023f;
        return EnvironmentMesh.SavePrefab(root, asset);
    }

    private static Mesh Variant(int lod, int variant)
    {
        int key = variant * 4 + lod;
        if (variants.TryGetValue(key, out var ready)) return ready;
        if (!sources.TryGetValue(lod, out var source))
            sources[lod] = source = lod == 3 ? FarMesh(sources[2]) : Import(lod, EnvironmentMesh.Root + "/Meshes/NaturalBroadleaf_LOD" + lod + ".asset");
        if (variant == 0) return variants[key] = source;
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = "NaturalBroadleaf_V" + variant + "_LOD" + lod;
        var positions = mesh.vertices; var normals = mesh.normals; var colors = mesh.colors;
        float height = sources[0].bounds.size.y;
        // Smooth deformation of the actual CC0 branch/leaf topology. It is shared by every LOD,
        // keeps the low trunk/collider in place, and changes the silhouette rather than just yaw.
        float sx = variant == 1 ? .90f : variant == 2 ? 1.13f : .97f;
        float sz = variant == 1 ? 1.09f : variant == 2 ? .91f : 1.15f;
        float twist = variant == 1 ? .17f : variant == 2 ? -.13f : .08f;
        Vector2 lean = variant == 1 ? new Vector2(.022f, -.009f) : variant == 2 ? new Vector2(-.016f, .012f) : new Vector2(.008f, .023f);
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 p = positions[i];
            float u = Mathf.Clamp01((p.y / height - .12f) / .73f), w = u * u * (3 - 2 * u);
            float dw = u > 0 && u < 1 ? 6 * u * (1 - u) / (.73f * height) : 0;
            float a = twist * w, c = Mathf.Cos(a), s = Mathf.Sin(a);
            float x = c * p.x - s * p.z, z = s * p.x + c * p.z;
            float scaleX = Mathf.Lerp(1, sx, w), scaleZ = Mathf.Lerp(1, sz, w);
            positions[i] = new Vector3(x * scaleX + lean.x * p.y * w, p.y, z * scaleZ + lean.y * p.y * w);
            // Inverse-transpose Jacobian preserves leaf normals under the non-uniform crown bend.
            Vector3 dx = new Vector3(c * scaleX, 0, s * scaleZ);
            Vector3 dz = new Vector3(-s * scaleX, 0, c * scaleZ);
            Vector3 dy = new Vector3(-twist * dw * z * scaleX + x * (sx - 1) * dw + lean.x * (w + p.y * dw), 1,
                twist * dw * x * scaleZ + z * (sz - 1) * dw + lean.y * (w + p.y * dw));
            Vector3 n = normals[i];
            normals[i] = (Vector3.Cross(dy, dz) * n.x + Vector3.Cross(dz, dx) * n.y + Vector3.Cross(dx, dy) * n.z).normalized;
            float shade = .96f + .035f * Mathf.Sin(p.x * .63f + p.z * .45f + variant);
            colors[i] = new Color(shade, shade, shade, colors[i].a);
        }
        mesh.vertices = positions; mesh.normals = normals; mesh.colors = colors;
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        string path = EnvironmentMesh.Root + "/Meshes/" + mesh.name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); mesh = saved; }
        else AssetDatabase.CreateAsset(mesh, path);
        return variants[key] = mesh;
    }

    // A cheap fourth LOD for the georeferenced forest. Keep complete leaf islands (and their UVs),
    // never a random triangle subset. Small wood branches can merge at the distant pixel scale.
    private static Mesh FarMesh(Mesh source)
    {
        var sourceVertices = source.vertices; var sourceNormals = source.normals; var sourceUV = source.uv;
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
        var groups = new List<int>[] { new List<int>(), new List<int>(), new List<int>() };
        foreach (int material in new[] { 0, 2 })
        {
            var map = new Dictionary<Vector3Int, int>(); var lookup = new Dictionary<int, int>();
            var sums = new List<Vector3>(); var normalSums = new List<Vector3>(); var uvSums = new List<Vector2>(); var counts = new List<int>();
            var indices = source.GetTriangles(material);
            foreach (int index in indices.Distinct())
            {
                Vector3 p = sourceVertices[index];
                var cell = new Vector3Int(Mathf.RoundToInt(p.x / .12f), Mathf.RoundToInt(p.y / .12f), Mathf.RoundToInt(p.z / .12f));
                if (!map.TryGetValue(cell, out int cluster))
                {
                    map[cell] = cluster = sums.Count;
                    sums.Add(Vector3.zero); normalSums.Add(Vector3.zero); uvSums.Add(Vector2.zero); counts.Add(0);
                }
                lookup[index] = cluster; sums[cluster] += p; normalSums[cluster] += sourceNormals[index]; uvSums[cluster] += sourceUV[index]; counts[cluster]++;
            }
            int start = vertices.Count;
            for (int i = 0; i < sums.Count; i++)
            {
                vertices.Add(sums[i] / counts[i]); normals.Add(normalSums[i].sqrMagnitude > .00001f ? normalSums[i].normalized : Vector3.up);
                uv.Add(uvSums[i] / counts[i]);
            }
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = start + lookup[indices[i]], b = start + lookup[indices[i + 1]], c = start + lookup[indices[i + 2]];
                if (a == b || b == c || c == a || Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).sqrMagnitude < .00000001f) continue;
                groups[material].Add(a); groups[material].Add(b); groups[material].Add(c);
            }
        }
        var leaves = source.GetTriangles(1); var parent = Enumerable.Range(0, sourceVertices.Length).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int i = 0; i < leaves.Length; i += 3)
        { parent[Find(leaves[i + 1])] = Find(leaves[i]); parent[Find(leaves[i + 2])] = Find(leaves[i]); }
        int component = 0;
        foreach (var island in leaves.Distinct().GroupBy(Find).OrderBy(g => g.Key))
        {
            if (component++ % 6 != 0) continue;
            var ids = island.ToArray();
            Vector3 center = ids.Aggregate(Vector3.zero, (a, i) => a + sourceVertices[i]) / ids.Length;
            var map = new Dictionary<int, int>();
            foreach (int i in ids)
            {
                map[i] = vertices.Count;
                vertices.Add(center + (sourceVertices[i] - center) * 2.0f);
                normals.Add(sourceNormals[i]); uv.Add(sourceUV[i]);
            }
            // All triangles of the island survive together. Source submesh order is retained.
            for (int i = 0; i < leaves.Length; i += 3)
                if (map.ContainsKey(leaves[i]))
                { groups[1].Add(map[leaves[i]]); groups[1].Add(map[leaves[i + 1]]); groups[1].Add(map[leaves[i + 2]]); }
        }
        var mesh = new Mesh { name = "NaturalBroadleaf_LOD3", indexFormat = IndexFormat.UInt32,
            vertices = vertices.ToArray(), normals = normals.ToArray(), uv = uv.ToArray(),
            colors = Enumerable.Repeat(Color.white, vertices.Count).ToArray(), subMeshCount = 3 };
        for (int i = 0; i < 3; i++) mesh.SetTriangles(groups[i], i);
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        string path = EnvironmentMesh.Root + "/Meshes/NaturalBroadleaf_LOD3.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); mesh = saved; }
        else AssetDatabase.CreateAsset(mesh, path);
        Debug.Log("NATURAL_TREE LOD3 triangles=" + groups.Sum(g => g.Count) / 3);
        return mesh;
    }

    private static Mesh Import(int lod, string output)
    {
        using (var reader = new BinaryReader(File.OpenRead("Assets/ThirdParty/PolyHaven/OptimizedTrees/Broadleaf_LOD" + lod + ".bytes")))
        {
            if (reader.ReadInt32() != 1) throw new InvalidOperationException("Unknown optimized tree format");
            int count = reader.ReadInt32(), submeshes = reader.ReadInt32();
            var positions = new Vector3[count]; var normals = new Vector3[count]; var uv = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                normals[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                uv[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }
            var mesh = new Mesh { name = "NaturalBroadleaf_LOD" + lod, indexFormat = IndexFormat.UInt32,
                vertices = positions, normals = normals, uv = uv, colors = Enumerable.Repeat(Color.white, count).ToArray(), subMeshCount = submeshes };
            string[] expected = { "tree_small_02_branches", "tree_small_02_leaves", "tree_small_02_trunk" };
            for (int submesh = 0; submesh < submeshes; submesh++)
            {
                string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadInt32()));
                if (name != expected[submesh]) throw new InvalidOperationException("Unexpected tree material: " + name);
                var indices = new int[reader.ReadInt32()];
                for (int i = 0; i < indices.Length; i++) indices[i] = reader.ReadInt32();
                mesh.SetTriangles(indices, submesh);
            }
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(output);
            if (saved) { EditorUtility.CopySerialized(mesh, saved); UnityEngine.Object.DestroyImmediate(mesh); mesh = saved; }
            else AssetDatabase.CreateAsset(mesh, output);
            Debug.Log("NATURAL_TREE LOD" + lod + " triangles=" + mesh.triangles.Length / 3);
            return mesh;
        }
    }
}
