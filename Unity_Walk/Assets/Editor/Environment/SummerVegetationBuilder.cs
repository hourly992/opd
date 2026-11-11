using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Reuses the licensed natural tree topology. Conifers/shrubs are branched meshes with alpha-tested foliage,
// never solid canopy primitives. This builder defines shapes only; planting belongs to geographic data.
internal static class SummerVegetationBuilder
{
    private static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
    internal static void Initialize() { cache.Clear(); OptimizedTreeBuilder.Initialize(); }
    internal static GameObject Broadleaf(string asset, float height, int seed)
    {
        if (!cache.TryGetValue(asset, out var prefab)) cache[asset] = prefab = OptimizedTreeBuilder.Create(asset, height, seed);
        return prefab;
    }

    internal static GameObject SelectBroadleaf(float height, int variant)
    {
        variant = PositiveVariant(variant);
        return Broadleaf("BroadleafV" + variant + "H" + Mathf.RoundToInt(height * 100), height, 104 + variant);
    }

    internal static GameObject Spruce() => Spruce(0);
    internal static GameObject Spruce(int variant)
    {
        variant = PositiveVariant(variant);
        string asset = variant == 0 ? "Spruce" : "SpruceV" + variant;
        if (cache.TryGetValue(asset, out var existing)) return existing;
        Material bark = StreetSurfaceLibrary.Bark();
        Material needles = StreetSurfaceLibrary.Foliage("SpruceNeedles", "fir_tree_01", new Color(.91f, 1, .78f));
        var root = new GameObject("Хвойное дерево · разновозрастные ветви · вариант " + variant);
        var renderers = new Renderer[3];
        float height = 10.5f + variant * .38f;
        int seed = 884 + variant * 163;
        Vector3 Trunk(float t) => new Vector3(Mathf.Sin(t * 2.4f + variant) * t * t * .18f,
            t * height, Mathf.Sin(t * 1.7f + variant * .8f) * t * t * .16f);
        for (int lod = 0; lod < 3; lod++)
        {
            var mesh = new EnvironmentMesh();
            var random = new System.Random(seed);
            var axis = new Vector3[15]; var radii = new float[15];
            for (int i = 0; i < axis.Length; i++)
            {
                float t = i / (float)(axis.Length - 1);
                axis[i] = Trunk(t);
                radii[i] = .20f * Mathf.Pow(1 - t, 1.14f) + .006f;
                if (i == 0) radii[i] *= 1.38f;
            }
            Limb(mesh, bark, axis, radii, lod == 0 ? 12 : lod == 1 ? 8 : 6, seed);
            // Irregular whorls and rising terminal shoots retain air between branches.
            // Every LOD consumes the same random sequence; retained sprigs stay in the same place.
            for (int level = 0; level < 21; level++)
            {
                float t = level / 21f;
                float y = 1.35f + t * (height - 1.45f) + Range(random, -.09f, .09f);
                float radius = (2.1f + variant * .08f) * Mathf.Pow(1 - t, .84f);
                int branches = level > 15 ? 5 : 7;
                for (int branch = 0; branch < branches; branch++)
                {
                    float a = branch * Mathf.PI * 2 / branches + level * 2.399963f + Range(random, -.16f, .16f);
                    Vector3 direction = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 sideways = Vector3.Cross(Vector3.up, direction);
                    float reach = radius * Range(random, .78f, 1.13f);
                    Vector3 start = Trunk(y / height);
                    Vector3 joint = start + direction * reach * .62f - Vector3.up * reach * .16f;
                    Vector3 end = start + direction * reach - Vector3.up * reach * Range(random, .06f, .22f);
                    Vector3 tip = end + direction * reach * .07f + Vector3.up * (.12f + t * .12f);
                    float width = .034f * (1 - t * .80f);
                    Limb(mesh, bark, new[] { start, joint, end, tip }, new[] { width, width * .60f, .005f, .0015f }, lod == 0 ? 6 : 4, seed + branch + level);
                    for (int twig = 0; twig < 18; twig++)
                    {
                        float along = Range(random, .16f, 1.03f);
                        float side = twig % 2 == 0 ? -1 : 1;
                        Vector3 stem = Vector3.Lerp(joint, end, Mathf.Clamp01(along));
                        Vector3 p = Vector3.Lerp(start, end, along) + sideways * side * reach * (.06f + (1 - along) * .17f)
                            + Vector3.up * Range(random, -.10f, .13f);
                        float size = (.43f + reach * .20f) * Range(random, .84f, 1.12f);
                        Quaternion rotation = Quaternion.Euler(Range(random, -20, 58), a * Mathf.Rad2Deg + side * 44 + Range(random, -23, 23), Range(random, -30, 30));
                        float shade = Range(random, .80f, 1.03f);
                        if (lod == 0 && twig % 3 == 0)
                            Limb(mesh, bark, new[] { stem, (stem + p) * .5f + Vector3.up * .02f, p }, new[] { .005f, .003f, .001f }, 4, seed + twig);
                        int stride = lod == 0 ? 1 : lod == 1 ? 2 : 4;
                        if (twig % stride != 0) continue;
                        float compensation = lod == 0 ? 1 : lod == 1 ? 1.23f : 1.57f;
                        Vector3 normal = (direction * .4f + Vector3.up).normalized;
                        mesh.FoliageCard(needles, p, rotation, new Vector2(size, size) * compensation, normal,
                            new Color(shade * .94f, shade, shade * .92f, .86f));
                    }
                }
            }
            var part = mesh.Create(asset + "_LOD" + lod, "LOD " + lod);
            part.transform.SetParent(root.transform, false);
            renderers[lod] = part.GetComponent<Renderer>();
            if (lod == 2) renderers[lod].shadowCastingMode = ShadowCastingMode.Off;
        }
        AddLODs(root, renderers);
        var trunk = root.AddComponent<CapsuleCollider>();
        trunk.center = Vector3.up * 2; trunk.height = 4; trunk.radius = .21f;
        return cache[asset] = EnvironmentMesh.SavePrefab(root, asset);
    }

