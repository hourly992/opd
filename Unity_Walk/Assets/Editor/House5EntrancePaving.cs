using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class House5EntrancePaving
{
    private const string AssetRoot = "Assets/PhotoReconstruction/House5EntrancePaving";
    private const string StairName = "Бетонное крыльцо · исправленные ступени";
    private const float Floor = -.025f;

    internal static void Apply()
    {
        var pilot = UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        var entries = pilot.entrances.OrderBy(t => t.name, StringComparer.Ordinal).Select(t => t.parent).ToArray();
        if (entries.Length != 5 || entries.Any(entry => entry.parent != entries[0].parent))
            throw new InvalidOperationException("The five entrances of the photo-based house are required.");

        var original = entries[0].parent.Find("Бетон ступеней").GetComponent<MeshRenderer>();
        if (entries.All(entry => entry.Find(StairName)) && !original.enabled)
            return;
        if (!original.enabled || original.GetComponent<MeshFilter>().sharedMesh.GetIndexCount(0) != 900 ||
            entries.Any(entry => entry.Find(StairName) || entry.GetComponents<BoxCollider>().Length != 5))
            throw new InvalidOperationException("The original house stair geometry is different from the inspected scene.");

        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        var stairs = CreateStairs(original.sharedMaterial);
        foreach (var entry in entries)
        {
            var stair = new GameObject(StairName);
            stair.transform.SetParent(entry, false);
            stair.AddComponent<MeshFilter>().sharedMesh = stairs;
            stair.AddComponent<MeshRenderer>().sharedMaterial = original.sharedMaterial;
            var collider = stair.AddComponent<MeshCollider>();
            collider.sharedMesh = stairs;
            foreach (var previous in entry.GetComponents<BoxCollider>())
                UnityEngine.Object.DestroyImmediate(previous);
            GameObjectUtility.SetStaticEditorFlags(stair, StaticEditorFlags.BatchingStatic);
        }
        original.enabled = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(original);
        Physics.SyncTransforms();
        foreach (var entry in entries)
            VerifyStair(entry);

        JoinStreetPavers(entries.Take(2).ToArray());
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    private static Mesh CreateStairs(Material concrete)
    {
        // Match the existing door and the four original nosings. The last bay
        // narrows where the upper tread meets the landing beside the portal.
        var mesh = new EnvironmentMesh();
        float[] edges = { 1.80f, 1.52f, 1.24f, .96f, .68f, -.035f };
        float previousTop = Floor, previousHalfWidth = 1.16f;
        for (int i = 0; i < edges.Length - 1; i++)
        {
            float front = edges[i], back = edges[i + 1];
            float top = Mathf.Min(i + 1, 4) * .16f;
            float half = i == 4 ? 1.035f : 1.16f;
            mesh.Face(concrete, Vector3.up, new Vector3(-half, top, front), new Vector3(half, top, front),
                new Vector3(half, top, back), new Vector3(-half, top, back));
            mesh.Face(concrete, Vector3.down, new Vector3(-half, Floor, front), new Vector3(half, Floor, front),
                new Vector3(half, Floor, back), new Vector3(-half, Floor, back));
            foreach (int side in new[] { -1, 1 })
            {
                float x = side * half;
                mesh.Face(concrete, Vector3.right * side, new Vector3(x, Floor, front), new Vector3(x, top, front),
                    new Vector3(x, top, back), new Vector3(x, Floor, back));
                if (half < previousHalfWidth)
                    CrossFace(mesh, concrete, Vector3.back, front, x, side * previousHalfWidth, Floor, top);
            }
            if (top > previousTop)
                CrossFace(mesh, concrete, Vector3.forward, front, -half, half, previousTop, top);
            previousTop = top;
            previousHalfWidth = half;
        }
        CrossFace(mesh, concrete, Vector3.back, edges[edges.Length - 1], -previousHalfWidth, previousHalfWidth, Floor, previousTop);
        var temporary = mesh.Create("House5ConcreteStairs", StairName, AssetRoot, true);
        var saved = temporary.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temporary);
        return saved;
    }

    private static void CrossFace(EnvironmentMesh mesh, Material material, Vector3 normal, float z,
        float minX, float maxX, float minY, float maxY)
    {
        mesh.Face(material, normal, new Vector3(minX, minY, z), new Vector3(maxX, minY, z),
            new Vector3(maxX, maxY, z), new Vector3(minX, maxY, z));
    }

    private static void VerifyStair(Transform entry)
    {
        var collider = entry.Find(StairName).GetComponent<MeshCollider>();
        for (int step = 0; step < 5; step++)
        {
            float depth = step == 4 ? .35f : 1.66f - step * .28f;
            float height = Mathf.Min(step + 1, 4) * .16f;
            foreach (float across in new[] { -.95f, 0, .95f })
            {
                Vector3 sample = entry.TransformPoint(new Vector3(across, 0, depth));
                if (!Cast(collider, sample, out var hit) || Mathf.Abs(hit.point.y - sample.y - height) > .001f)
                    throw new InvalidOperationException($"A repaired stair tread has no support at {sample:F3}.");
            }
            foreach (int side in new[] { -1, 1 })
            {
                var ray = new Ray(entry.TransformPoint(new Vector3(side * 1.4f, height - .06f, depth)), -side * entry.right);
                if (!collider.Raycast(ray, out _, .5f))
                    throw new InvalidOperationException("A repaired stair is open at the side.");
            }
        }
    }

    private static void JoinStreetPavers(Transform[] streetEntries)
    {
        var colliders = UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None);
        var paving = colliders.Single(c => c.sharedMesh && c.sharedMesh.name == "Stage03Surface_5");
        var road = colliders.Single(c => c.sharedMesh && c.sharedMesh.name == "ReferenceRoad");
        var filter = paving.GetComponent<MeshFilter>();
        var original = paving.sharedMesh;
        if (filter.sharedMesh != original)
            throw new InvalidOperationException("The saved house paving renderer and collider have different geometry.");

        bool NearPad(Vector3 world) => streetEntries.Any(entry =>
        {
            var local = entry.InverseTransformPoint(world);
            return Mathf.Abs(local.x) < 1.27f && local.z > 1.61f && local.z < 3.09f;
        });

        var mesh = UnityEngine.Object.Instantiate(original);
        mesh.name = "House5StreetPavers";
        var vertices = mesh.vertices;
        var normals = mesh.normals;
        int changed = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 world = paving.transform.TransformPoint(vertices[i]);
            if (Mathf.Abs(world.y - .14f) > .001f || !NearPad(world))
                continue;
            if (!Cast(road, world, out var hit) || hit.point.y < .02f || hit.point.y > .06f)
                throw new InvalidOperationException($"Expected street asphalt below the raised house paving at {world:F3}.");
            vertices[i] = paving.transform.InverseTransformPoint(hit.point + Vector3.up * .004f);
            if (normals[i].y > .9f)
                normals[i] = paving.transform.InverseTransformDirection(hit.normal);
            changed++;
        }
        if (changed < 8)
            throw new InvalidOperationException("The two raised street paving pads were not found in the inspected mesh.");
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();

        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var oldSurfaceChecks = TouchedChecks(access.surfaceChecks, .06f);
        var oldLandingChecks = TouchedChecks(access.entranceLandingChecks, .025f);
        string path = AssetRoot + "/Meshes/House5StreetPavers.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(mesh, saved);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        else
        {
            saved = mesh;
            AssetDatabase.CreateAsset(saved, path);
        }
        filter.sharedMesh = saved;
        paving.sharedMesh = saved;
        EditorUtility.SetDirty(filter);
        EditorUtility.SetDirty(paving);
        Physics.SyncTransforms();

        if (!access.surfaces.Contains(paving))
            access.surfaces = access.surfaces.Append(paving).ToArray();
        Refresh(access.surfaceChecks, oldSurfaceChecks);
        Refresh(access.entranceLandingChecks, oldLandingChecks);
        EditorUtility.SetDirty(access);

        foreach (var entry in streetEntries)
            foreach (float depth in new[] { 1.85f, 2.3f, 2.8f })
                foreach (float across in new[] { -1.12f, 0, 1.12f })
                {
                    Vector3 sample = entry.TransformPoint(new Vector3(across, 0, depth));
                    if (!Cast(road, sample, out var asphalt) || !Cast(paving, sample, out var tile) ||
                        Mathf.Abs(tile.point.y - asphalt.point.y - .004f) > .002f)
                        throw new InvalidOperationException($"The repaired paving does not meet the asphalt at {sample:F3}.");
                }
        Debug.Log($"HOUSE5_ENTRANCE_PAVING stairs=5 loweredPads=2 vertices={changed} " +
                  $"resurveyedRoutes={oldSurfaceChecks.Count} resurveyedLandings={oldLandingChecks.Count}");

        List<int> TouchedChecks(Vector3[] points, float tolerance)
        {
            var touched = new List<int>();
            for (int i = 0; i < points.Length; i++)
            {
                var point = points[i];
                if (NearPad(point) && Cast(paving, point, out var hit) && Mathf.Abs(hit.point.y - point.y) < tolerance)
                    touched.Add(i);
            }
            return touched;
        }

        void Refresh(Vector3[] points, List<int> indices)
        {
            foreach (int index in indices)
            {
                if (!Cast(paving, points[index], out var hit))
                    throw new InvalidOperationException($"A paving check lost support at {points[index]:F3}.");
                points[index] = hit.point;
            }
        }
    }

    private static bool Cast(MeshCollider surface, Vector3 point, out RaycastHit hit) =>
        surface.Raycast(new Ray(point + Vector3.up * 2f, Vector3.down), out hit, 2.4f) && hit.normal.y > .85f;
}
