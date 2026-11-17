using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class StreetAutumnGrassBlades
{
    // These colours are linear. A muted palette leaves room for the warm directional light.
    private static readonly Color Green = new(.145f, .211f, .067f, 1);
    private static readonly Color Olive = new(.205f, .234f, .083f, 1);
    private static readonly Color Straw = new(.345f, .259f, .110f, 1);

    internal static bool TryBuild(Mesh source, Transform place, int grassSubmesh, int lod,
        Func<Vector3, Vector3?> ground, out Mesh mesh, out int tufts, out int blades)
    {
        mesh = null;
        tufts = blades = 0;
        if (!source || !source.isReadable || (uint)grassSubmesh >= (uint)source.subMeshCount ||
            source.GetTopology(grassSubmesh) != MeshTopology.Triangles)
            return false;

        Vector3[] sourcePositions = source.vertices;
        Vector3[] sourceNormals = source.normals;
        Vector4[] sourceTangents = source.tangents;
        Color[] sourceColors = source.colors;
        var sourceUv = new List<Vector4>(source.vertexCount);
        source.GetUVs(0, sourceUv);

        // The authored detail meshes carry this layout. Refuse to discard any additional stream.
        if (sourceUv.Count != sourcePositions.Length || sourceNormals.Length != sourcePositions.Length ||
            sourceTangents.Length != sourcePositions.Length || sourceColors.Length != sourcePositions.Length ||
            source.vertexAttributeCount != 5 ||
            source.GetVertexAttributeDimension(VertexAttribute.TexCoord0) != 4)
            return false;

        int[] sourceGrass = source.GetIndices(grassSubmesh);
        var roots = new List<Vector3>();
        if (!ReadAuthoredTufts(sourceGrass, sourcePositions, sourceUv, roots))
            return false;

        var detail = new DetailMesh(source.subMeshCount);
        var vertexMap = new int[sourcePositions.Length];
        Array.Fill(vertexMap, -1);
        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            if (submesh == grassSubmesh)
                continue;

            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                return false;

            // Keep clover, flowers and any other material slot with its original vertex data.
            foreach (int index in source.GetIndices(submesh))
            {
                if (vertexMap[index] < 0)
                {
                    vertexMap[index] = detail.Positions.Count;
                    detail.Add(sourcePositions[index], sourceNormals[index], sourceTangents[index],
                        sourceColors[index], sourceUv[index]);
                }
                detail.Indices[submesh].Add(vertexMap[index]);
            }
        }

        var planted = new List<Vector3>(roots.Count);
        foreach (Vector3 localRoot in roots)
        {
            var site = ground(place.TransformPoint(localRoot));
            if (!site.HasValue)
                continue;

            planted.Add(site.Value);
            blades += AddTuft(detail, grassSubmesh, lod, place.InverseTransformPoint(site.Value), site.Value);
        }
        tufts = planted.Count;
        int fills = lod == 0 ? 3 : lod == 1 ? 1 : 0;
        if (fills > 0)
            tufts += AddInfill(detail, grassSubmesh, place, ground, planted, fills, ref blades);

        mesh = detail.Create(source.name + "_AutumnBlades");
        return true;
    }

    private static int AddInfill(DetailMesh detail, int submesh, Transform place,
        Func<Vector3, Vector3?> ground, List<Vector3> planted, int fills, ref int blades)
    {
        const float spacing = .15f;
        var grid = new Dictionary<Vector2Int, List<Vector3>>();
        Vector2Int Key(Vector3 site) => new(Mathf.FloorToInt(site.x / spacing), Mathf.FloorToInt(site.z / spacing));

        void Record(Vector3 site)
        {
            var key = Key(site);
            if (!grid.TryGetValue(key, out var sites))
                grid.Add(key, sites = new List<Vector3>());
            sites.Add(site);
        }

        bool TooClose(Vector3 site)
        {
            var key = Key(site);
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            {
                if (!grid.TryGetValue(key + new Vector2Int(x, z), out var sites))
                    continue;
                foreach (var other in sites)
                {
                    float dx = site.x - other.x;
                    float dz = site.z - other.z;
                    if (dx * dx + dz * dz < spacing * spacing)
                        return true;
                }
            }
            return false;
        }

        foreach (var root in planted)
            Record(root);
        int accepted = 0;
        foreach (var root in planted)
        {
            var random = new System.Random(WorldSeed(root) ^ 142739);
            float phase = Between(random, 0f, Mathf.PI * 2f);
            for (int sample = 0; sample < fills; sample++)
            {
                float angle = phase + sample * 2.39996f + Between(random, -.32f, .32f);
                float radius = Between(random, .18f, .44f);
                Vector3 candidate = root + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                if (TooClose(candidate))
                    continue;
                var hit = ground(candidate);
                if (!hit.HasValue)
                    continue;
                Record(hit.Value);
                blades += AddTuft(detail, submesh, 2, place.InverseTransformPoint(hit.Value), hit.Value);
                accepted++;
            }
        }
        return accepted;
    }

    internal static Mesh BuildStandalone(string name, IReadOnlyList<Vector3> localRoots, Transform place,
        int lod, out int blades)
    {
        var detail = new DetailMesh(1);
        blades = 0;
        foreach (Vector3 root in localRoots)
            blades += AddTuft(detail, 0, lod, root, place ? place.TransformPoint(root) : root);
        return detail.Create(name);
    }

    private static int AddTuft(DetailMesh detail, int submesh, int lod, Vector3 localRoot, Vector3 worldRoot)
    {
        var random = new System.Random(WorldSeed(worldRoot));
        float patch = Mathf.PerlinNoise(worldRoot.x * .32f + 71.3f, worldRoot.z * .32f + 41.9f);
        int closeCount = random.Next(4, 7);
        int count = lod == 0 ? closeCount : 2;
        float bladeRotation = Between(random, 0f, Mathf.PI * 2f);
        float height = Mathf.Lerp(.052f, .093f, patch) * Between(random, .85f, 1.19f);
        if (lod >= 2)
            height *= .83f;

        for (int strand = 0; strand < count; strand++)
        {
            float angle = bladeRotation + strand * 2.39996f + Between(random, -.48f, .48f);
            Vector3 facing = new(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            Vector3 side = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            float tone = Mathf.Clamp01(patch * .48f + Between(random, 0f, .55f));
            Color hue = tone < .66f ? Color.Lerp(Green, Olive, tone / .66f) :
                Color.Lerp(Olive, Straw, (tone - .66f) / .34f);
            hue *= Between(random, .86f, 1.14f);
            Vector3 spread = facing * Between(random, .009f, .036f) + side * Between(random, -.012f, .012f);
            Vector3 basePoint = localRoot - Vector3.up * .007f + spread;
            float strandHeight = Mathf.Clamp(height * Between(random, .72f, 1.08f), .038f, .109f);
            float halfWidth = Between(random, .0043f, .0072f);
            float lean = Between(random, .22f, .66f) * strandHeight;

            AddBlade(detail, submesh, lod, strand, basePoint, facing, side, strandHeight,
                halfWidth, lean, hue);
        }

        return count;
    }

    private static bool ReadAuthoredTufts(int[] indices, Vector3[] positions,
        List<Vector4> coordinates, List<Vector3> roots)
    {
        var seen = new HashSet<Vector3Int>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];
            if (!TryCardRoot(positions[a], positions[b], positions[c], coordinates[a], coordinates[b],
                    coordinates[c], out Vector3 root))
                continue; // A clipped sliver no longer gives a reliable root for a new tuft.

            Vector3Int key = Vector3Int.RoundToInt(root * 100f);
            if (seen.Add(key))
                roots.Add(root);
        }

        return roots.Count != 0 || indices.Length == 0;
    }

    private static bool TryCardRoot(Vector3 a, Vector3 b, Vector3 c, Vector4 uvA, Vector4 uvB,
        Vector4 uvC, out Vector3 root)
    {
        root = default;
        Vector2 ab = (Vector2)(uvB - uvA);
        Vector2 ac = (Vector2)(uvC - uvA);
        float determinant = ab.x * ac.y - ab.y * ac.x;
        if (Mathf.Abs(determinant) < .001f)
            return false;

        // UV (.5, 0) is the center of the card's root edge. Reconstruct it even
        // when a pavement cleanup removed the other triangle and compacted the mesh.
        Vector2 target = new(.5f - uvA.x, -uvA.y);
        float alongB = (target.x * ac.y - target.y * ac.x) / determinant;
        float alongC = (ab.x * target.y - ab.y * target.x) / determinant;
        if (Mathf.Abs(alongB) > 1.1f || Mathf.Abs(alongC) > 1.1f ||
            Mathf.Abs(1f - alongB - alongC) > 1.1f)
            return false;

        root = a + (b - a) * alongB + (c - a) * alongC;
        return true;
    }

    private static int WorldSeed(Vector3 point)
    {
        Vector3Int key = Vector3Int.RoundToInt(point * 100f);
        return unchecked(key.x * 73856093 ^ key.y * 83492791 ^ key.z * 19349663 ^ 734147);
    }

    private static float Between(System.Random random, float from, float to)
    {
        return Mathf.Lerp(from, to, (float)random.NextDouble());
    }

    private static void AddBlade(DetailMesh detail, int slot, int lod, int strand, Vector3 root,
        Vector3 facing, Vector3 side, float height, float halfWidth, float lean, Color hue)
    {
        Vector3 normal = (facing * .62f + Vector3.up * 1.7f).normalized;
        Vector4 tangent = new(side.x, side.y, side.z, -1f);
        Vector3 tip = root + Vector3.up * height + facing * lean;
        Color dark = Shade(hue, .81f);
        Color bright = Shade(hue, 1.16f);
        int start = detail.Positions.Count;

        detail.Add(root - side * halfWidth, normal, tangent, dark, new Vector4(0, 0, 0, 0));
        detail.Add(root + side * halfWidth, normal, tangent, dark, new Vector4(1, 0, 0, 0));
        if (lod >= 2)
        {
            detail.Add(tip, normal, tangent, bright, new Vector4(.5f, 1, 0, 0));
            detail.Triangle(slot, start, start + 1, start + 2, normal);
            return;
        }

        Vector3 middle = root + Vector3.up * (height * .57f) + facing * (lean * .32f);
        Vector3 spread = side * (halfWidth * .68f);
        if (lod == 0 && strand >= 2)
        {
            detail.Add(middle - spread, normal, tangent, Shade(hue, .95f), new Vector4(0, .57f, 0, 0));
            detail.Add(tip, normal, tangent, bright, new Vector4(.5f, 1, 0, 0));
            detail.Triangle(slot, start, start + 1, start + 2, normal);
            detail.Triangle(slot, start + 1, start + 2, start + 3, normal);
            return;
        }
        detail.Add(middle - spread, normal, tangent, Shade(hue, .93f), new Vector4(0, .57f, 0, 0));
        detail.Add(middle + spread, normal, tangent, Shade(hue, 1.03f), new Vector4(1, .57f, 0, 0));
        detail.Add(tip, normal, tangent, bright, new Vector4(.5f, 1, 0, 0));

        if (strand == 0 && lod == 0)
        {
            // A shallow crease changes the silhouette when the player walks around the patch.
            detail.Add(middle + facing * (halfWidth * .58f), normal, tangent, Shade(hue, 1.10f),
                new Vector4(.5f, .57f, 0, 0));
            detail.Triangle(slot, start, start + 1, start + 5, normal);
            detail.Triangle(slot, start, start + 5, start + 2, normal);
            detail.Triangle(slot, start + 1, start + 3, start + 5, normal);
            detail.Triangle(slot, start + 2, start + 5, start + 4, normal);
            detail.Triangle(slot, start + 5, start + 3, start + 4, normal);
            return;
        }

        detail.Triangle(slot, start, start + 1, start + 3, normal);
        detail.Triangle(slot, start, start + 3, start + 2, normal);
        detail.Triangle(slot, start + 2, start + 3, start + 4, normal);
    }

    private static Color Shade(Color hue, float factor)
    {
        return new Color(hue.r * factor, hue.g * factor, hue.b * factor, 1f);
    }

    private sealed class DetailMesh
    {
        internal readonly List<Vector3> Positions = new();
        internal readonly List<int>[] Indices;
        private readonly List<Vector3> normals = new();
        private readonly List<Vector4> tangents = new();
        private readonly List<Color> colors = new();
        private readonly List<Vector4> uv = new();

        internal DetailMesh(int submeshCount)
        {
            Indices = new List<int>[submeshCount];
            for (int i = 0; i < Indices.Length; i++)
                Indices[i] = new List<int>();
        }

        internal void Add(Vector3 position, Vector3 normal, Vector4 tangent, Color color, Vector4 coordinate)
        {
            Positions.Add(position);
            normals.Add(normal);
            tangents.Add(tangent);
            colors.Add(color);
            uv.Add(coordinate);
        }

        internal void Triangle(int slot, int a, int b, int c, Vector3 normal)
        {
            bool reverse = Vector3.Dot(Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]),
                normal) < 0f;
            Indices[slot].Add(a);
            Indices[slot].Add(reverse ? c : b);
            Indices[slot].Add(reverse ? b : c);
        }

        internal Mesh Create(string name)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = Positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.SetVertices(Positions);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = Indices.Length;
            for (int i = 0; i < Indices.Length; i++)
                mesh.SetTriangles(Indices[i], i, false);
            mesh.RecalculateBounds();
            Bounds padding = mesh.bounds;
            padding.Expand(.025f);
            mesh.bounds = padding;
            return mesh;
        }
    }
}
