using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

internal static class PaintedDistrictEdges
{
    private const string GroupName = "Осень · условный дальний план";
    private const string AssetFolder = "Assets/Art/PaintedDistrict/Edges";

    private static readonly Color[] Autumn =
    {
        new Color(.67f, .34f, .065f), new Color(.74f, .46f, .097f),
        new Color(.48f, .11f, .032f), new Color(.52f, .35f, .087f),
        new Color(.43f, .18f, .050f), new Color(.67f, .26f, .045f)
    };

    private static readonly Color[] Evergreen =
    {
        new Color(.18f, .28f, .137f), new Color(.20f, .32f, .155f),
        new Color(.25f, .34f, .150f)
    };

    // Authored composition anchors beyond the mapped courtyards.
    private static readonly Grove[] South =
    {
        new Grove(168, -254, 16, 13, 10), new Grove(201, -238, 13, 12, 12),
        new Grove(217, -270, 20, 16, 14), new Grove(247, -217, 16, 15, 13),
        new Grove(265, -255, 24, 16, 16), new Grove(283, -289, 27, 23, 20),
        new Grove(148, -302, 26, 20, 16), new Grove(225, -320, 29, 21, 18),
        new Grove(211, -202, 16, 11, 12), new Grove(248, -180, 29, 19, 16)
    };

    private static readonly Grove[] East =
    {
        new Grove(225, 146, 16, 13, 12), new Grove(243, 111, 18, 17, 14),
        new Grove(266, 145, 19, 17, 15), new Grove(252, 181, 22, 17, 17),
        new Grove(289, 122, 21, 18, 16), new Grove(307, 172, 23, 22, 16),
        new Grove(282, 209, 24, 18, 19), new Grove(338, 150, 29, 26, 20),
        new Grove(211, 165, 12, 16, 10), new Grove(220, 203, 20, 21, 14)
    };

    private static readonly Vector2[] SouthRoofs =
    {
        new Vector2(178, -284), new Vector2(195, -289), new Vector2(249, -302),
        new Vector2(270, -312), new Vector2(316, -247), new Vector2(328, -254)
    };

    private static readonly Vector2[] EastRoofs =
    {
        new Vector2(277, 179), new Vector2(292, 187), new Vector2(311, 200),
        new Vector2(308, 138), new Vector2(321, 140), new Vector2(330, 111)
    };

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before adding the district backdrop.");

        var previous = GameObject.Find(GroupName);
        if (previous)
            UnityEngine.Object.DestroyImmediate(previous);

        Physics.SyncTransforms();
        var ground = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(collider =>
            collider.enabled && collider.name == "Grass" && collider.sharedMesh && collider.bounds.size.x > 1000);
        var occupied = UnityEngine.Object.FindObjectsByType<LODGroup>()
            .Select(group => group.transform.position).ToArray();
        EnsureAssetFolder();
        var material = LoadMaterial("Backdrop", "Street/PaintedBackdrop");
        var contactMaterial = LoadMaterial("BackdropContacts", "Street/PaintedBackdropContact");
        var sun = RenderSettings.sun;
        var shadeDirection = Vector3.ProjectOnPlane(sun ? sun.transform.forward : new Vector3(.7f, 0, .3f),
            Vector3.up).normalized;
        var root = new GameObject(GroupName).transform;
        AddSector(root, "Юг", South, SouthRoofs, ground, material, contactMaterial, shadeDirection, occupied, 5319, 0);
        AddSector(root, "Восток", East, EastRoofs, ground, material, contactMaterial, shadeDirection, occupied, 8411, 90);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private readonly struct Grove
    {
        internal readonly Vector2 Center;
        internal readonly Vector2 Radius;
        internal readonly int Trees;

        internal Grove(float x, float z, float radiusX, float radiusZ, int trees)
        {
            Center = new Vector2(x, z);
            Radius = new Vector2(radiusX, radiusZ);
            Trees = trees;
        }
    }

