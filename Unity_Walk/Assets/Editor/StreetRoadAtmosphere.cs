using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class StreetRoadAtmosphere
{
    private const string Folder = "Assets/StreetHDRP/AtmosphereDetails";
    private const string RootName = "Разметка и деликатные следы улицы";
    private const string RoadMeshPath = "Assets/Art/StreetRepair/Surfaces/ReferenceRoad.asset";
    private const string RoadMaterialGuid = "121909ce7feba9c4799a6aa47553085b";
    private const float LineWidth = .12f;
    private const float DashLength = 2.7f;
    private const float DashPeriod = 8.5f;
    private const float SurfaceOffset = .006f;
    private const float CarriageHalfWidth = 3.5f;
    private static readonly float[] AcrossSamples = { -1f, 0f, 1f };

    [Serializable] private sealed class RoadProfile
    {
        public StreetBuilder.Point[] crossings, manholes;
    }

    [Serializable] public sealed class Result
    {
        public string scene;
        public int dashedLines, paintTriangles, roadSampleChecks, rejectedPaintCandidates;
        public int curbDecals, asphaltCrackDecals, foundationDecals, decalSurfaceChecks;
        public bool allPaintSamplesOnMainCarriageway, existingGeometryPreserved = true;
    }

    private sealed class Route
    {
        internal readonly Vector3[] Points;
        internal readonly float[] Distance;
        internal float Length => Distance[Distance.Length - 1];

        internal Route(StreetBuilder.Point[] points)
        {
            Points = points.Select(p => p.V).ToArray();
            Distance = new float[Points.Length];
            for (int i = 1; i < Points.Length; i++)
                Distance[i] = Distance[i - 1] + Vector3.Distance(Points[i - 1], Points[i]);
        }

        internal Vector3 At(float distance, out Vector3 right)
        {
            distance = Mathf.Clamp(distance, 0f, Length);
            int segment = 1;
            while (segment < Distance.Length - 1 && Distance[segment] < distance) segment++;
            Vector3 delta = Points[segment] - Points[segment - 1];
            right = new Vector3(delta.z, 0f, -delta.x).normalized;
            return Vector3.Lerp(Points[segment - 1], Points[segment],
                Mathf.InverseLerp(Distance[segment - 1], Distance[segment], distance));
        }

        internal float Nearest(Vector3 point, out float lateral)
        {
            float best = float.MaxValue, chain = 0f;
            lateral = 0f;
            for (int i = 1; i < Points.Length; i++)
            {
                Vector3 delta = Points[i] - Points[i - 1];
                float fraction = Mathf.Clamp01(Vector3.Dot(point - Points[i - 1], delta) / delta.sqrMagnitude);
                Vector3 nearest = Points[i - 1] + delta * fraction;
                float sqr = (point - nearest).sqrMagnitude;
                if (sqr >= best) continue;
                best = sqr;
                chain = Mathf.Lerp(Distance[i - 1], Distance[i], fraction);
                lateral = Vector3.Dot(point - nearest, new Vector3(delta.z, 0f, -delta.x).normalized);
            }
            return chain;
        }
    }

    private sealed class Surface
    {
        private readonly PhysicsScene physics;
        private readonly MeshCollider road;
        private readonly int firstTriangle, endTriangle;
        private readonly Route route;
        private readonly Result result;

        internal Surface(Scene scene, Route route, Result result)
        {
            this.route = route;
            this.result = result;
            physics = scene.GetPhysicsScene();
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(RoadMeshPath);
            if (!asset) throw new InvalidOperationException("The saved authored road mesh is missing.");
            road = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(c =>
                c.gameObject.scene == scene && c.enabled && c.sharedMesh == asset);
            var mainMaterial = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(RoadMaterialGuid));
            int slot = Array.IndexOf(road.GetComponent<MeshRenderer>().sharedMaterials, mainMaterial);
            if (slot < 0) throw new InvalidOperationException("The main carriageway material slot is missing.");
            for (int i = 0; i < slot; i++) firstTriangle += (int)asset.GetIndexCount(i) / 3;
            endTriangle = firstTriangle + (int)asset.GetIndexCount(slot) / 3;
        }

        internal bool Sample(Vector3 position, out Vector3 point, bool paint = false)
        {
            if (paint) result.roadSampleChecks++;
            else result.decalSurfaceChecks++;
            point = default;
            route.Nearest(position, out float lateral);
            // The exact road collider also contains parking aprons in another material slot.
            if (Mathf.Abs(lateral) > CarriageHalfWidth - .11f) return false;
            if (!physics.Raycast(position + Vector3.up * 24f, Vector3.down, out var hit, 50f, ~0,
                    QueryTriggerInteraction.Ignore) || hit.collider != road || hit.normal.y < .9f ||
                hit.triangleIndex < firstTriangle || hit.triangleIndex >= endTriangle)
                return false;
            point = hit.point;
            return true;
        }

        internal bool RoadBox(Vector3 center, Vector3 across, Vector3 along, float width, float length)
        {
            foreach (float x in AcrossSamples)
            foreach (float z in AcrossSamples)
                if (!Sample(center + across * (width * .5f * x) + along * (length * .5f * z), out _))
                    return false;
            return true;
        }
    }

    [MenuItem("Street/Atmosphere/Add authored-road markings and decals")]
    public static void ApplySavedStreet()
    {
        Apply(SceneManager.GetActiveScene());
    }

    public static Result Apply(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("A saved loaded Street scene is required.");
        var source = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        var profile = JsonUtility.FromJson<RoadProfile>(File.ReadAllText("Assets/StreetSource/road_profiles.json"));
        var route = new Route(source.roads.Single(road => road.main).points);
        var result = new Result { scene = scene.name };
        Physics.SyncTransforms();
        var surface = new Surface(scene, route, result);
        EnsureFolder("Assets/StreetHDRP", "AtmosphereDetails");

        var old = scene.GetRootGameObjects().SingleOrDefault(go => go.name == RootName);
        if (old) UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);

        var gaps = BuildGaps(source, profile, route);
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (float distance = 22f; distance < route.Length - 22f - DashLength; distance += DashPeriod)
        {
            if (gaps.Any(gap => Mathf.Abs(distance + DashLength * .5f - gap.center) < gap.radius + DashLength * .5f))
                continue;
            var piece = new Vector3[10];
            bool safe = true;
            for (int step = 0; step <= 4 && safe; step++)
            {
                Vector3 center = route.At(distance + DashLength * step / 4f, out var right);
                if (!surface.Sample(center, out _, true)) { safe = false; break; }
                for (int side = 0; side < 2; side++)
                {
                    Vector3 sample = center + right * ((side == 0 ? -1f : 1f) * LineWidth * .5f);
                    if (!surface.Sample(sample, out var point, true)) { safe = false; break; }
                    piece[step * 2 + side] = point + Vector3.up * SurfaceOffset;
                }
            }
            if (!safe) { result.rejectedPaintCandidates++; continue; }
            int first = vertices.Count;
            vertices.AddRange(piece);
            for (int step = 0; step < 4; step++)
            {
                AddTriangleUp(triangles, vertices, first + step * 2, first + step * 2 + 1, first + step * 2 + 2);
                AddTriangleUp(triangles, vertices, first + step * 2 + 1, first + step * 2 + 3, first + step * 2 + 2);
            }
            result.dashedLines++;
        }
        if (result.dashedLines < 12) throw new InvalidOperationException($"Only {result.dashedLines} safe authored-road dashes fit.");
        result.paintTriangles = triangles.Count / 3;
        var mesh = new Mesh { name = "Verified main-carriageway white dashes" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Save(mesh, Folder + "/Main carriageway dashes.asset");
        var markings = new GameObject("Белая прерывистая осевая · только автомобильная дорога");
        markings.transform.SetParent(root.transform, false);
        markings.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/Main carriageway dashes.asset");
        var renderer = markings.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = PaintMaterial();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
        renderer.rayTracingMode = UnityEngine.Experimental.Rendering.RayTracingMode.Off;

        Material dust = DecalMaterial("Curb dust", Texture("Curb dust", 0), .27f);
        Material cracks = DecalMaterial("Fine asphalt wear", Texture("Fine asphalt wear", 1), .28f);
        Material streak = DecalMaterial("Foundation weathering", Texture("Foundation weathering", 2), .22f);
        for (float distance = 25f; distance < route.Length - 25f; distance += 22f)
        {
            if (gaps.Any(gap => Mathf.Abs(distance - gap.center) < gap.radius + 2f)) continue;
            Vector3 p = route.At(distance, out var right);
            Vector3 forward = new(-right.z, 0f, right.x);
            foreach (int side in new[] { -1, 1 })
            {
                Vector3 center = p + right * (side * 3.17f);
                if (!surface.RoadBox(center, right, forward, .26f, 2.2f) ||
                    !surface.Sample(center, out var point)) continue;
                MakeProjector(root.transform, "Пыль у кромки дороги", dust, point + Vector3.up * .025f,
                    Quaternion.LookRotation(Vector3.down, forward), new Vector3(.26f, 2.2f, .09f), .63f);
                result.curbDecals++;
            }
        }
        for (float distance = 34f; distance < route.Length - 34f; distance += 47f)
        {
            if (gaps.Any(gap => Mathf.Abs(distance - gap.center) < gap.radius + 2f)) continue;
            Vector3 p = route.At(distance, out var right);
            Vector3 forward = new(-right.z, 0f, right.x);
            Vector3 center = p + right * (Mathf.Sin(distance * .117f) * 1.8f);
            if (!surface.RoadBox(center, right, forward, .85f, 1.6f) ||
                !surface.Sample(center, out var point)) continue;
            MakeProjector(root.transform, "Небольшая трещина асфальта", cracks, point + Vector3.up * .025f,
                Quaternion.LookRotation(Vector3.down, forward), new Vector3(.85f, 1.6f, .09f), .62f);
            result.asphaltCrackDecals++;
        }
        AddFoundationWeathering(scene, route, root.transform, streak, result);
        result.allPaintSamplesOnMainCarriageway = result.dashedLines > 0;
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("QA_HDRP");
        File.WriteAllText($"QA_HDRP/road-atmosphere-{scene.name}.json", JsonUtility.ToJson(result, true));
        Debug.Log("STREET_ROAD_ATMOSPHERE " + JsonUtility.ToJson(result));
        return result;
    }

    private static List<(float center, float radius)> BuildGaps(StreetBuilder.Data source, RoadProfile profile, Route route)
    {
        var gaps = new List<(float, float)>();
        foreach (var crossing in profile.crossings ?? Array.Empty<StreetBuilder.Point>())
            gaps.Add((route.Nearest(crossing.V, out _), 4f));
        foreach (var manhole in profile.manholes ?? Array.Empty<StreetBuilder.Point>())
        {
            float position = route.Nearest(manhole.V, out float across);
            if (Mathf.Abs(across) < CarriageHalfWidth)
                gaps.Add((position, .8f));
        }
        foreach (var road in source.roads.Where(road => !road.main && road.kind != "footway" &&
                     road.kind != "path" && road.kind != "pedestrian"))
        {
            foreach (var point in road.points)
            {
                float position = route.Nearest(point.V, out float across);
                if (Mathf.Abs(across) > CarriageHalfWidth + .8f) continue;
                gaps.Add((position, Mathf.Clamp(road.width * .5f + 2.2f, 4f, 9f)));
            }
        }
        return gaps;
    }

    private static void AddFoundationWeathering(Scene scene, Route route, Transform root, Material material, Result result)
    {
        var buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>()
            .Where(building => building.gameObject.scene == scene).OrderBy(building => building.osmId).ToArray();
        var physics = scene.GetPhysicsScene();
        foreach (var building in buildings)
        {
            if (result.foundationDecals >= 14) break;
            if (building.bodyColliders == null || building.bodyColliders.Length == 0) continue;
            var bounds = building.bodyColliders.Where(c => c && c.enabled)
                .Select(c => c.bounds).FirstOrDefault();
            if (bounds.size.y < 2f) continue;
            float chain = route.Nearest(bounds.center, out _);
            Vector3 roadward = route.At(chain, out _) - bounds.center;
            roadward.y = 0f;
            if (roadward.sqrMagnitude < 1f) continue;
            roadward.Normalize();
            Vector3 target = new(bounds.center.x, bounds.min.y + .32f, bounds.center.z);
            float radius = Mathf.Max(bounds.size.x, bounds.size.z) + 3f;
            if (!physics.Raycast(target + roadward * radius, -roadward, out var hit, radius + 2f, ~0,
                    QueryTriggerInteraction.Ignore) || !building.bodyColliders.Contains(hit.collider) ||
                Mathf.Abs(hit.normal.y) > .15f)
                continue;
            MakeProjector(root, "Лёгкий след у цоколя " + building.address, material,
                hit.point + hit.normal * .018f, Quaternion.LookRotation(-hit.normal, Vector3.up),
                new Vector3(1.3f, .48f, .07f), .60f);
            result.foundationDecals++;
        }
    }

    private static void MakeProjector(Transform root, string name, Material material, Vector3 position,
        Quaternion rotation, Vector3 size, float opacity)
    {
        var node = new GameObject(name);
        node.transform.SetParent(root, false);
        node.transform.SetPositionAndRotation(position, rotation);
        var projector = node.AddComponent<DecalProjector>();
        projector.material = material;
        projector.pivot = Vector3.zero;
        projector.size = size;
        projector.drawDistance = 65f;
        projector.fadeScale = .75f;
        projector.fadeFactor = opacity;
        projector.startAngleFade = 40f;
        projector.endAngleFade = 75f;
    }

    private static Material PaintMaterial()
    {
        const string path = Folder + "/Warm white road paint.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        var shader = Shader.Find("HDRP/Lit");
        if (!shader) throw new InvalidOperationException("HDRP Lit is missing.");
        material = new Material(shader) { name = "Warm white road paint" };
        material.SetColor("_BaseColor", new Color(.74f, .75f, .70f, 1));
        material.SetFloat("_Smoothness", .20f);
        material.SetFloat("_Metallic", 0f);
        HDMaterial.ValidateMaterial(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material DecalMaterial(string name, Texture2D texture, float blend)
    {
        string path = $"{Folder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            var shader = Shader.Find("HDRP/Decal");
            if (!shader) throw new InvalidOperationException("Native HDRP Decal shader is missing.");
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseColorMap", texture);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_DecalBlend", blend);
        material.SetFloat("_AffectAlbedo", 1f);
        material.SetFloat("_AffectNormal", 0f);
        material.SetFloat("_AffectMetal", 0f);
        material.SetFloat("_AffectAO", 0f);
        material.SetFloat("_AffectSmoothness", 1f);
        material.SetFloat("_AffectEmission", 0f);
        material.SetFloat("_Smoothness", .12f);
        HDMaterial.ValidateMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D Texture(string name, int kind)
    {
        string path = $"{Folder}/{name}.png";
        if (!File.Exists(path))
        {
            const int resolution = 256;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, false);
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float u = (x + .5f) / resolution, v = (y + .5f) / resolution;
                float edge = Mathf.SmoothStep(0f, .14f, Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)));
                float noise = Mathf.PerlinNoise(u * 17f + 31.7f, v * 12f + 12.3f);
                float alpha;
                if (kind == 1)
                {
                    float center = .5f + .16f * Mathf.Sin(v * 13f) + .035f * Mathf.Sin(v * 49f);
                    float line = 1f - Mathf.SmoothStep(.003f, .012f, Mathf.Abs(u - center));
                    float branch = v > .35f && v < .65f ?
                        1f - Mathf.SmoothStep(.002f, .006f, Mathf.Abs(u - (.5f + (v - .5f) * 1.6f))) : 0f;
                    alpha = Mathf.Max(line, branch * .55f) * edge * Mathf.Lerp(.32f, .7f, noise);
                }
                else if (kind == 2)
                {
                    float strand = Mathf.Pow(Mathf.PerlinNoise(u * 23f, v * 1.8f + 19f), 2f);
                    alpha = strand * edge * Mathf.Pow(1f - v, .7f) * .56f;
                }
                else
                    alpha = Mathf.Pow(noise, 1.4f) * edge * .55f;
                pixels[y * resolution + x] = new Color(.19f, .165f, .125f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = resolution;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void AddTriangleUp(List<int> indices, List<Vector3> vertices, int a, int b, int c)
    {
        bool up = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).y > 0f;
        indices.Add(a);
        indices.Add(up ? b : c);
        indices.Add(up ? c : b);
    }

    private static void Save(Mesh mesh, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (old)
        {
            EditorUtility.CopySerialized(mesh, old);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(old);
        }
        else AssetDatabase.CreateAsset(mesh, path);
    }

    private static void EnsureFolder(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
            AssetDatabase.CreateFolder(parent, name);
    }
}
