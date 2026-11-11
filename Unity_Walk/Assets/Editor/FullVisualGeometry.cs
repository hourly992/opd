using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

internal static class FullVisualGeometry
{
    private const string AssetRoot = "Assets/PhotoReconstruction/FullVisualGeometry";
    private const string MeshFolder = AssetRoot + "/Meshes";
    private const string DistrictPrefix = "DistrictConcreteStairs_";
    private const string HouseMesh = "House5ConcreteStairs";
    private const string SoftPrefix = "Soft_";

    [Serializable]
    private sealed class Report
    {
        public int standardStairs, house5Stairs, stairHeightProbes, nosingProbes;
        public int slideRenderers, slideColliders, correctedGuardPanels, guardProbes;
        public int uniqueMeshes, originalTriangles, finalTriangles;
        public float maximumTreadHeightChange;
    }

    internal static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before finishing close-up geometry.");
        Directory.CreateDirectory(MeshFolder);
        AssetDatabase.Refresh();
        Physics.SyncTransforms();
        var report = new Report();
        var replacements = new Dictionary<Mesh, Mesh>();
        foreach (var filter in Object.FindObjectsByType<MeshFilter>())
        {
            var source = filter.sharedMesh;
            if (!source) continue;
            string name = source.name.StartsWith(SoftPrefix, StringComparison.Ordinal)
                ? source.name.Substring(SoftPrefix.Length) : source.name;
            bool house = name == HouseMesh;
            bool standard = name.StartsWith(DistrictPrefix, StringComparison.Ordinal);
            if (!house && !standard) continue;
            var collider = filter.GetComponent<MeshCollider>();
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!collider || collider.sharedMesh != source || !renderer)
                throw new InvalidOperationException("The inspected stair mesh and its collider differ.");

