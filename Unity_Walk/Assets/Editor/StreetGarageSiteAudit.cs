using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetGarageSiteAudit
{
    public static void Inspect()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Street.unity", OpenSceneMode.Single);
        Physics.SyncTransforms();
        var lines = new List<string>();
        var colliders = UnityEngine.Object.FindObjectsByType<Collider>();
        foreach (var collider in colliders.OrderBy(c => c.name))
        {
            var b = collider.bounds;
            if (!collider.enabled || !b.Intersects(new Bounds(new Vector3(30, 0, -40), new Vector3(30, 6, 35)))) continue;
            string asset = collider is MeshCollider m && m.sharedMesh
                ? AssetDatabase.GetAssetPath(m.sharedMesh) + "#" + m.sharedMesh.name
                : collider.GetType().Name;
            lines.Add($"{PathOf(collider.transform)} | {asset} | min={b.min:F3} max={b.max:F3}");
        }
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>().OrderBy(c => c.name))
        {
            var b = renderer.bounds;
            if (!renderer.enabled || !renderer.TryGetComponent<MeshFilter>(out var mesh) || !mesh.sharedMesh ||
                !b.Intersects(new Bounds(new Vector3(27, 0, -41), new Vector3(15, 1, 27)))) continue;
            lines.Add($"Render {PathOf(renderer.transform)} | {AssetDatabase.GetAssetPath(mesh.sharedMesh)}#{mesh.sharedMesh.name} | {b.min:F2} {b.max:F2}");
        }
        foreach (float z in new[] {-58, -56, -55, -54.4f, -53, -51, -50, -49, -47, -44, -41, -38, -35, -32, -30, -28, -26.8f})
        foreach (float x in new[] {19.55f, 21f, 22.9f, 25.5f, 27f, 31.1f, 33f, 34.45f, 40f, 44f})
        {
            var hits = Physics.RaycastAll(new Vector3(x, 6, z), Vector3.down, 12)
                .OrderByDescending(h => h.point.y)
                .Select(h => $"{h.point.y:F3} {h.collider.name}");
            lines.Add($"Hit ({x:F2},{z:F2}): " + string.Join(" / ", hits));
        }
        Directory.CreateDirectory("QA_HDRP/garage-site");
        File.WriteAllLines("QA_HDRP/garage-site/scene.txt", lines);
        Debug.Log($"GARAGE_SITE_DONE lines={lines.Count}");
    }

    private static string PathOf(Transform t)
    {
        var names = new Stack<string>();
        for (; t; t = t.parent) names.Push(t.name);
        return string.Join("/", names);
    }
}
