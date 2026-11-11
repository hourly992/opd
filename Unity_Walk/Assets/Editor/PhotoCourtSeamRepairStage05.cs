using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Refine the existing court connector against its actual adjoining colliders.
// All replacement meshes are written to this pass's own asset folder.
public static class PhotoCourtSeamRepairStage05
{
    const string Root = "Assets/PhotoReconstruction/Stage05Seams/Meshes";
    const string FloorName = "CourtDoorContinuousPaving05";
    const float X0 = 41.7f, X1 = 60.141938f, Z0 = 37.155f, Z1 = 39.455f;
    const float CenterZ = 38.305f, HalfWidth = 1.15f, EndBlend = .75f, SideFade = .8f;
    // The two rows either side of the center are fixed. The actual triangulated
    // centerline is then fixed too, despite the odd number of cross-section cells.
    const float CenterCore = .129f;

    [Serializable] sealed class Report
    {
        public string note = "Boundary levels sampled from existing adjoining pavement, with an 80cm fade into exposed edges. Corrections spread over the original width; centerline fixed away from the matched endpoints. Old protruding slab/kerb parts clipped at the exact new footprint, with outside geometry retained. No surveyed elevations are inferred.";
        public int neighborSamples, southAnchors, northAnchors, correctedVertices, clippedMeshes, clippedTriangles, retainedOutsideTriangles;
        public float maximumHeightCorrection, maximumSlope, maximumInteriorCenterlineDifference;
        public bool passed;
    }

    readonly struct CutVertex
    {
        public readonly Vector3 position, weights;
        public CutVertex(Vector3 position, Vector3 weights)
        {
            this.position = position;
            this.weights = weights;
        }
    }

    sealed class Replacement
    {
        public MeshFilter filter;
        public MeshCollider collider;
        public Mesh mesh;
        public string assetPath;
    }

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before repairing its court seams.");
        Physics.SyncTransforms();
        var floor = UnityEngine.Object.FindObjectsByType<MeshCollider>().Single(c => c.enabled && c.sharedMesh && c.sharedMesh.name == FloorName);
        string floorAsset = Root + "/" + FloorName + ".asset";
        if (AssetDatabase.GetAssetPath(floor.sharedMesh) == floorAsset)
        {
            Debug.Log("COURT_SEAM_REPAIR05 already applied.");
            return;
        }