    private static void AddSector(Transform root, string name, Grove[] groves, Vector2[] rooflines,
        MeshCollider ground, Material material, Material contactMaterial, Vector3 shadeDirection, Vector3[] occupied,
        int seed, float roofAngle)
    {
        var sector = Child(root, name);
        var random = new Random(seed);
        var sites = new List<Vector3>();
        var roofs = new EdgeMesh(Point(rooflines[0]));
        var contacts = new EdgeMesh(Point(groves[0].Center));
        foreach (var point in rooflines)
        {
            float width = Between(random, 9f, 14f);
            if (!TryGround(ground, point, width * .68f, out var site) ||
                occupied.Any(other => DistanceSquared(other, site) < 100f))
                continue;
            var turn = Quaternion.Euler(0, roofAngle + Between(random, -15f, 17f), 0);
            roofs.Roof(site + Vector3.down * .45f, turn, width, Between(random, 7f, 11f),
                Between(random, 6f, 10f), Between(random, 1.2f, 2.7f), Between(random, 0f, 1f));
            sites.Add(site);
        }
        if (!roofs.Empty)
            AddRenderer(sector, "Дальние крыши · условные силуэты", roofs, material, $"{seed}_Roofs");

        int treeCount = 0;
        int shrubCount = 0;
        int roofCount = sites.Count;
        // Groups have independent frustum bounds and a lower-detail mesh at distance.
        for (int i = 0; i < groves.Length; i += 2)
        {
            var origin = Point((groves[i].Center + groves[i + 1].Center) * .5f);
            var near = new EdgeMesh(origin);
            var far = new EdgeMesh(origin);
            for (int j = i; j <= i + 1; j++)
            {
                Grove grove = groves[j];
                int trees = 0;
                for (int attempt = 0; attempt < grove.Trees * 5 && trees < grove.Trees; attempt++)
                {
                    if (!TryGround(ground, Sample(grove, random, 1f), 1.7f, out var site) ||
                        occupied.Any(other => DistanceSquared(other, site) < 25f) ||
                        sites.Any(other => DistanceSquared(other, site) < 17f))
                        continue;

                    bool conifer = random.NextDouble() < .38;
                    float height = Between(random, 4.4f, 11.8f) * (conifer ? 1.12f : 1f);
                    int form = random.Next();
                    Tree(near, site, height, conifer, form, false);
                    Tree(far, site, height, conifer, form, true);
                    contacts.Contact(ground, site, shadeDirection, height * .30f, height * .20f, .42f);
                    sites.Add(site);
                    trees++;
                }
                treeCount += trees;

                for (int attempt = 0; attempt < grove.Trees / 2; attempt++)
                {
                    if (!TryGround(ground, Sample(grove, random, 1.18f), 1f, out var site) ||
                        sites.Any(other => DistanceSquared(other, site) < 3f))
                        continue;

                    float radius = Between(random, .65f, 1.3f);
                    Color color = Color.Lerp(Autumn[random.Next(Autumn.Length)], Evergreen[0], .52f);
                    near.Crown(site + new Vector3(0, radius * .43f, 0),
                        new Vector3(radius, radius * .6f, radius * .78f), color, random, 6, false);
                    near.Crown(site + new Vector3(radius * .63f, radius * .28f, radius * .22f),
                        new Vector3(radius * .64f, radius * .44f, radius * .57f), color * .9f, random, 6, false);
                    contacts.Contact(ground, site, shadeDirection, radius * 1.25f, radius * .82f, .30f);
                    shrubCount++;
                }
            }

            if (near.Empty)
                continue;
            string asset = $"{seed}_Grove_{i / 2 + 1}";
            var cluster = Child(sector, $"Деревья и кусты {i / 2 + 1}");
            var nearRenderer = AddRenderer(cluster, "LOD0", near, material, $"{asset}_0");
            if (far.Empty)
                continue;
            var farRenderer = AddRenderer(cluster, "LOD1", far, material, $"{asset}_1");
            var lod = cluster.gameObject.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(.15f, new Renderer[] { nearRenderer }), new LOD(.013f, new Renderer[] { farRenderer }) });
            lod.RecalculateBounds();
        }
        if (!contacts.Empty)
            AddRenderer(sector, "Мягкие контактные тени", contacts, contactMaterial, $"{seed}_Contacts");

        Debug.Log($"PAINTED_DISTRICT_EDGES {name}: trees={treeCount} shrubs={shrubCount} roofs={roofCount}");
    }

    private static void Tree(EdgeMesh mesh, Vector3 site, float height, bool conifer, int seed, bool distant)
    {
        var random = new Random(seed);
        var palette = conifer ? Evergreen : Autumn;
        var foliage = palette[random.Next(palette.Length)];
        float width = height * (conifer ? Between(random, .18f, .28f) : Between(random, .18f, .36f));
        mesh.Crown(site + Vector3.up * height * .28f, new Vector3(height * .032f, height * .44f, height * .029f),
            new Color(.19f, .108f, .065f), random, 5, distant);

        if (conifer)
        {
            int layers = distant ? 2 : 4;
            for (int i = 0; i < layers; i++)
            {
                float t = (float)i / (layers - 1);
                float radius = width * Mathf.Lerp(.91f, .37f, t);
                mesh.Crown(site + Vector3.up * height * Mathf.Lerp(.43f, .82f, t),
                    new Vector3(radius, height * Mathf.Lerp(.24f, .19f, t), radius * .94f),
                    foliage * Mathf.Lerp(.79f, 1.12f, t), random, distant ? 5 : 7, distant, true);
            }
            return;
        }

        var lean = new Vector3(Between(random, -.15f, .15f), 0, Between(random, -.12f, .12f)) * width;
        float crownHeight = Between(random, .24f, .35f);
        mesh.Crown(site + Vector3.up * height * .71f + lean, new Vector3(width, height * crownHeight, width * .87f),
            foliage, random, distant ? 6 : 8, distant);
        if (distant)
            return;
        for (int i = 0; i < 3; i++)
        {
            float angle = i * 2.094f + Between(random, -.38f, .38f);
            var offset = new Vector3(Mathf.Cos(angle) * width * .57f, height * Between(random, .56f, .74f),
                Mathf.Sin(angle) * width * .54f);
            mesh.Crown(site + offset, new Vector3(width * .59f, height * .22f, width * .60f),
                Color.Lerp(foliage, Autumn[random.Next(Autumn.Length)], .27f), random, 7, false);
        }
    }

    private static Vector3 Point(Vector2 point) => new Vector3(point.x, 0, point.y);

    private static Vector2 Sample(Grove grove, Random random, float spread)
    {
        float angle = Between(random, 0f, Mathf.PI * 2f);
        float radius = Mathf.Sqrt((float)random.NextDouble()) * spread;
        return grove.Center + Vector2.Scale(grove.Radius, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle))) * radius;
    }

    private static float DistanceSquared(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).sqrMagnitude;

    private static float Between(Random random, float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

    private static bool TryGround(MeshCollider ground, Vector2 point, float extent, out Vector3 position)
    {
        position = default;
        if (!Cast(ground, point, out var center))
            return false;
        for (int i = 0; i < 4; i++)
        {
            float angle = Mathf.PI * (.25f + .5f * i);
            var corner = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * extent;
            if (!Cast(ground, corner, out var hit) || Mathf.Abs(hit.point.y - center.point.y) > .85f)
                return false;
        }
        position = center.point;
        return true;
    }

    private static bool Cast(MeshCollider ground, Vector2 point, out RaycastHit hit) =>
        Physics.Raycast(new Vector3(point.x, 36f, point.y), Vector3.down, out hit, 45f, ~0,
            QueryTriggerInteraction.Ignore) && hit.collider == ground && hit.normal.y > .82f;

    private static Transform Child(Transform parent, string name)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static MeshRenderer AddRenderer(Transform parent, string name, EdgeMesh mesh, Material material, string asset)
    {
        var child = Child(parent, name);
        child.position = mesh.Origin;
        child.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.Save(asset);
        var renderer = child.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return renderer;
    }

    private static void EnsureAssetFolder()
    {
        string path = "Assets";
        foreach (string folder in AssetFolder.Split('/').Skip(1))
        {
            string next = $"{path}/{folder}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(path, folder);
            path = next;
        }
    }

    private static Material LoadMaterial(string name, string shaderName)
    {
        var shader = Shader.Find(shaderName);
        if (!shader)
            throw new InvalidOperationException($"Missing backdrop shader {shaderName}.");
        string materialPath = $"{AssetFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (!material)
        {
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, materialPath);
        }
        if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    private sealed class EdgeMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<int> indices = new List<int>();
        internal Vector3 Origin { get; }
        internal bool Empty => vertices.Count == 0;

        internal EdgeMesh(Vector3 origin) => Origin = origin;

        internal void Crown(Vector3 center, Vector3 size, Color color, Random random, int sides,
            bool distant, bool conifer = false)
        {
            int first = vertices.Count;
            int rings = distant ? 2 : 3;
            float turn = Between(random, 0, Mathf.PI * 2f);
            for (int ring = 0; ring < rings; ring++)
            {
                float y = ring == 0 ? -.70f : ring == rings - 1 ? .55f : -.14f;
                float radius = ring == 0 ? .70f : ring == rings - 1 ? .66f : 1f;
                if (conifer)
                    radius = ring == 0 ? .92f : ring == rings - 1 ? .25f : .69f;
                for (int side = 0; side < sides; side++)
                {
                    float angle = turn + side * Mathf.PI * 2f / sides;
                    float variation = Between(random, .84f, 1.16f) * radius;
                    var local = new Vector3(Mathf.Cos(angle) * variation, y + Between(random, -.09f, .09f),
                        Mathf.Sin(angle) * variation);
                    Vertex(center + Vector3.Scale(size, local),
                        color * (Mathf.Lerp(.69f, 1.07f, (y + 1) * .5f) * Between(random, .91f, 1.10f)));
                }
            }

            int bottom = Vertex(center + Vector3.down * size.y, color * .62f);
            int top = Vertex(center + Vector3.up * size.y * (conifer ? 1f : .86f), color * 1.12f);
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                Triangle(bottom, first + side, first + next);
                for (int ring = 0; ring < rings - 1; ring++)
                {
                    int offset = first + ring * sides;
                    Quad(offset + side, offset + sides + side, offset + sides + next, offset + next);
                }
                int last = first + (rings - 1) * sides;
                Triangle(top, last + next, last + side);
            }
        }

        internal void Roof(Vector3 anchor, Quaternion turn, float width, float depth, float height, float rise, float tone)
        {
            var a = new Vector3(-width / 2f, 0, -depth / 2f);
            var b = new Vector3(width / 2f, 0, -depth / 2f);
            var c = new Vector3(width / 2f, 0, depth / 2f);
            var d = new Vector3(-width / 2f, 0, depth / 2f);
            var up = Vector3.up * height;
            var eaveA = a + up;
            var eaveB = b + up;
            var eaveC = c + up;
            var eaveD = d + up;
            var ridgeA = new Vector3(0, height + rise, a.z);
            var ridgeD = new Vector3(0, height + rise, d.z);
            var wall = Color.Lerp(new Color(.43f, .32f, .24f), new Color(.43f, .41f, .36f), tone);
            var roof = new Color(.26f, .30f, .28f);

            void Face(Color color, Vector3 first, Vector3 second, Vector3 third, Vector3? fourth = null)
            {
                int i = Vertex(anchor + turn * first, color);
                int j = Vertex(anchor + turn * second, color);
                int k = Vertex(anchor + turn * third, color);
                if (fourth.HasValue)
                    Quad(i, j, k, Vertex(anchor + turn * fourth.Value, color));
                else
                    Triangle(i, j, k);
            }

            Face(wall, a, eaveA, eaveB, b);
            Face(wall * .88f, b, eaveB, eaveC, c);
            Face(wall, c, eaveC, eaveD, d);
            Face(wall * .91f, d, eaveD, eaveA, a);
            Face(wall, eaveA, ridgeA, eaveB);
            Face(wall, eaveC, ridgeD, eaveD);
            Face(roof, eaveA, eaveD, ridgeD, ridgeA);
            Face(roof * .86f, eaveB, ridgeA, ridgeD, eaveC);
        }

        internal void Contact(MeshCollider ground, Vector3 site, Vector3 direction, float length, float width,
            float opacity)
        {
            const int sides = 9;
            var axis = new Vector2(direction.x, direction.z);
            var cross = new Vector2(-axis.y, axis.x);
            var center = new Vector2(site.x, site.z) + axis * length * .29f;
            if (!Cast(ground, center, out var middle))
                return;

            var outline = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                var point = center + axis * (Mathf.Cos(angle) * length) + cross * (Mathf.Sin(angle) * width);
                if (!Cast(ground, point, out var hit))
                    return;
                outline[i] = hit.point + Vector3.up * .04f;
            }

            var tint = new Color(.24f, .18f, .105f, opacity);
            int hub = Vertex(middle.point + Vector3.up * .04f, tint);
            int first = vertices.Count;
            tint.a = 0;
            foreach (var point in outline)
                Vertex(point, tint);
            for (int i = 0; i < sides; i++)
                Triangle(hub, first + (i + 1) % sides, first + i);
        }

        private int Vertex(Vector3 point, Color color)
        {
            int index = vertices.Count;
            vertices.Add(point - Origin);
            colors.Add(color);
            return index;
        }

        private void Triangle(int a, int b, int c)
        {
            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }

        private void Quad(int a, int b, int c, int d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }

        internal Mesh Save(string name)
        {
            string path = $"{AssetFolder}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool exists = mesh != null;
            if (!exists)
                mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateNormals();
            if (exists)
                EditorUtility.SetDirty(mesh);
            else
                AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