    internal static GameObject Shrub(bool hedge = false) => Shrub(hedge, 0);
    internal static GameObject Shrub(bool hedge, int variant)
    {
        variant = PositiveVariant(variant);
        string asset = (hedge ? "Hedge2m" : "Shrub") + (variant == 0 ? "" : "V" + variant);
        if (cache.TryGetValue(asset, out var existing)) return existing;
        Material leaf = StreetSurfaceLibrary.Foliage("HedgeLeaves", "tree_small_02", new Color(.81f, 1, .67f));
        leaf.SetFloat("_WindStrength", .035f);
        leaf.SetFloat("_WindStemHeight", .05f);
        leaf.SetFloat("_WindHeightScale", 1);
        Material bark = StreetSurfaceLibrary.Bark();
        var root = new GameObject(hedge ? "Живая изгородь · ветви и листва" : "Кустарник · разветвлённые побеги · вариант " + variant);
        var renderers = new Renderer[3];
        int seed = (hedge ? 1724 : 918) + variant * 109;
        for (int lod = 0; lod < 3; lod++)
        {
            var mesh = new EnvironmentMesh();
            var random = new System.Random(seed);
            var ends = new Vector3[hedge ? 14 : 11];
            for (int branch = 0; branch < ends.Length; branch++)
            {
                float a = branch * 2.399963f + variant;
                Vector3 start = hedge ? new Vector3(Range(random, -.88f, .88f), 0, Range(random, -.24f, .24f)) : Vector3.zero;
                Vector3 end = hedge ? new Vector3(start.x + Range(random, -.13f, .13f), Range(random, .67f, 1.02f), Range(random, -.32f, .32f))
                    : new Vector3(Mathf.Cos(a) * Range(random, .35f, .67f), Range(random, .66f, 1.13f), Mathf.Sin(a) * Range(random, .30f, .60f));
                ends[branch] = end;
                Vector3 joint = Vector3.Lerp(start, end, .53f) + Vector3.up * .14f;
                Limb(mesh, bark, new[] { start, joint, end }, new[] { .016f, .009f, .002f }, lod == 0 ? 6 : 4, seed + branch);
                if (lod == 0)
                    for (int twig = 0; twig < 3; twig++)
                    {
                        Vector3 tip = end + new Vector3(Mathf.Sin(a + twig) * .21f, -.08f + twig * .07f, Mathf.Cos(a + twig) * .19f);
                        Limb(mesh, bark, new[] { joint, Vector3.Lerp(joint, tip, .63f), tip }, new[] { .007f, .003f, .001f }, 4, seed + twig);
                    }
            }
            int count = hedge ? 1800 : 1900;
            for (int i = 0; i < count; i++)
            {
                // Clusters grow around visible shoots, rather than filling one perfectly smooth ellipsoid.
                Vector3 center = ends[i % ends.Length] * .85f;
                Vector3 p = center + Vector3.Scale(Sphere(random), hedge ? new Vector3(.24f, .25f, .23f) : new Vector3(.35f, .32f, .31f));
                p.y = Mathf.Max(.09f, p.y);
                Vector3 normal = (p - new Vector3(0, .40f, 0)).normalized;
                normal = (normal + Vector3.up * .38f).normalized;
                Quaternion rotation = Rotation(random);
                float size = Range(random, .11f, .18f), shade = Range(random, .82f, 1.05f);
                int stride = lod == 0 ? 1 : lod == 1 ? 3 : 8;
                if (i % stride != 0) continue;
                float compensation = lod == 0 ? 1 : lod == 1 ? 1.42f : 2.05f;
                mesh.FoliageCard(leaf, p, rotation, new Vector2(size, size * .72f) * compensation, normal,
                    new Color(shade * .92f, shade, shade * .80f, .86f));
            }
            var part = mesh.Create(asset + "_LOD" + lod, "LOD " + lod);
            part.transform.SetParent(root.transform, false);
            renderers[lod] = part.GetComponent<Renderer>();
            if (lod == 2) renderers[lod].shadowCastingMode = ShadowCastingMode.Off;
        }
        AddLODs(root, renderers, true);
        if (hedge) EnvironmentMesh.Collider(root, new Vector3(0, .48f, 0), new Vector3(1.95f, .9f, .78f));
        return cache[asset] = EnvironmentMesh.SavePrefab(root, asset);
    }

