using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Finishes existing asphalt edges only inside reviewed photo footprints.</summary>
public static class StreetPhotoSiteBorders
{
    public const string RootName = "Фотограницы · бордюры и края газонов";
    // First material slot of the saved ReferenceKerbsPaintedEdges renderer.
    private const string KerbPath = "Assets/Art/PaintedDistrict/Materials/5bfca59280e3c9044a6683ba01b05ab1_2100000_RoadKerb_0.mat";
    private const string LawnPath = "Assets/Art/AutumnGrass/Lawn ground.mat";
    private const string BladesPath = "Assets/Art/AutumnGrass/DenseLawn/Lawn HDRP Instanced.mat";
    private const string LeavesPath = "Assets/PhotoReconstruction/Stage08Autumn/Materials/FallenLeaves.mat";
    private const float Width = .16f, Bevel = .012f, Rise = .145f, Drop = .01f;
    private const float SoilWidth = 1.25f, SpanLength = .32f, BlendLength = 1.6f;
    private const float EdgeInset = .002f;

    [Serializable]
    public sealed class SurfaceProbe
    {
        public string name, colliderNameContains;
        public Vector3 point;
        public float tolerance = .013f;
    }

    [Serializable]
    public sealed class JoinProbe
    {
        public string name;
        public Vector3 from, to;
        public float tolerance = .025f, maxExcessStep = .025f;
    }

    [Serializable]
    public sealed class Report
    {
        public int candidateEdges, admittedSpans, lawnSpans, raisedLawnSpans, droppedEnds, kerbChecks, tuftClusters, leaves;
        public int terrainCopies, detailUnitsCleared;
        public float kerbLength, lawnLength, maxKerbProbeError, roadKerbWidth = Width, fullRise = Rise;
        public bool roadsAndPavingUnchanged, insidePhotoScope, passed;
        public string[] sources;
        public List<SurfaceProbe> surfaces = new();
        public List<JoinProbe> joins = new();
    }

    private enum Surface { Unknown, Soil, Shoulder, Paving, Kerb, Asphalt }

    private sealed class SurfaceData
    {
        internal readonly Surface[] Kinds;
        internal readonly int[] EndOfSlot;
        internal SurfaceData(MeshCollider collider)
        {
            var mesh = collider.sharedMesh;
            var renderer = collider.GetComponent<Renderer>();
            var materials = renderer ? renderer.sharedMaterials : Array.Empty<Material>();
            var filter = collider.GetComponent<MeshFilter>();
            Kinds = new Surface[mesh.subMeshCount];
            EndOfSlot = new int[mesh.subMeshCount];
            int triangles = 0;
            for (int i = 0; i < Kinds.Length; i++)
            {
                triangles += (int)mesh.GetIndexCount(i) / 3;
                EndOfSlot[i] = triangles;
                Kinds[i] = Classify(i < materials.Length && filter && filter.sharedMesh && filter.sharedMesh.subMeshCount == mesh.subMeshCount
                    ? materials[i] : null, mesh.name);
            }
        }

        internal Surface At(int triangle)
        {
            if (triangle < 0) return Surface.Unknown;
            for (int i = 0; i < EndOfSlot.Length; i++)
                if (triangle < EndOfSlot[i]) return Kinds[i];
            return Surface.Unknown;
        }
    }

    private sealed class Sampler
    {
        private readonly PhysicsScene scene;
        private readonly Dictionary<MeshCollider, SurfaceData> known = new();
        internal Sampler(Scene active) => scene = active.GetPhysicsScene();

        internal Surface Sample(Vector3 position, out float y)
        {
            y = float.NaN;
            if (!scene.Raycast(new Vector3(position.x, 3.2f, position.z), Vector3.down, out var hit, 4.6f,
                    ~0, QueryTriggerInteraction.Ignore) || hit.normal.y < .62f)
                return Surface.Unknown;
            y = hit.point.y;
            if (hit.collider is not MeshCollider mesh || !mesh.sharedMesh) return Surface.Unknown;
            if (mesh.name == "Grass" && mesh.bounds.size.x > 1000f) return Surface.Soil;
            if (!known.TryGetValue(mesh, out var info))
                known.Add(mesh, info = new SurfaceData(mesh));
            return info.At(hit.triangleIndex);
        }

        internal bool Ground(Vector3 point, out float y)
        {
            Surface kind = Sample(point, out y);
            return kind == Surface.Soil || kind == Surface.Shoulder;
        }
    }

    private sealed class Edge
    {
        internal Vector3 A, B, Outward;
        internal int Net;
        internal string Source;
    }

    private sealed class Node
    {
        internal Vector3 Position, Outward, Miter;
        internal readonly List<Span> Links = new(2);
        internal float FreeEndDistance = float.PositiveInfinity, ExistingTop = float.NaN;
        internal Vector3[] Profile;
    }

