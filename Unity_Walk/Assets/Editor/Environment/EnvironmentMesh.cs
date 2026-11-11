using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Editor-only construction. The player uses saved meshes, not a runtime generator.
internal sealed class EnvironmentMesh
{
    internal const string Root = "Assets/StreetGenerated/Pilot";
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector3> normals = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<Vector4> uvs = new List<Vector4>();
    private bool hasWindows;
    private readonly Dictionary<Material, List<int>> triangles = new Dictionary<Material, List<int>>();

    internal static void EnsureFolders()
    {
        foreach (string folder in new[] { "Materials", "Meshes", "Prefabs" })
            Directory.CreateDirectory(Root + "/" + folder);
        AssetDatabase.Refresh();
    }

    internal static Material Material(string name, Color color, float kind = 0, float smoothness = .15f,
        float metallic = 0, bool foliage = false, Color emission = default(Color))
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var shader = Shader.Find("Street/Surface");
            if (!shader) throw new InvalidOperationException("Street/Surface shader not found");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Street/Surface");
        material.SetColor("_Color", color);
        material.SetFloat("_Kind", kind);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Cull", foliage ? 0 : 2);
        material.SetFloat("_VertexTint", foliage ? 1 : 0);
        material.SetColor("_EmissionColor", emission);
        material.enableInstancing = true;
        StreetSurfaceLibrary.ApplyNamed(material, name);
        EditorUtility.SetDirty(material);
        return material;
    }

    internal void Face(Material material, Vector3 normal, params Vector3[] points)
    {
        AddFace(material, points, points.Select(_ => normal).ToArray(), Color.white);
    }

    internal void Surface(Material material, Vector3[] points, Vector3[] faceNormals) =>
        AddFace(material, points, faceNormals, Color.white);

    private void AddFace(Material material, Vector3[] points, Vector3[] faceNormals, Color tint)
    {
        if (!triangles.TryGetValue(material, out var indices))
            triangles.Add(material, indices = new List<int>());
        int start = vertices.Count;
        vertices.AddRange(points);
        normals.AddRange(faceNormals);
        bool window = material.shader.name == "Street/Glass";
        Vector4[] paneUV = window ? WindowCoordinates(points, faceNormals[0]) : null;
        hasWindows |= window;
        for (int i = 0; i < points.Length; i++) { colors.Add(tint); uvs.Add(window ? paneUV[i] : Vector4.zero); }
        Vector3 expected = faceNormals.Aggregate(Vector3.zero, (sum, n) => sum + n);
        bool reverse = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), expected) < 0;
        for (int i = 1; i < points.Length - 1; i++)
        {
            indices.Add(start);
            indices.Add(start + (reverse ? i + 1 : i));
            indices.Add(start + (reverse ? i : i + 1));
        }
    }

    internal void WindowFace(Material material, Vector3 normal, Vector3[] points, Vector4[] coordinates)
    {
        int start = vertices.Count;
        Face(material, normal, points);
        for (int i = 0; i < coordinates.Length; i++) uvs[start + i] = coordinates[i];
    }

    internal static Vector4[] WindowCoordinates(Vector3[] points, Vector3 normal)
    {
        Vector3 across = Mathf.Abs(normal.y) > .9f ? Vector3.right : Vector3.Cross(Vector3.up, normal).normalized;
        Vector3 up = Vector3.Cross(normal, across).normalized;
        float minX = points.Min(p => Vector3.Dot(p, across)), maxX = points.Max(p => Vector3.Dot(p, across));
        float minY = points.Min(p => Vector3.Dot(p, up)), maxY = points.Max(p => Vector3.Dot(p, up));
        Vector3 center = points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Length;
        float seed = Mathf.Repeat(Mathf.Sin(Vector3.Dot(center, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f, 1);
        return points.Select(p => new Vector4((Vector3.Dot(p, across) - minX) / Mathf.Max(.001f, maxX - minX),
            (Vector3.Dot(p, up) - minY) / Mathf.Max(.001f, maxY - minY), seed, 1)).ToArray();
    }

    // Preserve the original imported house topology; map each connected glass plane, not the whole material batch.
    internal static void MapWindowPlanes(Mesh mesh)
    {
        var vertices = mesh.vertices; var normals = mesh.normals; var indices = mesh.triangles;
        var parent = Enumerable.Range(0, vertices.Length).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int i = 0; i < indices.Length; i += 3)
        { parent[Find(indices[i + 1])] = Find(indices[i]); parent[Find(indices[i + 2])] = Find(indices[i]); }
        var uv = new Vector4[vertices.Length];
        foreach (var group in Enumerable.Range(0, vertices.Length).GroupBy(Find))
        {
            var ids = group.ToArray(); var coords = WindowCoordinates(ids.Select(i => vertices[i]).ToArray(), normals[ids[0]]);
            for (int i = 0; i < ids.Length; i++) uv[ids[i]] = coords[i];
        }
        mesh.SetUVs(0, uv);
    }

    internal void Box(Material material, Vector3 center, Vector3 size, Quaternion rotation = default(Quaternion))
    {
        if (rotation == default(Quaternion)) rotation = Quaternion.identity;
        Vector3 x = rotation * Vector3.right * size.x * .5f;
        Vector3 y = rotation * Vector3.up * size.y * .5f;
        Vector3 z = rotation * Vector3.forward * size.z * .5f;
        Face(material, rotation * Vector3.up, center - x + y - z, center - x + y + z, center + x + y + z, center + x + y - z);
        Face(material, rotation * Vector3.down, center - x - y - z, center + x - y - z, center + x - y + z, center - x - y + z);
        Face(material, rotation * Vector3.right, center + x - y - z, center + x + y - z, center + x + y + z, center + x - y + z);
        Face(material, rotation * Vector3.left, center - x - y - z, center - x - y + z, center - x + y + z, center - x + y - z);
        Face(material, rotation * Vector3.forward, center - x - y + z, center + x - y + z, center + x + y + z, center - x + y + z);
        Face(material, rotation * Vector3.back, center - x - y - z, center - x + y - z, center + x + y - z, center + x - y - z);
    }

    // Rounded horizontal corners and bevelled upper/lower edges, including tapered bin bodies.
    internal void RoundedBox(Material material, Vector3 center, Vector3 size, float radius, float bottomScale = 1)
    {
        const int segments = 4;
        var outline = new List<Vector3>();
        for (int corner = 0; corner < 4; corner++)
        {
            float angle = corner * Mathf.PI * .5f;
            Vector3 c = new Vector3((corner == 0 || corner == 3 ? 1 : -1) * (size.x * .5f - radius), 0,
                (corner < 2 ? 1 : -1) * (size.z * .5f - radius));
            for (int i = 0; i <= segments; i++)
            {
                float a = angle + i * Mathf.PI * .5f / segments;
                outline.Add(c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius);
            }
        }
        float bevel = Mathf.Min(.018f, size.y * .2f);
        Vector3[] Ring(float y, float scale, bool inset) => outline.Select(v => center + new Vector3(
            v.x * scale * (inset ? .97f : 1), y, v.z * scale * (inset ? .97f : 1))).ToArray();
        var rings = new[] { Ring(-size.y * .5f, bottomScale, true), Ring(-size.y * .5f + bevel, bottomScale, false),
            Ring(size.y * .5f - bevel, 1, false), Ring(size.y * .5f, 1, true) };
        Face(material, Vector3.down, rings[0]);
        Face(material, Vector3.up, rings[3]);
        for (int r = 0; r < rings.Length - 1; r++)
            for (int i = 0; i < outline.Count; i++)
            {
                int j = (i + 1) % outline.Count;
                Vector3 normal = Vector3.Cross(rings[r + 1][i] - rings[r][i], rings[r][j] - rings[r][i]).normalized;
                Vector3 radial = (rings[r][i] + rings[r][j]) * .5f - center;
                if (Vector3.Dot(normal, new Vector3(radial.x, 0, radial.z)) < 0) normal = -normal;
                Face(material, normal, rings[r][i], rings[r][j], rings[r + 1][j], rings[r + 1][i]);
            }
    }

    internal void Cylinder(Material material, Vector3 from, Vector3 to, float radius, float topRadius = -1, int sides = 10)
    {
        if (topRadius < 0) topRadius = radius;
        Vector3 axis = (to - from).normalized;
        Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.forward).normalized;
        Vector3 v = Vector3.Cross(axis, u);
        var bottom = new Vector3[sides];
        var top = new Vector3[sides];
        var radial = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2 / sides;
            radial[i] = u * Mathf.Cos(a) + v * Mathf.Sin(a);
            bottom[i] = from + radial[i] * radius;
            top[i] = to + radial[i] * topRadius;
        }
        Face(material, -axis, bottom);
        if (topRadius > 0) Face(material, axis, top);
        float slope = (radius - topRadius) / Vector3.Distance(from, to);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Vector3 n1 = (radial[i] + axis * slope).normalized;
            Vector3 n2 = (radial[j] + axis * slope).normalized;
            AddFace(material, new[] { bottom[i], bottom[j], top[j], top[i] }, new[] { n1, n2, n2, n1 }, Color.white);
        }
    }

    internal void Ellipsoid(Material material, Vector3 center, Vector3 radii, int sides = 10, int rings = 6)
    {
        Vector3 P(float a, float b) => center + Vector3.Scale(radii,
            new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a), Mathf.Cos(a) * Mathf.Sin(b)));
        Vector3 N(Vector3 p) => new Vector3((p.x - center.x) / (radii.x * radii.x),
            (p.y - center.y) / (radii.y * radii.y), (p.z - center.z) / (radii.z * radii.z)).normalized;
        for (int y = 0; y < rings; y++)
            for (int x = 0; x < sides; x++)
            {
                float a = -Mathf.PI * .5f + y * Mathf.PI / rings;
                float b = (x + 1) * Mathf.PI * 2 / sides;
                var p = new[] { P(a, b), P(a, b - Mathf.PI * 2 / sides), P(a + Mathf.PI / rings, b - Mathf.PI * 2 / sides), P(a + Mathf.PI / rings, b) };
                if (y == 0) p = new[] { p[0], p[2], p[3] };
                if (y == rings - 1) p = new[] { p[0], p[1], p[2] };
                AddFace(material, p, p.Select(N).ToArray(), Color.white);
            }
    }

    internal void Leaf(Material material, Vector3 center, Quaternion rotation, float length, Color tint)
    {
        var outline = new[] { new Vector3(0, 0, -.5f), new Vector3(-.29f, 0, -.19f),
            new Vector3(-.26f, 0, .21f), new Vector3(0, 0, .5f), new Vector3(.26f, 0, .21f), new Vector3(.29f, 0, -.19f) };
        Vector3 middle = center + rotation * Vector3.up * length * .075f;
        for (int i = 0; i < outline.Length; i++)
        {
            var p = new[] { middle, center + rotation * outline[i] * length,
                center + rotation * outline[(i + 1) % outline.Length] * length };
            Vector3 n = rotation * Vector3.up;
            AddFace(material, p, new[] { n, n, n }, tint);
        }
    }

    internal void GrassTuft(Material material, Vector3 position, float height, float angle, Color tint)
    {
        // Six shader-defined stems per card, three intersecting cards: 18 stems at 12 vertices.
        // This is denser than separate blades while reducing mesh size and vertex processing.
        for (int card = 0; card < 3; card++)
        {
            float a = angle + card * Mathf.PI / 3;
            Vector3 side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * height * .70f;
            Vector3 bend = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)) * height * .13f;
            Vector3 normal = (Vector3.Cross(side, Vector3.up).normalized * .25f + Vector3.up * .75f).normalized;
            int start = uvs.Count;
            AddFace(material, new[] { position - side, position + side, position + side + Vector3.up * height + bend,
                position - side + Vector3.up * height + bend }, new[] { normal, normal, normal, normal }, tint);
            uvs[start] = new Vector4(0, 0, 0, 0); uvs[start + 1] = new Vector4(1, 0, 0, 0);
            uvs[start + 2] = new Vector4(1, 1, 0, 0); uvs[start + 3] = new Vector4(0, 1, 0, 0);
        }
    }

    internal void GrassBlade(Material material, Vector3 position, float height, float angle, Color tint)
    {
        if (!triangles.TryGetValue(material, out var indices)) triangles.Add(material, indices = new List<int>());
        int start = vertices.Count;
        Vector3 side = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * height * .052f;
        Vector3 bend = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * height * .42f;
        Vector3 middle = position + Vector3.up * height * .62f + bend * .4f;
        Vector3 tip = position + Vector3.up * height + bend;
        Vector3 normal = (Vector3.Cross(side, Vector3.up).normalized * .3f + Vector3.up * .7f).normalized;
        // Shared middle vertices keep the bent silhouette at five vertices instead of seven.
        vertices.AddRange(new[] { position - side, position + side, middle + side * .55f, middle - side * .55f, tip });
        for (int i = 0; i < 5; i++) { normals.Add(normal); colors.Add(tint); }
        uvs.AddRange(new[] { new Vector4(0, 0, 0, 0), new Vector4(1, 0, 0, 0), new Vector4(1, .62f, 0, 0), new Vector4(0, .62f, 0, 0), new Vector4(.5f, 1, 0, 0) });
        bool reverse = Vector3.Dot(Vector3.Cross(side, middle - position), normal) < 0;
        int[] faces = { 0, 1, 2, 0, 2, 3, 3, 2, 4 };
        for (int i = 0; i < faces.Length; i += 3)
        {
            indices.Add(start + faces[i]); indices.Add(start + faces[i + (reverse ? 2 : 1)]); indices.Add(start + faces[i + (reverse ? 1 : 2)]);
        }
    }

    internal void FoliageCard(Material material, Vector3 center, Quaternion rotation, Vector2 size, Vector3 normal, Color tint)
    {
        Vector3 x = rotation * Vector3.right * size.x * .5f;
        Vector3 y = rotation * Vector3.up * size.y * .5f;
        int start = uvs.Count;
        AddFace(material, new[] { center-x-y, center+x-y, center+x+y, center-x+y },
            new[] { normal, normal, normal, normal }, tint);
        uvs[start]=new Vector4(0,0,0,0); uvs[start+1]=new Vector4(1,0,0,0);
        uvs[start+2]=new Vector4(1,1,0,0); uvs[start+3]=new Vector4(0,1,0,0);
    }

    internal void Path(Material surface, Material edging, Vector3[] points, float width, float height = .14f)
    {
        var left = new Vector3[points.Length];
        var right = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 before = (points[i] - points[Math.Max(0, i - 1)]).normalized;
            Vector3 after = (points[Math.Min(points.Length - 1, i + 1)] - points[i]).normalized;
            if (i == 0) before = after;
            if (i == points.Length - 1) after = before;
            Vector3 n1 = new Vector3(before.z, 0, -before.x), n2 = new Vector3(after.z, 0, -after.x);
            Vector3 n = (n1 + n2).normalized;
            float miter = width * .5f / Mathf.Max(.5f, Vector3.Dot(n, n2));
            left[i] = points[i] + n * miter + Vector3.up * height;
            right[i] = points[i] - n * miter + Vector3.up * height;
        }
        for (int i = 0; i < points.Length - 1; i++)
        {
            Face(surface, Vector3.up, left[i], right[i], right[i + 1], left[i + 1]);
            foreach (var edge in new[] { left, right })
            {
                Vector3 from = edge[i], to = edge[i + 1], direction = to - from;
                if (edging)
                    Box(edging, (from + to) * .5f - Vector3.up * .055f,
                        new Vector3(.11f, .15f, direction.magnitude), Quaternion.LookRotation(direction));
                else
                    Face(surface, Vector3.Cross(direction, Vector3.up).normalized,
                        from, to, to - Vector3.up * .1f, from - Vector3.up * .1f);
            }
        }
    }

    internal GameObject Create(string assetName, string objectName, string assetRoot = Root, bool worldMappedOnly = false)
    {
        var mesh = new Mesh { name = assetName, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.tangents = Tangents(normals);
        // World-mapped building materials do not read UV0 or vertex colours.
        if (!worldMappedOnly) mesh.SetColors(colors);
        if (!worldMappedOnly || hasWindows) mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Count;
        int submesh = 0;
        foreach (var indices in triangles.Values) mesh.SetTriangles(indices, submesh++);
        mesh.RecalculateBounds();
        string path = assetRoot + "/Meshes/" + assetName + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(mesh, saved);
            UnityEngine.Object.DestroyImmediate(mesh);
            mesh = saved;
        }
        else AssetDatabase.CreateAsset(mesh, path);
        var root = new GameObject(objectName);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterials = triangles.Keys.ToArray();
        return root;
    }

    internal static Vector4[] Tangents(IEnumerable<Vector3> normals)
    {
        return normals.Select(n =>
        {
            Vector3 tangent = Mathf.Abs(n.y) > .8f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized;
            return new Vector4(tangent.x, tangent.y, tangent.z, 1);
        }).ToArray();
    }

    internal static GameObject SavePrefab(GameObject root, string name, string assetRoot = Root)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, assetRoot + "/Prefabs/" + name + ".prefab");
        UnityEngine.Object.DestroyImmediate(root);
        if (!prefab) throw new InvalidOperationException("Could not save prefab: " + name);
        return prefab;
    }

    internal static BoxCollider Collider(GameObject root, Vector3 center, Vector3 size)
    {
        var collider = root.AddComponent<BoxCollider>();
        collider.center = center;
        collider.size = size;
        return collider;
    }

    internal static void Label(Transform parent, string text, Vector3 position, Quaternion rotation, float size, Color color)
    {
        var root = new GameObject(text);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = position;
        root.transform.localRotation = rotation;
        var label = root.AddComponent<TextMesh>();
        label.text = text;
        label.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/PTSans.ttf");
        label.fontSize = 64;
        label.characterSize = size;
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.color = color;
        label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
    }
}