        var report = new Report();
        var floorFilter = floor.GetComponent<MeshFilter>();
        if (!floorFilter || floorFilter.sharedMesh != floor.sharedMesh)
            throw new InvalidOperationException("The court floor renderer and collider do not share a mesh.");
        var oldFloor = floor.sharedMesh;
        int sampleCount = Mathf.CeilToInt((X1 - X0) / .10f) + 1;
        var centerHeights = new float[sampleCount];
        var south = new float[sampleCount];
        var north = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            float x = Mathf.Lerp(X0, X1, i / (float)(sampleCount - 1));
            float center = FloorHeight(floor, x, CenterZ);
            centerHeights[i] = center;
            south[i] = BoundaryHeight(floor, new Vector3(x, 0, Z0), Vector3.back, report) - center;
            north[i] = BoundaryHeight(floor, new Vector3(x, 0, Z1), Vector3.forward, report) - center;
            if (float.IsFinite(south[i])) report.southAnchors++;
            if (float.IsFinite(north[i])) report.northAnchors++;
        }
        if (report.southAnchors == 0 || report.northAnchors == 0)
            throw new InvalidOperationException("No physically adjoining pavement was found at one of the long court edges.");
        south = FadeUnanchored(south);
        north = FadeUnanchored(north);

        var positions = oldFloor.vertices;
        var westCorrections = new Dictionary<float, float>();
        var eastCorrections = new Dictionary<float, float>();
        foreach (float z in positions.Select(p => floor.transform.TransformPoint(p).z).Distinct())
        {
            float weight = CrossWeight(z);
            float westBase = centerHeights[0] + weight * (z < CenterZ ? south[0] : north[0]);
            float eastBase = centerHeights[sampleCount - 1] + weight * (z < CenterZ ? south[sampleCount - 1] : north[sampleCount - 1]);
            float west = BoundaryHeight(floor, new Vector3(X0, 0, z), Vector3.left, report);
            float east = BoundaryHeight(floor, new Vector3(X1, 0, z), Vector3.right, report);
            westCorrections[z] = float.IsFinite(west) ? west - westBase : 0;
            eastCorrections[z] = float.IsFinite(east) ? east - eastBase : 0;
        }
        for (int i = 0; i < positions.Length; i++)
        {
            var point = floor.transform.TransformPoint(positions[i]);
            float correction = CrossWeight(point.z) * SampleProfile(point.z < CenterZ ? south : north, point.x);
            correction += westCorrections[point.z] * Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - (point.x - X0) / EndBlend));
            correction += eastCorrections[point.z] * Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - (X1 - point.x) / EndBlend));
            if (!float.IsFinite(correction) || Mathf.Abs(correction) > .10f)
                throw new InvalidOperationException("An adjacent level is inconsistent with a small court seam correction.");
            if (Mathf.Abs(correction) > .00001f) report.correctedVertices++;
            report.maximumHeightCorrection = Mathf.Max(report.maximumHeightCorrection, Mathf.Abs(correction));
            positions[i] = floor.transform.InverseTransformPoint(point + Vector3.up * correction);
        }

        var refined = UnityEngine.Object.Instantiate(oldFloor);
        refined.name = FloorName;
        refined.vertices = positions;
        refined.RecalculateNormals();
        refined.tangents = EnvironmentMesh.Tangents(refined.normals);
        refined.RecalculateBounds();
        ValidateSlope(refined, floor.transform, report);
        // Detect and clip against the original floor before installing its refined
        // replacement. This removes the already measured artifacts even if the
        // raised seam would conceal part of them.
        var clipped = FindClippedMeshes(floor, report);
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        var savedFloor = Save(refined, floorAsset);
        Replace(floorFilter, floor, savedFloor);
        foreach (var replacement in clipped)
            Replace(replacement.filter, replacement.collider, Save(replacement.mesh, replacement.assetPath));
        Physics.SyncTransforms();
        for (int i = 0; i < sampleCount; i++)
        {
            float x = Mathf.Lerp(X0, X1, i / (float)(sampleCount - 1));
            if (x < X0 + EndBlend + .3f || x > X1 - EndBlend - .3f) continue;
            float difference = Mathf.Abs(centerHeights[i] - FloorHeight(floor, x, CenterZ));
            report.maximumInteriorCenterlineDifference = Mathf.Max(report.maximumInteriorCenterlineDifference, difference);
        }
        report.passed = report.correctedVertices > 0 && report.maximumInteriorCenterlineDifference < .001f && report.maximumSlope < .18f;
        string output = "street_qa/district_stage05_editor";
        Directory.CreateDirectory(output);
        File.WriteAllText(output + "/court_seam_repair.json", JsonUtility.ToJson(report, true));
        if (!report.passed) throw new InvalidOperationException("The court repair failed its slope or centerline preservation check.");
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("COURT_SEAM_REPAIR05 vertices=" + report.correctedVertices + " clippedTriangles=" + report.clippedTriangles + " maxSlope=" + report.maximumSlope + " centerlineDelta=" + report.maximumInteriorCenterlineDifference);
    }

    static float CrossWeight(float z) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(CenterCore, HalfWidth, Mathf.Abs(z - CenterZ)));

    static float FloorHeight(MeshCollider floor, float x, float z)
    {
        // Stay inside the exact rectangle when sampling the center's endpoints.
        var origin = new Vector3(Mathf.Clamp(x, X0 + .0001f, X1 - .0001f), 1.6f, z);
        if (floor.Raycast(new Ray(origin, Vector3.down), out var hit, 2.5f)) return hit.point.y;
        throw new InvalidOperationException("The current court centerline is not continuous at " + origin);
    }

    static float BoundaryHeight(MeshCollider floor, Vector3 edge, Vector3 outward, Report report)
    {
        report.neighborSamples++;
        if (!Neighbor(floor, edge + outward * .015f, out float near)) return float.NaN;
        if (Neighbor(floor, edge + outward * .04f, out float far) && Mathf.Abs(far - near) < .006f)
            return near - (far - near) * (.015f / .025f);
        return near;
    }

    static bool Neighbor(MeshCollider floor, Vector3 point, out float height)
    {
        var origin = new Vector3(point.x, 1.6f, point.z);
        var hits = Physics.RaycastAll(origin, Vector3.down, 2.7f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits.OrderByDescending(hit => hit.point.y))
        {
            if (hit.collider == floor || hit.normal.y < .3f || hit.point.y < -.1f || hit.point.y > .4f || hit.collider.GetComponentInParent<StreetVehicle>()) continue;
            var material = MaterialAt(hit);
            if (Retained(material)) break;
            if (hit.normal.y < .82f || !Paved(material)) continue;
            height = hit.point.y;
            return true;
        }
        height = 0;
        return false;
    }

    static Material MaterialAt(RaycastHit hit)
    {
        var renderer = hit.collider.GetComponent<Renderer>();
        if (!renderer || !renderer.enabled) return null;
        var materials = renderer.sharedMaterials;
        if (!(hit.collider is MeshCollider mc) || !mc.sharedMesh || hit.triangleIndex < 0)
            return materials.Length == 1 ? materials[0] : null;
        int end = 0;
        for (int sub = 0; sub < mc.sharedMesh.subMeshCount; sub++)
        {
            end += (int)mc.sharedMesh.GetIndexCount(sub) / 3;
            if (hit.triangleIndex < end) return sub < materials.Length ? materials[sub] : null;
        }
        return null;
    }

    static bool Kerb(Material material) => material && (HasName(material, "kerb") || HasName(material, "curb"));
    static bool Retained(Material material) => material && (Kerb(material) || HasName(material, "coping") || HasName(material, "retaining"));
    static bool HasName(Material material, string token) => material.name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    static bool Paved(Material material)
    {
        if (!material || HasName(material, "grass") || HasName(material, "soil") || HasName(material, "mulch")) return false;
        float type = material.HasProperty("_SurfaceClass") ? material.GetFloat("_SurfaceClass") : 0;
        return type >= 3.5f || HasName(material, "asphalt") || HasName(material, "pav") || HasName(material, "concrete") || HasName(material, "footpath") || HasName(material, "edge") || material.name == "Path";
    }

    static float[] FadeUnanchored(float[] profile)
    {
        var result = (float[])profile.Clone();
        float step = (X1 - X0) / (profile.Length - 1);
        int radius = Mathf.CeilToInt(SideFade / step);
        for (int i = 0; i < profile.Length; i++)
        {
            if (float.IsFinite(profile[i])) continue;
            int left = -1, right = -1;
            for (int j = 1; j <= radius && (left < 0 || right < 0); j++)
            {
                if (left < 0 && i - j >= 0 && float.IsFinite(profile[i - j])) left = i - j;
                if (right < 0 && i + j < profile.Length && float.IsFinite(profile[i + j])) right = i + j;
            }
            if (left >= 0 && right >= 0 && (right - left) * step <= SideFade)
            {
                result[i] = Mathf.Lerp(profile[left], profile[right], (i - left) / (float)(right - left));
                continue;
            }
            int nearest = left < 0 ? right : right < 0 || i - left <= right - i ? left : right;
            float weight = nearest < 0 ? 0 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(1 - Mathf.Abs(i - nearest) * step / SideFade));
            result[i] = nearest < 0 ? 0 : profile[nearest] * weight;
        }
        return result;
    }

    static float SampleProfile(float[] profile, float x)
    {
        float fraction = Mathf.InverseLerp(X0, X1, x) * (profile.Length - 1);
        int left = Mathf.Min(profile.Length - 2, Mathf.FloorToInt(fraction));
        return Mathf.Lerp(profile[left], profile[left + 1], fraction - left);
    }

    static void ValidateSlope(Mesh mesh, Transform transform, Report report)
    {
        var world = mesh.vertices.Select(transform.TransformPoint).ToArray();
        var indices = mesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            var a = world[indices[i]];
            var cross = Vector3.Cross(world[indices[i + 1]] - a, world[indices[i + 2]] - a);
            if (!float.IsFinite(cross.y) || cross.y <= 0) throw new InvalidOperationException("Invalid court repair triangle.");
            float slope = Mathf.Sqrt(cross.x * cross.x + cross.z * cross.z) / cross.y;
            report.maximumSlope = Mathf.Max(report.maximumSlope, slope);
        }
        if (report.maximumSlope >= .18f) throw new InvalidOperationException("Court seam grading would be too steep: " + report.maximumSlope);
    }

    static List<Replacement> FindClippedMeshes(MeshCollider floor, Report report)
    {
        var replacements = new List<Replacement>();
        foreach (var filter in UnityEngine.Object.FindObjectsByType<MeshFilter>())
        {
            var renderer = filter.GetComponent<MeshRenderer>();
            if (filter.gameObject == floor.gameObject || !filter.sharedMesh || !renderer || !renderer.enabled || filter.GetComponentInParent<StreetBuilding>() || filter.GetComponentInParent<StreetVehicle>()) continue;
            var bounds = renderer.bounds;
            if (bounds.max.x <= X0 || bounds.min.x >= X1 || bounds.max.z <= Z0 || bounds.min.z >= Z1 || bounds.min.y > 1.3f || bounds.max.y < 0) continue;
            var original = filter.sharedMesh;
            var world = original.vertices.Select(filter.transform.TransformPoint).ToArray();
            var materials = renderer.sharedMaterials;
            var targets = new Dictionary<int, HashSet<int>>();
            for (int sub = 0; sub < original.subMeshCount && sub < materials.Length; sub++)
            {
                var material = materials[sub];
                if (!Paved(material) && !Retained(material)) continue;
                var indices = original.GetTriangles(sub);
                var selected = new HashSet<int>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    var a = world[indices[i]];
                    var b = world[indices[i + 1]];
                    var c = world[indices[i + 2]];
                    var cross = Vector3.Cross(b - a, c - a);
                    if (!Intersects(a, b, c) || cross.sqrMagnitude < 1e-12f || Mathf.Abs(cross.y) / cross.magnitude > .35f) continue;
                    var inside = Triangle(a, b, c);
                    foreach (var plane in Planes(.002f)) inside = Clip(inside, plane.axis, plane.value, plane.sign, true);
                    if (inside.Count > 1 && Protrudes(inside, floor)) selected.Add(i);
                }
                if (selected.Count == 0) continue;
                if (Kerb(material))
                {
                    // Remove the cap and bevel too where this obsolete low kerb
                    // enters the paved interior, so no detached white cap remains.
                    for (int i = 0; i < indices.Length; i += 3)
                    {
                        var a = world[indices[i]];
                        var b = world[indices[i + 1]];
                        var c = world[indices[i + 2]];
                        if (Mathf.Max(a.y, Mathf.Max(b.y, c.y)) < .35f && Intersects(a, b, c)) selected.Add(i);
                    }
                }
                targets.Add(sub, selected);
            }
            if (targets.Count == 0) continue;
            var collider = filter.GetComponent<MeshCollider>();
            if (collider && collider.sharedMesh != original) throw new InvalidOperationException("Court edge collider uses a separate mesh: " + filter.name);
            var mesh = ClipMesh(original, world, targets, report);
            var id = GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId;
            replacements.Add(new Replacement { filter = filter, collider = collider, mesh = mesh, assetPath = Root + "/CourtTrim_" + id + ".asset" });
            report.clippedMeshes++;
        }
        return replacements;
    }

    static bool Protrudes(List<CutVertex> inside, MeshCollider floor)
    {
        bool Above(Vector3 point)
        {
            if (!floor.Raycast(new Ray(new Vector3(point.x, 1.6f, point.z), Vector3.down), out var hit, 2.5f)) return false;
            float excess = point.y - hit.point.y;
            return excess > .008f && excess <= 1;
        }
        for (int i = 0; i < inside.Count; i++)
        {
            var a = inside[i].position;
            var b = inside[(i + 1) % inside.Count].position;
            if (Above(a) || Above((a + b) * .5f)) return true;
        }
        return Above(inside.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex.position) / inside.Count);
    }

    // A measured box around an obsolete edge. Refuse to cut where the
    // independent adjoining pavement does not actually support the footprint.
    internal static Mesh ClipOverPaving(MeshFilter source, MeshCollider floor, Rect bounds, out int trimmed)
        => ClipOverPaving(source, floor, bounds, Matrix4x4.identity, false, out trimmed);

    internal static Mesh ClipOverPaving(MeshFilter source, MeshCollider floor, Rect bounds,
        Matrix4x4 worldToClip, bool levelJoint, out int trimmed)
    {
        var original = source.sharedMesh;
        var toClip = worldToClip * source.transform.localToWorldMatrix;
        var world = original.vertices.Select(toClip.MultiplyPoint3x4).ToArray();
        var toWorld = worldToClip.inverse;
        var planes = new[] { (axis: 0, value: bounds.xMin, sign: 1f), (axis: 0, value: bounds.xMax, sign: -1f),
            (axis: 2, value: bounds.yMin, sign: 1f), (axis: 2, value: bounds.yMax, sign: -1f) };
        bool Floor(Vector3 point, out RaycastHit hit)
        {
            var worldPoint = toWorld.MultiplyPoint3x4(point);
            return floor.Raycast(new Ray(new Vector3(worldPoint.x, 1.5f, worldPoint.z), Vector3.down), out hit, 2.1f)
                && hit.normal.y > .82f;
        }
        bool Supported(Vector3 point)
        {
            if (levelJoint)
                point = Vector3.MoveTowards(point, new Vector3(bounds.center.x, point.y, bounds.center.y), .001f);
            return Floor(point, out var hit) && point.y - hit.point.y < .28f;
        }
        Vector3 LevelEdge(Vector3 clipPoint, Vector3 localPoint)
        {
            const float tolerance = .00025f;
            bool atX = clipPoint.x >= bounds.xMin - tolerance && clipPoint.x <= bounds.xMax + tolerance;
            bool atZ = clipPoint.z >= bounds.yMin - tolerance && clipPoint.z <= bounds.yMax + tolerance;
            bool edgeX = Mathf.Abs(clipPoint.x - bounds.xMin) < tolerance || Mathf.Abs(clipPoint.x - bounds.xMax) < tolerance;
            bool edgeZ = Mathf.Abs(clipPoint.z - bounds.yMin) < tolerance || Mathf.Abs(clipPoint.z - bounds.yMax) < tolerance;
            if (!atX || !atZ || !(edgeX || edgeZ))
                return localPoint;
            var innerPoint = Vector3.MoveTowards(clipPoint, new Vector3(bounds.center.x, clipPoint.y, bounds.center.y), .003f);
            if (!Floor(innerPoint, out var hit) || clipPoint.y < hit.point.y || clipPoint.y - hit.point.y > .08f)
                return localPoint;
            var boundary = toWorld.MultiplyPoint3x4(clipPoint);
            boundary.y = hit.point.y;
            return source.transform.InverseTransformPoint(boundary);
        }

        var targets = new Dictionary<int, HashSet<int>>();
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            var indices = original.GetTriangles(sub);
            var selected = new HashSet<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                var a = world[indices[i]];
                var b = world[indices[i + 1]];
                var c = world[indices[i + 2]];
                if (Mathf.Max(a.x, b.x, c.x) <= bounds.xMin || Mathf.Min(a.x, b.x, c.x) >= bounds.xMax ||
                    Mathf.Max(a.z, b.z, c.z) <= bounds.yMin || Mathf.Min(a.z, b.z, c.z) >= bounds.yMax ||
                    Mathf.Max(a.y, b.y, c.y) > .35f)
                    continue;
                var inside = Triangle(a, b, c);
                foreach (var plane in planes)
                    inside = Clip(inside, plane.axis, plane.value, plane.sign, true);
                if (inside.Count < 3)
                    continue;
                if (inside.Select((v, j) => (v.position + inside[(j + 1) % inside.Count].position) * .5f).All(Supported)
                    && inside.All(v => Supported(v.position)))
                    selected.Add(i);
            }
            if (selected.Count > 0) targets.Add(sub, selected);
        }
        var report = new Report();
        var result = targets.Count == 0 ? null : ClipMesh(original, world, targets, report, planes,
            levelJoint ? LevelEdge : null);
        trimmed = report.clippedTriangles;
        return result;
    }

    static Mesh ClipMesh(Mesh original, Vector3[] world, Dictionary<int, HashSet<int>> targets, Report report,
        (int axis, float value, float sign)[] clipPlanes = null, Func<Vector3, Vector3, Vector3> levelEdge = null)
    {
        var vertices = original.vertices.ToList();
        var normals = original.normals.ToList();
        var tangents = original.tangents.ToList();
        var colors = original.colors.ToList();
        var uv = Enumerable.Range(0, 8).Select(channel =>
        {
            var values = new List<Vector4>();
            original.GetUVs(channel, values);
            return values;
        }).ToArray();
        int originalCount = vertices.Count;
        var output = new int[original.subMeshCount][];
        var planes = clipPlanes ?? Planes(0);
        for (int sub = 0; sub < original.subMeshCount; sub++)
        {
            var indices = original.GetTriangles(sub);
            if (!targets.TryGetValue(sub, out var selected)) { output[sub] = indices; continue; }
            var kept = new List<int>(indices.Length);
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (!selected.Contains(i)) { kept.Add(a); kept.Add(b); kept.Add(c); continue; }
                var source = Triangle(world[a], world[b], world[c]);
                var outside = new List<List<CutVertex>>();
                var remainder = source;
                foreach (var plane in planes)
                {
                    if (levelEdge != null)
                    {
                        // Keep matching breakpoints on both sides before snapping their heights.
                        var partitioned = new List<List<CutVertex>>();
                        foreach (var fragment in outside)
                        {
                            var low = Clip(fragment, plane.axis, plane.value, plane.sign, false);
                            var high = Clip(fragment, plane.axis, plane.value, plane.sign, true);
                            if (low.Count >= 3) partitioned.Add(low);
                            if (high.Count >= 3) partitioned.Add(high);
                        }
                        outside = partitioned;
                    }
                    var piece = Clip(remainder, plane.axis, plane.value, plane.sign, false);
                    if (piece.Count >= 3) outside.Add(piece);
                    remainder = Clip(remainder, plane.axis, plane.value, plane.sign, true);
                    if (remainder.Count == 0) break;
                }
                if (remainder.Count < 3) { kept.Add(a); kept.Add(b); kept.Add(c); continue; }
                report.clippedTriangles++;
                int Append(CutVertex point)
                {
                    int index = vertices.Count;
                    var w = point.weights;
                    Vector3 position = vertices[a] * w.x + vertices[b] * w.y + vertices[c] * w.z;
                    vertices.Add(levelEdge == null ? position : levelEdge(point.position, position));
                    if (normals.Count >= originalCount) normals.Add((normals[a] * w.x + normals[b] * w.y + normals[c] * w.z).normalized);
                    if (tangents.Count >= originalCount) tangents.Add(tangents[a] * w.x + tangents[b] * w.y + tangents[c] * w.z);
                    if (colors.Count >= originalCount) colors.Add(colors[a] * w.x + colors[b] * w.y + colors[c] * w.z);
                    foreach (var channel in uv)
                        if (channel.Count >= originalCount) channel.Add(channel[a] * w.x + channel[b] * w.y + channel[c] * w.z);
                    return index;
                }
                foreach (var piece in outside)
                {
                    int first = -1;
                    for (int j = 1; j + 1 < piece.Count; j++)
                    {
                        if (Vector3.Cross(piece[j].position - piece[0].position, piece[j + 1].position - piece[0].position).sqrMagnitude < 1e-12f) continue;
                        if (first < 0) first = Append(piece[0]);
                        kept.Add(first);
                        kept.Add(Append(piece[j]));
                        kept.Add(Append(piece[j + 1]));
                        report.retainedOutsideTriangles++;
                    }
                }
            }
            output[sub] = kept.ToArray();
        }
        var mesh = UnityEngine.Object.Instantiate(original);
        if (vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        if (normals.Count == vertices.Count) mesh.SetNormals(normals);
        if (tangents.Count == vertices.Count) mesh.SetTangents(tangents);
        if (colors.Count == vertices.Count) mesh.SetColors(colors);
        for (int channel = 0; channel < uv.Length; channel++)
            if (uv[channel].Count == vertices.Count) mesh.SetUVs(channel, uv[channel]);
        mesh.subMeshCount = output.Length;
        for (int sub = 0; sub < output.Length; sub++) mesh.SetTriangles(output[sub], sub);
        mesh.RecalculateBounds();
        return mesh;
    }

    static (int axis, float value, float sign)[] Planes(float margin) => new[] { (0, X0 + margin, 1f), (0, X1 - margin, -1f), (2, Z0 + margin, 1f), (2, Z1 - margin, -1f) };
    static List<CutVertex> Triangle(Vector3 a, Vector3 b, Vector3 c) => new List<CutVertex> { new CutVertex(a, Vector3.right), new CutVertex(b, Vector3.up), new CutVertex(c, Vector3.forward) };

    static bool Intersects(Vector3 a, Vector3 b, Vector3 c) =>
        Mathf.Max(a.x, Mathf.Max(b.x, c.x)) > X0 && Mathf.Min(a.x, Mathf.Min(b.x, c.x)) < X1 &&
        Mathf.Max(a.z, Mathf.Max(b.z, c.z)) > Z0 && Mathf.Min(a.z, Mathf.Min(b.z, c.z)) < Z1;

    static List<CutVertex> Clip(List<CutVertex> polygon, int axis, float value, float sign, bool inside)
    {
        var result = new List<CutVertex>();
        if (polygon.Count == 0) return result;
        var previous = polygon[polygon.Count - 1];
        bool Keep(CutVertex point) => ((point.position[axis] - value) * sign > 0) == inside;
        foreach (var current in polygon)
        {
            bool currentKept = Keep(current);
            bool previousKept = Keep(previous);
            if (currentKept != previousKept)
            {
                float t = (value - previous.position[axis]) / (current.position[axis] - previous.position[axis]);
                var position = Vector3.Lerp(previous.position, current.position, t);
                position[axis] = value;
                result.Add(new CutVertex(position, Vector3.Lerp(previous.weights, current.weights, t)));
            }
            if (currentKept) result.Add(current);
            previous = current;
        }
        return result;
    }

    static Mesh Save(Mesh mesh, string path)
    {
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!saved)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        mesh.name = Path.GetFileNameWithoutExtension(path);
        EditorUtility.CopySerialized(mesh, saved);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(saved);
        return saved;
    }

    static void Replace(MeshFilter filter, MeshCollider collider, Mesh mesh)
    {
        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);
        if (!collider) return;
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
        EditorUtility.SetDirty(collider);
    }
}