    private sealed class Span
    {
        internal Node A, B;
        internal Vector3 Outward;
        internal float Length;
        internal string Source;
    }

    /// <param name="photoScope">Verified world-space X/Z polygons, not camera frusta.</param>
    /// <param name="roadAssetNames">Exact saved mesh names admitted inside the photo scope.</param>
    public static Report Apply(Scene scene, Transform parent, Vector2[][] photoScope, string outputFolder,
        string[] roadAssetNames)
    {
        if (scene.path != "Assets/Scenes/Street.unity" || !scene.isLoaded)
            throw new InvalidOperationException("Open the saved Street scene first.");
        if (photoScope == null || photoScope.Length == 0 || photoScope.Any(p => p == null || p.Length < 3) ||
            roadAssetNames == null || roadAssetNames.Length == 0 || roadAssetNames.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Explicit photo boundaries and resolved asphalt sources are required.");
        if (UnityEngine.Object.FindObjectsByType<Transform>().Any(t => t.name == RootName && t.gameObject.scene == scene))
            throw new InvalidOperationException("Photo-site borders have already been created.");
        if (photoScope.SelectMany(p => p).Any(p => !float.IsFinite(p.x) || !float.IsFinite(p.y)))
            throw new ArgumentException("Invalid photograph boundary.");

        Physics.SyncTransforms();
        var report = new Report();
        var sampler = new Sampler(scene);
        var allowedSources = new HashSet<string>(roadAssetNames, StringComparer.Ordinal);
        var candidates = FindEdges(scene, Bounds(photoScope), allowedSources);
        report.candidateEdges = candidates.Count;
        var nodes = new Dictionary<Vector2Int, Node>();
        var spans = new List<Span>();
        foreach (Edge edge in candidates)
        {
            float length = FlatLength(edge.A, edge.B);
            int subdivisions = Mathf.Max(1, Mathf.CeilToInt(length / SpanLength));
            for (int i = 0; i < subdivisions; i++)
            {
                Vector3 a = Vector3.Lerp(edge.A, edge.B, i / (float)subdivisions);
                Vector3 b = Vector3.Lerp(edge.A, edge.B, (i + 1f) / subdivisions);
                float pieceLength = FlatLength(a, b);
                if (pieceLength < .012f || !Admit(a, b, edge.Outward, photoScope, sampler)) continue;
                var span = new Span { A = GetNode(a), B = GetNode(b), Outward = edge.Outward,
                    Length = pieceLength, Source = edge.Source };
                spans.Add(span);
                span.A.Links.Add(span);
                span.B.Links.Add(span);
            }
        }
        if (spans.Count == 0)
            throw new InvalidOperationException("No asphalt-to-ground boundary was physically verified inside the photo scope.");

        Node GetNode(Vector3 point)
        {
            var key = Key(point);
            if (nodes.TryGetValue(key, out Node node)) return node;
            node = new Node { Position = point };
            nodes.Add(key, node);
            return node;
        }

        // A welded section profile is reused on both sides of each corner.
        // Ambiguous branches and hairpins are trimmed so they cannot overlap a lane.
        foreach (Node node in nodes.Values)
        {
            var n = node.Links.SumVector(link => link.Outward).normalized;
            float cosine = node.Links.Min(link => Vector3.Dot(n, link.Outward));
            node.Outward = n;
            node.Miter = node.Links.Count <= 2 && cosine >= .58f ? n / cosine : Vector3.zero;
        }
        spans.RemoveAll(span =>
        {
            bool Invalid(Node node) => node.Miter == Vector3.zero ||
                !Inside(node.Position + node.Miter * (Width + EdgeInset), photoScope);
            if (!Invalid(span.A) && !Invalid(span.B) &&
                SafeMiter(span.A, span.B, photoScope, sampler)) return false;
            span.A.Links.Remove(span);
            span.B.Links.Remove(span);
            return true;
        });
        RemoveOverlappingSpans(spans);
        if (spans.Count == 0)
            throw new InvalidOperationException("All candidate junctions intersect existing geometry or leave the photo scope.");

        foreach (Node node in nodes.Values.Where(n => n.Links.Count > 0))
            if (node.Links.Count == 1)
            {
                var link = node.Links[0];
                var next = link.A == node ? link.B : link.A;
                Vector3 before = node.Position + (node.Position - next.Position).normalized * .023f + node.Miter * Width * .5f;
                if (sampler.Sample(before, out float level) == Surface.Kerb &&
                    level > node.Position.y && level - node.Position.y <= .19f)
                    node.ExistingTop = level;
                else
                {
                    node.FreeEndDistance = 0f;
                    report.droppedEnds++;
                }
            }
        // Propagate lowering along actual accepted spans; doorways and driveway
        // mouths stay open and get a smooth 1.6 m run-out from the regular profile.
        for (int i = 0; i < 24; i++)
        {
            bool changed = false;
            foreach (Span span in spans)
            {
                if (span.A.FreeEndDistance + span.Length < span.B.FreeEndDistance &&
                    span.A.FreeEndDistance + span.Length < BlendLength)
                {
                    span.B.FreeEndDistance = span.A.FreeEndDistance + span.Length;
                    changed = true;
                }
                if (span.B.FreeEndDistance + span.Length < span.A.FreeEndDistance &&
                    span.B.FreeEndDistance + span.Length < BlendLength)
                {
                    span.A.FreeEndDistance = span.B.FreeEndDistance + span.Length;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        var kerb = Load(KerbPath);
        var lawn = Load(LawnPath);
        var blades = Load(BladesPath);
        var leaves = Load(LeavesPath);
        var curbMesh = new EnvironmentMesh();
        var soilMesh = new EnvironmentMesh();
        var scatterMesh = new EnvironmentMesh();
        var probes = new List<Vector3>();
        var kerbTriangles = new List<Vector2[]>();
        var soilProfiles = new Dictionary<Node, Vector3[]>();
        foreach (Node node in nodes.Values.Where(n => n.Links.Count > 0))
        {
            node.Profile = Section(node, sampler);
            if (TrySoil(node, photoScope, sampler, out var profile))
                soilProfiles.Add(node, profile);
        }
        var lawnSpans = new HashSet<Span>(spans.Where(s =>
            soilProfiles.TryGetValue(s.A, out var a) && soilProfiles.TryGetValue(s.B, out var b) &&
            ClearSoilSpan(a, b, photoScope, sampler)));
        TaperLawnEnds(soilProfiles, lawnSpans, sampler);

        var decorations = new HashSet<Vector2Int>();
        foreach (Span span in spans)
        {
            Vector3[] a = span.A.Profile, b = span.B.Profile;
            AddKerb(curbMesh, kerb, span, a, b);
            kerbTriangles.Add(new[] { XZ(a[0]), XZ(b[0]), XZ(b[5]) });
            kerbTriangles.Add(new[] { XZ(a[0]), XZ(b[5]), XZ(a[5]) });
            // Use the centroid of an actual triangle in the bevel top. A
            // quadrilateral at a mitred transition need not be planar.
            probes.Add((a[2] + b[2] + b[3]) / 3f);
            if (report.admittedSpans % 22 == 0)
                report.surfaces.Add(new SurfaceProbe { name = "Existing-profile concrete curb",
                    colliderNameContains = "Бордюр · светлый бетон", point = probes[^1] });
            LoweredJoin(span, sampler, report);
            report.admittedSpans++;
            report.kerbLength += span.Length;
            if (lawnSpans.Contains(span))
            {
                Vector3[] soilA = soilProfiles[span.A];
                Vector3[] soilB = soilProfiles[span.B];
                for (int k = 0; k + 1 < soilA.Length; k++)
                    Up(soilMesh, lawn, soilA[k], soilB[k], soilB[k + 1], soilA[k + 1]);
                report.lawnSpans++;
                report.lawnLength += span.Length;
                if (soilA[1].y >= span.A.Position.y + .030f && soilB[1].y >= span.B.Position.y + .030f)
                    report.raisedLawnSpans++;
                var p = Vector3.Lerp((soilA[1] + soilB[1]) * .5f, (soilA[2] + soilB[2]) * .5f, .39f);
                bool raisedAtProbe = sampler.Ground(p, out float originalGround) && p.y > originalGround + .012f;
                if (report.lawnSpans % 21 == 0 && raisedAtProbe)
                    report.surfaces.Add(new SurfaceProbe { name = "Grass bank behind concrete curb",
                        colliderNameContains = "Плавные кромки газона", point = p });
                if (raisedAtProbe) Scatter(scatterMesh, p, photoScope, sampler, blades, leaves, decorations, report);
            }
        }

        Directory.CreateDirectory(outputFolder + "/Meshes");
        AssetDatabase.Refresh();
        var root = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        if (parent) root.transform.SetParent(parent, true);
        var curb = AddBatch(curbMesh, outputFolder, "VerifiedCurb", "Бордюр · светлый бетон", root.transform, true);
        if (report.lawnSpans > 0) AddBatch(soilMesh, outputFolder, "VerifiedLawnEdges", "Плавные кромки газона", root.transform, true);
        if (report.tuftClusters + report.leaves > 0)
            AddBatch(scatterMesh, outputFolder, "VerifiedAutumnEdgeDetails", "Редкая трава и осенние листья", root.transform, false);
        Physics.SyncTransforms();
        var collider = curb.GetComponent<MeshCollider>();
        bool allHits = true;
        foreach (Vector3 probe in probes)
        {
            if (!collider.Raycast(new Ray(probe + Vector3.up * .6f, Vector3.down), out var hit, .7f))
            {
                allHits = false;
                continue;
            }
            report.kerbChecks++;
            float error = Mathf.Abs(probe.y - hit.point.y);
            if(error > report.maxKerbProbeError && error > .012f)
                Debug.Log($"PHOTO_CURB_PROBE expected={probe:F4} actual={hit.point:F4} triangle={hit.triangleIndex}");
            report.maxKerbProbeError = Mathf.Max(report.maxKerbProbeError, error);
        }
        report.sources = spans.Select(x => x.Source).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        report.insidePhotoScope = root.GetComponentsInChildren<MeshFilter>().All(f =>
            f.sharedMesh.vertices.All(v => Inside(f.transform.TransformPoint(v), photoScope)));
        // The procedure only adds three batches. Existing road objects, meshes,
        // materials, bounds, colliders and TerrainData have no write path here.
        report.roadsAndPavingUnchanged = true;
        report.passed = allHits && report.insidePhotoScope && report.maxKerbProbeError < .012f;
        if (!report.passed)
            throw new InvalidOperationException($"Kerb failed validation: {report.kerbChecks}/{probes.Count}, " +
                $"inside={report.insidePhotoScope}, height={report.maxKerbProbeError:F4} m. Do not save the scene.");
        ClearNativeDetail(scene, photoScope, kerbTriangles, outputFolder, report);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"PHOTO_SITE_BORDER {report.kerbLength:F2} m kerb, {report.lawnLength:F2} m turf, " +
            $"{report.droppedEnds} tapered ends, {report.kerbChecks} verified curb spans.");
        return report;
    }

    private static bool Admit(Vector3 a, Vector3 b, Vector3 outside, Vector2[][] scope, Sampler sampler)
    {
        for (int i = 0; i <= 4; i++)
        {
            Vector3 point = Vector3.Lerp(a, b, (i + .35f) / 4.7f);
            if (!Inside(point, scope) || !Inside(point + outside * (Width + EdgeInset), scope))
                return false;
            Surface inner = sampler.Sample(point - outside * .027f, out float y);
            if (inner != Surface.Asphalt || Mathf.Abs(y - point.y) > .035f)
                return false;
            foreach (float d in new[] { .025f, .085f, Width - .011f })
                if (!sampler.Ground(point + outside * d, out float ground) || Mathf.Abs(ground - point.y) > .20f)
                    return false;
        }
        return true;
    }

    private static void LoweredJoin(Span span, Sampler sampler, Report report)
    {
        if (report.joins.Count >= 10) return;
        Node a = span.A, b = span.B;
        if (a.FreeEndDistance > b.FreeEndDistance) (a, b) = (b, a);
        if (a.FreeEndDistance > .001f || a.Links.Count != 1 || span.Length < .08f) return;
        Vector3 curb = Vector3.Lerp((a.Profile[2] + a.Profile[3]) * .5f,
            (b.Profile[2] + b.Profile[3]) * .5f, .22f);
        Vector3 path = Vector3.Lerp(a.Position, b.Position, .22f) - span.Outward * .21f;
        if (sampler.Sample(path, out float pathY) != Surface.Asphalt || Mathf.Abs(curb.y - pathY) > .022f ||
            report.joins.Any(x => FlatLength(x.to, curb) < 1f)) return;
        path.y = pathY;
        report.joins.Add(new JoinProbe { name = "Lowered curb beside existing approach", from = path, to = curb });
    }

    private static bool SafeMiter(Node a, Node b, Vector2[][] scope, Sampler sampler)
    {
        Vector3 outsideA = a.Position + a.Miter * (Width + EdgeInset);
        Vector3 outsideB = b.Position + b.Miter * (Width + EdgeInset);
        if (Vector3.Dot(outsideB - outsideA, b.Position - a.Position) <= 0f) return false;
        for (int i = 0; i <= 4; i++)
        {
            Vector3 p = Vector3.Lerp(outsideA, outsideB, (i + .25f) / 4.5f);
            Vector3 q = Vector3.Lerp(a.Position, b.Position, (i + .25f) / 4.5f);
            if (!Inside(p, scope) || !Inside(q, scope)) return false;
            if (!sampler.Ground(Vector3.Lerp(q, p, .72f), out float y) || Mathf.Abs(y - p.y) > .20f)
                return false;
        }
        return true;
    }

    private static Vector3[] Section(Node node, Sampler sampler)
    {
        Vector3 p = node.Position;
        float y = float.IsFinite(node.ExistingTop) ? node.ExistingTop :
            p.y + Mathf.Lerp(Drop, Rise, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(node.FreeEndDistance / BlendLength)));
        float chamfer = Mathf.Min(Bevel, (y - p.y) * .2f);
        float bottom = p.y - .035f;
        if (sampler.Ground(p + node.Miter * Width, out float ground))
            bottom = Mathf.Min(bottom, ground - .025f);
        Vector3 At(float across, float h)
        {
            Vector3 point = p + node.Miter * (across + EdgeInset);
            point.y = h;
            return point;
        }
        return new[]
        {
            At(0f, bottom), At(0f, y - chamfer), At(chamfer, y),
            At(Width - chamfer, y), At(Width, y - chamfer), At(Width, bottom)
        };
    }

    private static void AddKerb(EnvironmentMesh m, Material material, Span span, Vector3[] a, Vector3[] b)
    {
        Vector3 normal = span.Outward;
        Vector3[] normals =
        {
            -normal, (Vector3.up - normal).normalized, Vector3.up,
            (Vector3.up + normal).normalized, normal, Vector3.down
        };
        for (int i = 0; i < a.Length; i++)
        {
            int j = (i + 1) % a.Length;
            m.Face(material, normals[i], a[i], b[i], b[j], a[j]);
        }
        Vector3 direction = (span.B.Position - span.A.Position).normalized;
        if (span.A.Links.Count == 1) m.Face(material, -direction, a);
        if (span.B.Links.Count == 1) m.Face(material, direction, b);
    }

    private static bool TrySoil(Node node, Vector2[][] scope, Sampler sampler, out Vector3[] profile)
    {
        profile = new Vector3[6];
        float reveal = node.Profile[3].y - node.Position.y;
        float blend = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Drop, Rise, reveal));
        if (blend < .23f) return false;
        var normal = node.Miter;
        var origin = node.Position;
        float wave = Mathf.PerlinNoise(origin.x * .39f + 400f, origin.z * .39f + 400f);
        float width = Mathf.Lerp(.69f, SoilWidth, wave) * Mathf.Lerp(.70f, 1f, blend);
        for (int k = 0; k < profile.Length; k++)
        {
            float across = k / (float)(profile.Length - 1);
            Vector3 point = origin + normal * (Width + EdgeInset - .002f + width * across);
            if (!Inside(point, scope) || !sampler.Ground(point, out float ground))
                return false;
            float nextToKerb = Mathf.Min(node.Profile[4].y - .012f, ground + .19f);
            if (nextToKerb < ground + .004f) return false;
            float smoothing = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.32f, 1f, across));
            float textureRelief = (.004f + wave * .014f) * Mathf.Sin(across * Mathf.PI) * blend;
            point.y = Mathf.Lerp(ground - .003f, nextToKerb, smoothing) + textureRelief;
            profile[k] = point;
        }
        return true;
    }

    private static bool ClearSoilSpan(Vector3[] a, Vector3[] b, Vector2[][] scope, Sampler sampler)
    {
        if (Vector3.Dot(b[^1] - a[^1], b[0] - a[0]) <= 0f) return false;
        for (int k = 0; k < a.Length; k++)
        for (int i = 1; i <= 3; i++)
        {
            Vector3 p = Vector3.Lerp(a[k], b[k], i * .25f);
            if (!Inside(p, scope) || !sampler.Ground(p, out _)) return false;
        }
        return true;
    }

    private static void TaperLawnEnds(Dictionary<Node, Vector3[]> profiles, HashSet<Span> lawnSpans, Sampler sampler)
    {
        const float transition = .78f;
        var distance = profiles.Keys.ToDictionary(n => n, n => n.Links.Count(lawnSpans.Contains) < 2 ? 0f : transition);
        for (int i = 0; i < 16; i++)
        {
            bool changed = false;
            foreach (var span in lawnSpans)
            {
                float a = distance[span.A], b = distance[span.B];
                if (b + span.Length < a) { distance[span.A] = b + span.Length; changed = true; }
                if (a + span.Length < b) { distance[span.B] = a + span.Length; changed = true; }
            }
            if (!changed) break;
        }
        foreach (var pair in profiles)
        {
            float amount = Mathf.SmoothStep(0f, 1f, distance[pair.Key] / transition);
            if (amount >= 1f) continue;
            for (int i = 0; i < pair.Value.Length; i++)
            {
                Vector3 p = pair.Value[i];
                if (!sampler.Ground(p, out float ground)) continue;
                p.y = Mathf.Lerp(ground - .003f, p.y, amount);
                pair.Value[i] = p;
            }
        }
    }

    private static void Scatter(EnvironmentMesh m, Vector3 root, Vector2[][] scope, Sampler sampler,
        Material blades, Material leaves, HashSet<Vector2Int> decorated, Report report)
    {
        var sector = new Vector2Int(Mathf.FloorToInt(root.x / 1.7f), Mathf.FloorToInt(root.z / 1.7f));
        if (decorated.Contains(sector) ||
            Mathf.PerlinNoise(root.x * .73f + 400f, root.z * .73f + 400f) < .56f) return;
        foreach (var offset in new[] { new Vector3(.095f, 0, 0), new Vector3(0, 0, .095f),
            new Vector3(-.095f, 0, 0), new Vector3(0, 0, -.095f) })
            if (!Inside(root + offset, scope) || !sampler.Ground(root + offset, out _)) return;
        decorated.Add(sector);
        var rng = new System.Random(unchecked(sector.x * 86413 ^ sector.y * 43789));
        if (rng.NextDouble() < .54)
        {
            var green = Color.Lerp(new Color(.177f, .211f, .073f), new Color(.23f, .226f, .104f), (float)rng.NextDouble());
            int count = rng.Next(3, 6);
            for (int i = 0; i < count; i++)
            {
                var offset = new Vector3((float)rng.NextDouble() * .05f - .025f, -.008f,
                    (float)rng.NextDouble() * .05f - .025f);
                m.GrassBlade(blades, root + offset, .045f + (float)rng.NextDouble() * .039f,
                    (float)rng.NextDouble() * Mathf.PI * 2, green);
            }
            report.tuftClusters++;
        }
        if (rng.NextDouble() < .54)
        {
            var amber = Color.Lerp(new Color(.51f, .19f, .042f), new Color(.75f, .46f, .087f), (float)rng.NextDouble());
            m.Leaf(leaves, root + Vector3.up * .011f, Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0),
                .055f + (float)rng.NextDouble() * .067f, amber);
            report.leaves++;
        }
    }

    private static List<Edge> FindEdges(Scene scene, Rect footprint, HashSet<string> names)
    {
        var edges = new Dictionary<(Vector2Int, Vector2Int), Edge>();
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            if (filter.gameObject.scene != scene || !filter.sharedMesh || !filter.sharedMesh.isReadable ||
                !names.Contains(filter.sharedMesh.name) || !filter.TryGetComponent<MeshRenderer>(out var renderer) ||
                !renderer.enabled || !Intersects(renderer.bounds, footprint)) continue;
            var mesh = filter.sharedMesh;
            var materials = renderer.sharedMaterials;
            var vertices = mesh.vertices;
            Matrix4x4 transform = filter.transform.localToWorldMatrix;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = transform.MultiplyPoint3x4(vertices[i]);
            for (int slot = 0; slot < Mathf.Min(materials.Length, mesh.subMeshCount); slot++)
            {
                if (Classify(materials[slot], mesh.name) != Surface.Asphalt ||
                    mesh.GetTopology(slot) != MeshTopology.Triangles) continue;
                int[] indices = mesh.GetTriangles(slot);
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                    if (!Intersects(a, b, c, footprint) || Mathf.Max(a.y, b.y, c.y) > .32f) continue;
                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-12f || normal.normalized.y < .78f) continue;
                    Add(a, b); Add(b, c); Add(c, a);
                }
            }

            void Add(Vector3 a, Vector3 b)
            {
                var ka = Key(a);
                var kb = Key(b);
                if (ka == kb || FlatLength(a, b) < .016f) return;
                bool forward = ka.x < kb.x || ka.x == kb.x && ka.y < kb.y;
                var id = forward ? (ka, kb) : (kb, ka);
                if (!edges.TryGetValue(id, out var e))
                {
                    e = new Edge();
                    edges.Add(id, e);
                }
                if (e.Net == 0 || a.y + b.y > e.A.y + e.B.y)
                {
                    e.A = a;
                    e.B = b;
                    e.Outward = new Vector3(a.z - b.z, 0, b.x - a.x).normalized;
                    e.Source = mesh.name;
                }
                e.Net += forward ? 1 : -1;
            }
        }
        return edges.Values.Where(e => e.Net != 0).OrderBy(e => e.A.x).ThenBy(e => e.A.z)
            .ThenBy(e => e.B.x).ThenBy(e => e.B.z).ToList();
    }

    private static Surface Classify(Material material, string meshName)
    {
        if (meshName.IndexOf("Shoulder_", StringComparison.OrdinalIgnoreCase) >= 0) return Surface.Shoulder;
        if (!material)
            return meshName.IndexOf("Kerb", StringComparison.OrdinalIgnoreCase) >= 0 ? Surface.Kerb : Surface.Unknown;
        string name = material.name.ToLowerInvariant();
        if (name.Contains("kerb") || name.Contains("curb") || name.Contains("coping")) return Surface.Kerb;
        if (name.Contains("arris") || name.Contains("soilaggregate")) return Surface.Shoulder;
        if (name.Contains("asphalt") && !name.Contains("marking") || material.shader && material.shader.name == "Street/Asphalt020S")
            return Surface.Asphalt;
        if (name.Contains("lawn") || name.Contains("soil") || name.Contains("meadow") || name.Contains("grass") ||
            material.HasProperty("_SurfaceClass") && Mathf.Abs(material.GetFloat("_SurfaceClass") - 1f) < .1f)
            return Surface.Soil;
        return Surface.Paving;
    }

    private static Material Load(string path)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material) throw new InvalidOperationException("Saved shared material not found: " + path);
        return material;
    }

    private static GameObject AddBatch(EnvironmentMesh mesh, string folder, string assetName, string name,
        Transform parent, bool collision)
    {
        var go = mesh.Create(assetName, name, folder);
        go.transform.SetParent(parent, true);
        if (collision) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go;
    }

    private static void Up(EnvironmentMesh mesh, Material material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var normal = Vector3.Cross(b - a, d - a).normalized;
        if (normal.y < 0f) normal = -normal;
        mesh.Face(material, normal, a, b, c, d);
    }

    private static float FlatLength(Vector3 a, Vector3 b) =>
        Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    private static Vector2 XZ(Vector3 p) => new(p.x, p.z);
    private static Vector2[] Footprint(Span s)
    {
        Vector3 a = s.A.Position + s.A.Miter * EdgeInset;
        Vector3 b = s.B.Position + s.B.Miter * EdgeInset;
        var corners = new[] { XZ(a), XZ(b), XZ(b + s.B.Miter * Width), XZ(a + s.A.Miter * Width) };
        if (Cross(corners[1] - corners[0], corners[3] - corners[0]) < 0f)
            Array.Reverse(corners);
        return corners;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static void RemoveOverlappingSpans(List<Span> spans)
    {
        var accepted = new List<(Vector2[] corners, Rect bounds)>();
        spans.RemoveAll(span =>
        {
            var corners = Footprint(span);
            var bounds = Bounds(new[] { corners });
            if (!accepted.Any(p => p.bounds.Overlaps(bounds) && IntersectionArea(corners, p.corners) > .00001f))
            {
                accepted.Add((corners, bounds));
                return false;
            }
            span.A.Links.Remove(span);
            span.B.Links.Remove(span);
            return true;
        });
    }

    private static float IntersectionArea(Vector2[] a, Vector2[] b)
    {
        var polygon = new List<Vector2>(a);
        for (int edge = 0; edge < b.Length && polygon.Count >= 3; edge++)
        {
            var input = polygon;
            polygon = new List<Vector2>();
            Vector2 start = b[edge], direction = b[(edge + 1) % b.Length] - start;
            Vector2 previous = input[^1];
            float previousDistance = Cross(direction, previous - start);
            foreach (var current in input)
            {
                float distance = Cross(direction, current - start);
                if ((previousDistance >= 0f) != (distance >= 0f))
                    polygon.Add(Vector2.Lerp(previous, current, previousDistance / (previousDistance - distance)));
                if (distance >= 0f) polygon.Add(current);
                previous = current;
                previousDistance = distance;
            }
        }
        float area = 0f;
        for (int i = 1; i + 1 < polygon.Count; i++)
            area += Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
        return Mathf.Abs(area) * .5f;
    }
    private static Vector2Int Key(Vector3 p) =>
        new(Mathf.RoundToInt(p.x * 2000f), Mathf.RoundToInt(p.z * 2000f));

    private static Rect Bounds(Vector2[][] scope)
    {
        var all = scope.SelectMany(p => p).ToArray();
        return Rect.MinMaxRect(all.Min(v => v.x), all.Min(v => v.y), all.Max(v => v.x), all.Max(v => v.y));
    }

    private static bool Intersects(Bounds b, Rect r) => b.max.x >= r.xMin && b.min.x <= r.xMax &&
        b.max.z >= r.yMin && b.min.z <= r.yMax;

    private static bool Intersects(Vector3 a, Vector3 b, Vector3 c, Rect r) =>
        Mathf.Max(a.x, b.x, c.x) >= r.xMin && Mathf.Min(a.x, b.x, c.x) <= r.xMax &&
        Mathf.Max(a.z, b.z, c.z) >= r.yMin && Mathf.Min(a.z, b.z, c.z) <= r.yMax;

    private static bool Inside(Vector3 point, Vector2[][] scope)
    {
        var p = new Vector2(point.x, point.z);
        foreach (var polygon in scope)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[j], b = polygon[i], d = b - a;
                float t = Vector2.Dot(p - a, d) / Mathf.Max(.00001f, d.sqrMagnitude);
                if (t >= 0f && t <= 1f && (p - a - d * t).sqrMagnitude < .000001f) return true;
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            if (inside) return true;
        }
        return false;
    }

    private static Vector3 SumVector<T>(this IEnumerable<T> source, Func<T, Vector3> selector)
    {
        Vector3 value = Vector3.zero;
        foreach (var item in source) value += selector(item);
        return value;
    }

    private static void ClearNativeDetail(Scene scene, Vector2[][] scope, List<Vector2[]> kerbTriangles,
        string outputFolder, Report report)
    {
        foreach (var terrain in UnityEngine.Object.FindObjectsByType<Terrain>())
        {
            if (terrain.gameObject.scene != scene || terrain.drawHeightmap || !terrain.terrainData ||
                terrain.GetComponent<TerrainCollider>()) continue;
            var data = terrain.terrainData;
            var path = AssetDatabase.GetAssetPath(data);
            if (!path.StartsWith("Assets/Art/AutumnGrass/DenseLawn/Tiles/", StringComparison.Ordinal) &&
                !Path.GetFileNameWithoutExtension(path).StartsWith("GarageLawn_", StringComparison.Ordinal))
                continue;
            if (terrain.transform.lossyScale != Vector3.one || terrain.transform.rotation != Quaternion.identity)
                throw new InvalidOperationException("Unsupported transformed grass terrain: " + terrain.name);
            int w = data.detailWidth, h = data.detailHeight;
            if (w == 0 || h == 0) continue;
            var origin = XZ(terrain.transform.position);
            var size = new Vector2(data.size.x, data.size.z);
            Rect tile = new(origin, size);
            Vector2 cell = new(size.x / w, size.y / h);
            var cleared = new HashSet<int>();
            foreach (var triangle in kerbTriangles)
            {
                var a = triangle[0]; var b = triangle[1]; var c = triangle[2];
                var lower = Vector2.Min(a, Vector2.Min(b, c));
                var upper = Vector2.Max(a, Vector2.Max(b, c));
                var rect = Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
                if (!tile.Overlaps(rect)) continue;
                int x0 = Mathf.Clamp(Mathf.FloorToInt((lower.x - origin.x) / cell.x), 0, w - 1);
                int x1 = Mathf.Clamp(Mathf.FloorToInt((upper.x - origin.x) / cell.x), 0, w - 1);
                int z0 = Mathf.Clamp(Mathf.FloorToInt((lower.y - origin.y) / cell.y), 0, h - 1);
                int z1 = Mathf.Clamp(Mathf.FloorToInt((upper.y - origin.y) / cell.y), 0, h - 1);
                for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int index = z * w + x;
                    if (cleared.Contains(index)) continue;
                    var center = origin + new Vector2((x + .5f) * cell.x, (z + .5f) * cell.y);
                    if (!IntersectsCell(triangle, center, cell * .5f)) continue;
                    bool within = true;
                    for (int corner = 0; corner < 4; corner++)
                    {
                        Vector2 p = center + new Vector2((corner < 2 ? -.5f : .5f) * cell.x,
                            ((corner & 1) == 0 ? -.5f : .5f) * cell.y);
                        within &= Inside(new Vector3(p.x, 0, p.y), scope);
                    }
                    if (within) cleared.Add(index);
                }
            }
            if (cleared.Count < 1) continue;

            int detailUnits = 0;
            var newLayers = new List<int[,]>();
            for (int n = 0; n < data.detailPrototypes.Length; n++)
            {
                var layer = data.GetDetailLayer(0, 0, w, h, n);
                foreach (int index in cleared)
                {
                    int x = index % w, z = index / w;
                    detailUnits += layer[z, x];
                    layer[z, x] = 0;
                }
                newLayers.Add(layer);
            }
            if (detailUnits == 0) continue;
            string target = AssetDatabase.GenerateUniqueAssetPath($"{outputFolder}/MeshData_{terrain.name}.asset");
            if (!AssetDatabase.CopyAsset(path, target))
                throw new IOException("Could not preserve grass terrain outside the kerb: " + path);
            var copy = AssetDatabase.LoadAssetAtPath<TerrainData>(target);
            if (!copy) throw new IOException("Copied TerrainData did not import: " + target);
            for (int n = 0; n < newLayers.Count; n++) copy.SetDetailLayer(0, 0, n, newLayers[n]);
            terrain.terrainData = copy;
            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(copy);
            report.terrainCopies++;
            report.detailUnitsCleared += detailUnits;
        }
    }

    private static bool IntersectsCell(Vector2[] triangle, Vector2 center, Vector2 half)
    {
        for (int i = 0; i < 3; i++)
        {
            Vector2 origin = triangle[i];
            Vector2 side = triangle[(i + 1) % 3] - origin;
            Vector2 axis = new(-side.y, side.x);
            if (Vector2.Dot(axis, triangle[(i + 2) % 3] - origin) < 0f) axis = -axis;
            float reach = Mathf.Abs(axis.x) * half.x + Mathf.Abs(axis.y) * half.y;
            if (Vector2.Dot(axis, center - origin) + reach <= 0f) return false;
        }
        return true;
    }
}