    // A continuous tapered tube with irregular rings and smooth radial normals. Multiple curved
    // segments form one branch/trunk; no Unity cylinder/sphere primitive is used as a final object.
    private static void Limb(EnvironmentMesh mesh, Material material, Vector3[] axis, float[] radii, int sides, int seed)
    {
        var rings = new Vector3[axis.Length, sides];
        var normals = new Vector3[axis.Length, sides];
        for (int ring = 0; ring < axis.Length; ring++)
        {
            Vector3 tangent = (axis[Mathf.Min(ring + 1, axis.Length - 1)] - axis[Mathf.Max(0, ring - 1)]).normalized;
            Vector3 across = Vector3.Cross(tangent, Mathf.Abs(tangent.y) > .94f ? Vector3.forward : Vector3.up).normalized;
            Vector3 other = Vector3.Cross(tangent, across).normalized;
            for (int side = 0; side < sides; side++)
            {
                float a = side * Mathf.PI * 2 / sides;
                Vector3 radial = across * Mathf.Cos(a) + other * Mathf.Sin(a);
                float uneven = 1 + .075f * Mathf.Sin(a * 3 + seed * .73f + ring * .18f);
                rings[ring, side] = axis[ring] + radial * radii[ring] * uneven;
                normals[ring, side] = radial;
            }
        }
        for (int ring = 0; ring < axis.Length - 1; ring++)
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                mesh.Surface(material, new[] { rings[ring, side], rings[ring + 1, side], rings[ring + 1, next], rings[ring, next] },
                    new[] { normals[ring, side], normals[ring + 1, side], normals[ring + 1, next], normals[ring, next] });
            }
        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            mesh.Face(material, (axis[0] - axis[1]).normalized, axis[0], rings[0, next], rings[0, side]);
            int last = axis.Length - 1;
            mesh.Face(material, (axis[last] - axis[last - 1]).normalized, axis[last], rings[last, side], rings[last, next]);
        }
    }

    private static void AddLODs(GameObject root, Renderer[] renderers, bool shrub = false)
    {
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(shrub ? .12f : .16f, new[] { renderers[0] }),
            new LOD(shrub ? .035f : .045f, new[] { renderers[1] }), new LOD(shrub ? .012f : .009f, new[] { renderers[2] }) });
        group.RecalculateBounds();
    }

    private static int PositiveVariant(int value) => ((value % 4) + 4) % 4;
    private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    private static Quaternion Rotation(System.Random random) => Quaternion.Euler(Range(random, -85, 85), Range(random, 0, 360), Range(random, -90, 90));
    private static Vector3 Sphere(System.Random random)
    {
        Vector3 p;
        do { p = new Vector3(Range(random, -1, 1), Range(random, -1, 1), Range(random, -1, 1)); } while (p.sqrMagnitude > 1 || p.sqrMagnitude < .03f);
        return p;
    }
}