            var probes = StairProbes(house).ToArray();
            float[] before = probes.Select(point => Height(collider, filter.transform, point)).ToArray();
            if (!replacements.TryGetValue(source, out var target))
            {
                if (source.name.StartsWith(SoftPrefix, StringComparison.Ordinal)) target = source;
                else target = BuildStairs(source, renderer.sharedMaterial, house);
                replacements.Add(source, target);
                report.originalTriangles += TriangleCount(source);
                report.finalTriangles += TriangleCount(target);
            }
            Assign(filter, collider, target);
            Physics.SyncTransforms();
            for (int i = 0; i < probes.Length; i++)
            {
                float change = Mathf.Abs(Height(collider, filter.transform, probes[i]) - before[i]);
                if (change > .0005f)
                    throw new InvalidOperationException("A central stair tread moved during its edge finish.");
                report.maximumTreadHeightChange = Mathf.Max(report.maximumTreadHeightChange, change);
                report.stairHeightProbes++;
            }
            VerifyNosings(collider, filter.transform, house, report);
            if (house) report.house5Stairs++;
            else report.standardStairs++;
        }

        FinishSlideGuards(replacements, report);
        report.uniqueMeshes = replacements.Count;
        if (report.standardStairs != 54 || report.house5Stairs != 5 || report.slideRenderers != 10)
            throw new InvalidOperationException($"Unexpected saved geometry counts, stairs={report.standardStairs}+{report.house5Stairs}, slide LODs={report.slideRenderers}.");
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/full_visual_geometry.json", JsonUtility.ToJson(report, true));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"FULL_VISUAL_GEOMETRY stairs={report.standardStairs}+{report.house5Stairs} " +
                  $"heightProbes={report.stairHeightProbes} noses={report.nosingProbes} panels={report.correctedGuardPanels} " +
                  $"guardProbes={report.guardProbes} uniqueMeshes={report.uniqueMeshes} " +
                  $"triangles={report.originalTriangles}->{report.finalTriangles}");
    }

    private static Mesh BuildStairs(Mesh source, Material material, bool house)
    {
        if (source.subMeshCount != 1)
            throw new InvalidOperationException("The inspected stairs should have one concrete submesh.");
        float floor = source.bounds.min.y;
        var geometry = new EnvironmentMesh();
        float bevel = house ? .006f : .008f;
        float front = house ? 1.80f : .01f;
        float back = house ? .68f : -1.64f;
        float width = house ? 1.16f : 1.20f;
        float depth = house ? .28f : .29f;
        var profile = new List<Vector2> { new Vector2(back, floor), new Vector2(front, floor) };
        for (int step = 0; step < 4; step++)
        {
            float z = front - step * depth;
            float y = (step + 1) * .16f;
            profile.Add(new Vector2(z, y - bevel));
            profile.Add(new Vector2(z - bevel, y));
            profile.Add(new Vector2(step == 3 ? back : front - (step + 1) * depth, y));
        }
        Vector3 P(float x, Vector2 p) => new Vector3(x, p.y, p.x);
        foreach (int side in new[] { -1, 1 })
            geometry.Face(material, Vector3.right * side, profile.Select(p => P(side * width, p)).ToArray());
        for (int i = 0; i < profile.Count - (house ? 1 : 0); i++)
        {
            var a = profile[i];
            var b = profile[(i + 1) % profile.Count];
            Vector2 delta = b - a;
            Vector3 normal = new Vector3(0, -delta.x, delta.y).normalized;
            geometry.Face(material, normal, P(-width, a), P(width, a), P(width, b), P(-width, b));
        }
        if (house)
        {
            const float landingHalf = 1.035f, landingBack = -.035f, top = .64f;
            foreach (int side in new[] { -1, 1 })
                Quad(geometry, material, Vector3.back, side * landingHalf, side * width, floor, top, back);
            geometry.Face(material, Vector3.up, new Vector3(-landingHalf, top, back),
                new Vector3(landingHalf, top, back), new Vector3(landingHalf, top, landingBack), new Vector3(-landingHalf, top, landingBack));
            geometry.Face(material, Vector3.down, new Vector3(-landingHalf, floor, back),
                new Vector3(landingHalf, floor, back), new Vector3(landingHalf, floor, landingBack), new Vector3(-landingHalf, floor, landingBack));
            foreach (int side in new[] { -1, 1 })
                geometry.Face(material, Vector3.right * side,
                    new Vector3(side * landingHalf, floor, back), new Vector3(side * landingHalf, top, back),
                    new Vector3(side * landingHalf, top, landingBack), new Vector3(side * landingHalf, floor, landingBack));
            Quad(geometry, material, Vector3.back, -landingHalf, landingHalf, floor, top, landingBack);
        }
        var temporary = geometry.Create(SoftPrefix + source.name, "Concrete edge finish", AssetRoot, true);
        var mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temporary);
        return mesh;
    }

    private static void Quad(EnvironmentMesh mesh, Material material, Vector3 normal,
        float firstX, float lastX, float minY, float maxY, float z) =>
        mesh.Face(material, normal, new Vector3(firstX, minY, z), new Vector3(lastX, minY, z),
            new Vector3(lastX, maxY, z), new Vector3(firstX, maxY, z));

    private static IEnumerable<Vector3> StairProbes(bool house)
    {
        for (int step = 0; step < 4; step++)
        {
            float z = (house ? 1.80f : .01f) - (step + .5f) * (house ? .28f : .29f);
            foreach (float x in new[] { -.95f, 0, .95f })
                yield return new Vector3(x, 0, z);
        }
        yield return new Vector3(0, 0, house ? .35f : -1.34f);
    }

    private static void VerifyNosings(MeshCollider collider, Transform transform, bool house, Report report)
    {
        float bevel = house ? .006f : .008f;
        for (int step = 0; step < 4; step++)
        {
            float z = (house ? 1.80f : .01f) - step * (house ? .28f : .29f);
            float y = (step + 1) * .16f;
            var point = new Vector3(0, 0, z - bevel * .5f);
            float actual = Height(collider, transform, point);
            if (Mathf.Abs(actual - (y - bevel * .5f)) > .0005f)
                throw new InvalidOperationException("The concrete nosing chamfer does not follow its intended profile.");
            report.nosingProbes++;
        }
    }

    private static float Height(MeshCollider collider, Transform transform, Vector3 local)
    {
        var ray = new Ray(transform.TransformPoint(local + Vector3.up * 1.5f), -transform.up);
        if (!collider.Raycast(ray, out var hit, 2f))
            throw new InvalidOperationException("A concrete tread lost support.");
        return transform.InverseTransformPoint(hit.point).y;
    }

    private static void FinishSlideGuards(Dictionary<Mesh, Mesh> replacements, Report report)
    {
        foreach (var site in Object.FindObjectsByType<PlaygroundSite>())
        foreach (var item in site.equipment)
        foreach (var filter in item.GetComponentsInChildren<MeshFilter>())
        {
            var source = filter.sharedMesh;
            if (!source || !source.name.Replace(SoftPrefix, "").StartsWith("PlaygroundSlide_LOD", StringComparison.Ordinal)) continue;
            var renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer) throw new InvalidOperationException("The saved slide has no visible renderer.");
            if (!replacements.TryGetValue(source, out var replacement))
            {
                replacement = source.name.StartsWith(SoftPrefix, StringComparison.Ordinal) ? source : ReplaceGuards(source, renderer.sharedMaterials);
                replacements.Add(source, replacement);
                report.originalTriangles += TriangleCount(source);
                report.finalTriangles += TriangleCount(replacement);
            }
            var collider = filter.GetComponent<MeshCollider>();
            if (collider && collider.sharedMesh != source)
                throw new InvalidOperationException("The slide uses different visible and physical meshes.");
            Assign(filter, collider, replacement);
            Physics.SyncTransforms();
            if (collider)
            {
                VerifyGuards(collider, report);
                report.slideColliders++;
            }
            report.slideRenderers++;
            report.correctedGuardPanels += 2;
        }
    }

    private static Mesh ReplaceGuards(Mesh source, Material[] materials)
    {
        int panelSub = Array.FindIndex(materials, material => material &&
            material.name.StartsWith("PlaygroundOchre", StringComparison.Ordinal));
        if (panelSub < 0) throw new InvalidOperationException("The slide guard material is missing.");
        var vertices = source.vertices;
        var triangles = source.GetTriangles(panelSub);
        var retained = new List<int>();
        int removed = 0;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            bool guard = true;
            for (int j = 0; j < 3; j++)
            {
                Vector3 p = vertices[triangles[i + j]];
                guard &= Mathf.Abs(p.x) >= .55f && Mathf.Abs(p.x) <= .78f &&
                    p.y >= 1.32f && p.y <= 2.0f && p.z >= -.37f && p.z <= .77f;
            }
            if (guard) removed++;
            else
            {
                retained.Add(triangles[i]);
                retained.Add(triangles[i + 1]);
                retained.Add(triangles[i + 2]);
            }
        }
        if (removed != 312)
            throw new InvalidOperationException($"The saved slide guards have changed, expected 312 triangles, found {removed}.");

        var geometry = new EnvironmentMesh();
        foreach (int side in new[] { -1, 1 })
            RoundedGuard(geometry, materials[panelSub], side * .66f);
        var temporary = geometry.Create(SoftPrefix + source.name + "_Panels", "Rounded slide panels", AssetRoot, true);
        var patch = temporary.GetComponent<MeshFilter>().sharedMesh;
        var result = Object.Instantiate(source);
        result.name = SoftPrefix + source.name;
        int vertexOffset = vertices.Length;
        result.SetVertices(vertices.Concat(patch.vertices).ToArray());
        result.SetNormals(source.normals.Concat(patch.normals).ToArray());
        result.SetTangents(source.tangents.Concat(patch.tangents).ToArray());
        var uv = new List<Vector4>();
        source.GetUVs(0, uv);
        if (uv.Count == vertices.Length)
        {
            uv.AddRange(Enumerable.Repeat(Vector4.zero, patch.vertexCount));
            result.SetUVs(0, uv);
        }
        if (source.colors.Length == vertices.Length)
            result.SetColors(source.colors.Concat(Enumerable.Repeat(Color.white, patch.vertexCount)).ToArray());
        retained.AddRange(patch.GetTriangles(0).Select(index => index + vertexOffset));
        result.SetTriangles(retained, panelSub, false);
        result.RecalculateBounds();
        string patchPath = AssetDatabase.GetAssetPath(patch);
        Object.DestroyImmediate(temporary);
        AssetDatabase.DeleteAsset(patchPath);
        string path = $"{MeshFolder}/{result.name}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(result, saved);
            Object.DestroyImmediate(result);
        }
        else
        {
            saved = result;
            AssetDatabase.CreateAsset(saved, path);
        }
        return saved;
    }

    private static void RoundedGuard(EnvironmentMesh geometry, Material material, float x)
    {
        const float halfY = .335f, halfZ = .56f, radius = .06f, halfX = .0175f, bevel = .003f;
        Vector3[] Ring(float axis, float inset)
        {
            var ring = new List<Vector3>();
            for (int corner = 0; corner < 4; corner++)
            {
                float a = corner * Mathf.PI * .5f;
                float cz = (corner is 0 or 3 ? 1 : -1) * (halfZ - radius);
                float cy = (corner < 2 ? 1 : -1) * (halfY - radius);
                for (int i = 0; i <= 6; i++)
                {
                    float angle = a + i * Mathf.PI / 12f;
                    ring.Add(new Vector3(x + axis, 1.66f + cy + Mathf.Sin(angle) * (radius - inset),
                        .20f + cz + Mathf.Cos(angle) * (radius - inset)));
                }
            }
            return ring.ToArray();
        }
        var rings = new[] { Ring(-halfX, bevel), Ring(-halfX + bevel, 0), Ring(halfX - bevel, 0), Ring(halfX, bevel) };
        geometry.Face(material, Vector3.left, rings[0]);
        geometry.Face(material, Vector3.right, rings[3]);
        for (int r = 0; r < rings.Length - 1; r++)
        for (int i = 0; i < rings[r].Length; i++)
        {
            int j = (i + 1) % rings[r].Length;
            Vector3 normal = Vector3.Cross(rings[r + 1][i] - rings[r][i], rings[r][j] - rings[r][i]).normalized;
            Vector3 midpoint = (rings[r][i] + rings[r][j] + rings[r + 1][i] + rings[r + 1][j]) * .25f;
            Vector3 outward = midpoint - new Vector3(x, 1.66f, .20f);
            if (Vector3.Dot(normal, outward) < 0) normal = -normal;
            geometry.Face(material, normal, rings[r][i], rings[r + 1][i], rings[r + 1][j], rings[r][j]);
        }
    }

    private static void VerifyGuards(MeshCollider collider, Report report)
    {
        foreach (int side in new[] { -1, 1 })
        foreach (float y in new[] { 1.39f, 1.66f, 1.92f })
        foreach (float z in new[] { -.26f, .20f, .66f })
        {
            var transform = collider.transform;
            Vector3 from = transform.TransformPoint(new Vector3(side * .80f, y, z));
            Vector3 direction = transform.TransformDirection(Vector3.left * side);
            if (!collider.Raycast(new Ray(from, direction), out var hit, .20f))
                throw new InvalidOperationException("A corrected slide guard has an open face.");
            Vector3 point = transform.InverseTransformPoint(hit.point);
            if (Mathf.Abs(Mathf.Abs(point.x) - .6775f) > .004f)
                throw new InvalidOperationException("A corrected slide guard protrudes outside its intended thickness.");
            report.guardProbes++;
        }
    }

    private static void Assign(MeshFilter filter, MeshCollider collider, Mesh mesh)
    {
        filter.sharedMesh = mesh;
        if (collider) collider.sharedMesh = mesh;
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        EditorUtility.SetDirty(filter);
        if (collider)
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            EditorUtility.SetDirty(collider);
        }
    }

    private static int TriangleCount(Mesh mesh) => (int)(Enumerable.Range(0, mesh.subMeshCount)
        .Sum(sub => (long)mesh.GetIndexCount(sub)) / 3);
}
