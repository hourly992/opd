using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class DistrictStairRepairs
{
    private const string AssetRoot = "Assets/PhotoReconstruction/Stage15Stairs";
    private const string ReportFolder = "street_qa/district_entrances_20261110";
    private const string PorchPrefix = "Access · подход к крыльцу ";
    internal const string StairName = "Крыльцо · цельные ступени";
    private const float HalfWidth = 1.2f;
    private const float StepHeight = .16f;
    private const float TreadDepth = .29f;
    private const float Front = .01f;
    private const float Back = -1.64f;
    private const float GeometryTolerance = .002f;
    private static readonly Bounds[] OriginalBoxes =
    {
        new Bounds(new Vector3(0, .08f, -.14f), new Vector3(2.4f, .16f, .30f)),
        new Bounds(new Vector3(0, .16f, -.43f), new Vector3(2.4f, .32f, .30f)),
        new Bounds(new Vector3(0, .24f, -.72f), new Vector3(2.4f, .48f, .30f)),
        new Bounds(new Vector3(0, .32f, -1.01f), new Vector3(2.4f, .64f, .30f)),
        new Bounds(new Vector3(0, .32f, -1.19f), new Vector3(2.4f, .64f, .90f)),
    };

    [Serializable]
    private sealed class Report
    {
        public int accessMarkers, porchMarkers, repaired, alreadyRepaired, retained, treadProbes, sideProbes, approachProbes;
        public float maximumFirstRise;
        public List<Entry> entries = new List<Entry>();
    }

    [Serializable]
    private sealed class Entry
    {
        public string address, marker, status;
        public Vector3 position, outward;
        public int[] oldTrianglesPerLod;
        public int originalColliders, approachProbes;
        public float maximumFirstRise;
    }

    private sealed class Porch
    {
        internal Transform marker;
        internal BoxCollider[] oldColliders;
        internal Cut[] cuts;
        internal Entry record;
    }

    private sealed class Cut
    {
        internal Material material;
        internal List<(int sub, int offset)> triangles = new List<(int, int)>();
    }

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before repairing district stairs.");

        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        Physics.SyncTransforms();
        var report = new Report();
        foreach (var building in UnityEngine.Object.FindObjectsByType<StreetBuilding>()
                     .OrderBy(b => b.address, StringComparer.Ordinal).ThenBy(b => b.name, StringComparer.Ordinal))
        {
            var access = building.GetComponentsInChildren<Transform>()
                .Where(t => t.name.StartsWith("Access · ", StringComparison.Ordinal)).ToArray();
            report.accessMarkers += access.Length;
            var markers = access.Where(t => t.name.StartsWith(PorchPrefix, StringComparison.Ordinal)).ToArray();
            report.porchMarkers += markers.Length;
            if (markers.Length == 0) continue;

            var bodyColliders = building.GetComponentsInChildren<BoxCollider>();
            var filters = building.GetComponent<LODGroup>().GetLODs()
                .Select(lod => lod.renderers[0].GetComponent<MeshFilter>()).ToArray();
            var repairs = new List<Porch>();
            foreach (var marker in markers)
            {
                var record = new Entry
                {
                    address = building.name, marker = marker.name, position = marker.position, outward = marker.forward,
                };
                report.entries.Add(record);
                var current = marker.Find(StairName);
                if (current)
                {
                    Verify(marker, current.GetComponent<MeshCollider>(), record, report);
                    record.status = "verified-existing-repair";
                    report.alreadyRepaired++;
                    continue;
                }

                var oldColliders = FindOriginalColliders(bodyColliders, marker);
                var cuts = filters.Select(filter => FindOriginalTriangles(filter, marker)).ToArray();
                record.oldTrianglesPerLod = cuts.Select(cut => cut.triangles.Count).ToArray();
                record.originalColliders = oldColliders.Length;
                // Require both independent signatures. The photo-specific low thresholds,
                // ramps and retained access markers can share names with standard porches.
                if (oldColliders.Length != 4 || cuts.Length != 3 ||
                    cuts.Any(cut => !cut.material || cut.triangles.Count != 60))
                {
                    record.status = "custom-or-different-geometry";
                    report.retained++;
                    continue;
                }
                repairs.Add(new Porch { marker = marker, oldColliders = oldColliders, cuts = cuts, record = record });
            }

            if (repairs.Count == 0) continue;
            for (int lod = 0; lod < filters.Length; lod++)
                RemoveBoxes(filters[lod], repairs.SelectMany(porch => porch.cuts[lod].triangles), building, lod);

            foreach (var porch in repairs)
            {
                var heights = ApproachHeights(porch.marker).ToArray();
                float lowest = heights.Length == 0 ? 0 : heights.Min();
                float baseHeight = Mathf.Floor(Mathf.Min(-.025f, lowest - .04f) * 100f) / 100f;
                Mesh mesh = Stairs(baseHeight, porch.cuts[0].material);
                var stair = new GameObject(StairName);
                stair.transform.SetParent(porch.marker, false);
                stair.AddComponent<MeshFilter>().sharedMesh = mesh;
                stair.AddComponent<MeshRenderer>().sharedMaterial = porch.cuts[0].material;
                var collider = stair.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                GameObjectUtility.SetStaticEditorFlags(stair, StaticEditorFlags.BatchingStatic);
                foreach (var previous in porch.oldColliders)
                    UnityEngine.Object.DestroyImmediate(previous);

                Physics.SyncTransforms();
                Verify(porch.marker, collider, porch.record, report);
                porch.record.status = "repaired";
                report.repaired++;
            }
        }

        Directory.CreateDirectory(ReportFolder);
        File.WriteAllText(ReportFolder + "/stairs.json", JsonUtility.ToJson(report, true));
        if (report.repaired > 0) EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_STAIRS markers={report.accessMarkers} porches={report.porchMarkers} " +
                  $"repaired={report.repaired} alreadyRepaired={report.alreadyRepaired} retained={report.retained} " +
                  $"treadProbes={report.treadProbes} sideProbes={report.sideProbes} approachProbes={report.approachProbes} " +
                  $"maximumFirstRise={report.maximumFirstRise:F3}");
    }

    private static BoxCollider[] FindOriginalColliders(BoxCollider[] candidates, Transform marker)
    {
        var found = new List<BoxCollider>();
        foreach (var spec in OriginalBoxes.Take(4))
        {
            var matches = candidates.Where(box => box && box.enabled &&
                (box.size - spec.size).sqrMagnitude < GeometryTolerance * GeometryTolerance &&
                (marker.InverseTransformPoint(box.transform.TransformPoint(box.center)) - spec.center)
                    .sqrMagnitude < GeometryTolerance * GeometryTolerance).ToArray();
            if (matches.Length == 1) found.Add(matches[0]);
        }
        return found.ToArray();
    }

    private static Cut FindOriginalTriangles(MeshFilter filter, Transform marker)
    {
        var result = new Cut();
        var mesh = filter.sharedMesh;
        var materials = filter.GetComponent<MeshRenderer>().sharedMaterials;
        Matrix4x4 toMarker = marker.worldToLocalMatrix * filter.transform.localToWorldMatrix;
        var vertices = mesh.vertices;
        for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
        {
            var material = materials[sub];
            if (!material || !material.name.StartsWith("EntranceConcrete", StringComparison.Ordinal)) continue;
            var triangles = mesh.GetTriangles(sub);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = toMarker.MultiplyPoint3x4(vertices[triangles[i]]);
                if (Mathf.Abs(a.x) > HalfWidth + GeometryTolerance || a.y < -GeometryTolerance ||
                    a.y > StepHeight * 4 + GeometryTolerance || a.z > Front + GeometryTolerance ||
                    a.z < Back - GeometryTolerance) continue;
                Vector3 b = toMarker.MultiplyPoint3x4(vertices[triangles[i + 1]]);
                Vector3 c = toMarker.MultiplyPoint3x4(vertices[triangles[i + 2]]);
                if (!OriginalBoxes.Any(box => OnBoxFace(box, a, b, c))) continue;
                result.triangles.Add((sub, i));
                result.material = material;
            }
        }
        return result;
    }

    private static bool OnBoxFace(Bounds box, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 min = box.min;
        Vector3 max = box.max;
        // Exact corners separate the original boxes from smaller doorway sill
        // triangles that may share a face plane within the same local envelope.
        if (!AtCorner(a, min, max) || !AtCorner(b, min, max) || !AtCorner(c, min, max)) return false;
        for (int axis = 0; axis < 3; axis++)
            if (NearPlane(a[axis], b[axis], c[axis], min[axis]) || NearPlane(a[axis], b[axis], c[axis], max[axis]))
                return true;
        return false;
    }

    private static bool AtCorner(Vector3 p, Vector3 min, Vector3 max)
    {
        for (int axis = 0; axis < 3; axis++)
            if (Mathf.Abs(p[axis] - min[axis]) >= GeometryTolerance &&
                Mathf.Abs(p[axis] - max[axis]) >= GeometryTolerance)
                return false;
        return true;
    }

    private static bool NearPlane(float a, float b, float c, float plane) =>
        Mathf.Abs(a - plane) < GeometryTolerance && Mathf.Abs(b - plane) < GeometryTolerance &&
        Mathf.Abs(c - plane) < GeometryTolerance;

    private static void RemoveBoxes(MeshFilter filter, IEnumerable<(int sub, int offset)> cuts, StreetBuilding building, int lod)
    {
        ulong id = GlobalObjectId.GetGlobalObjectIdSlow(building).targetObjectId;
        var source = filter.sharedMesh;
        var edited = UnityEngine.Object.Instantiate(source);
        edited.name = $"StairsTrimmed_{id}_LOD{lod}";
        foreach (var submesh in cuts.GroupBy(cut => cut.sub))
        {
            var cutOffsets = submesh.Select(cut => cut.offset).ToHashSet();
            var original = source.GetTriangles(submesh.Key);
            var retained = new List<int>(original.Length - 3 * cutOffsets.Count);
            for (int i = 0; i < original.Length; i += 3)
            {
                if (cutOffsets.Contains(i)) continue;
                retained.Add(original[i]);
                retained.Add(original[i + 1]);
                retained.Add(original[i + 2]);
            }
            edited.SetTriangles(retained, submesh.Key, false);
        }
        string path = $"{AssetRoot}/Meshes/{edited.name}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(edited, saved);
            UnityEngine.Object.DestroyImmediate(edited);
        }
        else
        {
            saved = edited;
            AssetDatabase.CreateAsset(saved, path);
        }
        filter.sharedMesh = saved;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        EditorUtility.SetDirty(filter);
    }

    private static Mesh Stairs(float baseHeight, Material concrete)
    {
        string name = $"DistrictConcreteStairs_{Mathf.RoundToInt(baseHeight * 100)}";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>($"{AssetRoot}/Meshes/{name}.asset");
        if (saved) return saved;

        // First point is at the low rear corner. It sees every edge of this
        // monotonically rising profile, so both side fans cover the full solid.
        var profile = new List<Vector2> { new Vector2(Back, baseHeight), new Vector2(Front, baseHeight) };
        for (int step = 0; step < 4; step++)
        {
            float y = (step + 1) * StepHeight;
            profile.Add(new Vector2(Front - step * TreadDepth, y));
            profile.Add(new Vector2(step == 3 ? Back : Front - (step + 1) * TreadDepth, y));
        }

        var geometry = new EnvironmentMesh();
        Vector3 P(float x, Vector2 p) => new Vector3(x, p.y, p.x);
        foreach (int side in new[] { -1, 1 })
            geometry.Face(concrete, Vector3.right * side, profile.Select(p => P(side * HalfWidth, p)).ToArray());
        for (int i = 0; i < profile.Count; i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Count];
            Vector3 normal = i == 0 ? Vector3.down : i == profile.Count - 1 ? Vector3.back :
                a.y == b.y ? Vector3.up : Vector3.forward;
            geometry.Face(concrete, normal, P(-HalfWidth, a), P(HalfWidth, a), P(HalfWidth, b), P(-HalfWidth, b));
        }
        var temporary = geometry.Create(name, StairName, AssetRoot, true);
        var mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temporary);
        return mesh;
    }

    private static IEnumerable<float> ApproachHeights(Transform marker)
    {
        foreach (float depth in new[] { .05f, .18f })
        foreach (float across in new[] { -.95f, 0, .95f })
        {
            Vector3 start = marker.TransformPoint(new Vector3(across, .40f, depth));
            var hit = Physics.RaycastAll(start, Vector3.down, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(h => h.normal.y > .65f && !h.collider.GetComponentInParent<StreetBuilding>())
                .OrderBy(h => h.distance).FirstOrDefault();
            if (hit.collider) yield return marker.InverseTransformPoint(hit.point).y;
        }
    }

    private static void Verify(Transform marker, MeshCollider collider, Entry record, Report report)
    {
        if (!collider || !collider.sharedMesh || collider.GetComponent<MeshFilter>().sharedMesh != collider.sharedMesh)
            throw new InvalidOperationException("The repaired stairs do not share their visible and physical mesh.");
        for (int step = 0; step < 4; step++)
        {
            float depth = step == 3 ? -1.34f : Front - (step + .5f) * TreadDepth;
            float height = (step + 1) * StepHeight;
            foreach (float across in new[] { -.95f, 0, .95f })
            {
                Vector3 point = marker.TransformPoint(new Vector3(across, 1.2f, depth));
                if (!collider.Raycast(new Ray(point, -marker.up), out var hit, 1.4f) ||
                    Mathf.Abs(marker.InverseTransformPoint(hit.point).y - height) > GeometryTolerance)
                    throw new InvalidOperationException($"The repaired stair tread has no support at {point:F3}.");
                report.treadProbes++;
            }
            foreach (int side in new[] { -1, 1 })
            {
                var ray = new Ray(marker.TransformPoint(new Vector3(side * 1.35f, height - .06f, depth)), -side * marker.right);
                if (!collider.Raycast(ray, out _, .3f))
                    throw new InvalidOperationException($"The repaired stair side is open at {marker.position:F3}.");
                report.sideProbes++;
            }
        }
        var approach = ApproachHeights(marker).ToArray();
        record.approachProbes = approach.Length;
        report.approachProbes += record.approachProbes;
        if (approach.Length > 0)
        {
            record.maximumFirstRise = Mathf.Max(0, StepHeight - approach.Min());
            report.maximumFirstRise = Mathf.Max(report.maximumFirstRise, record.maximumFirstRise);
        }
    }
}
