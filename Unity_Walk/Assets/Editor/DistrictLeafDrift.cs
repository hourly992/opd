using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Random = System.Random;

internal static class DistrictLeafDrift
{
    private static string Root => UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path == "Assets/Scenes/Street.unity"
        ? "Assets/Art/FullVisual" : "Assets/PhotoReconstruction/Stage16Leaf";
    private const string GroupName = "Осень · листья под порывами ветра";

    internal static void Apply(bool wholeDistrict = false)
    {
        if (GameObject.Find(GroupName)) return;
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
            "Assets/PhotoReconstruction/Stage09Courtyard/Leaves/WindblownLeaf.asset");
        var material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/PhotoReconstruction/Stage08Autumn/Materials/FallenLeaves.mat");
        if (!mesh || !material)
            throw new InvalidOperationException("The saved leaf shape and material are required.");

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var gold = Tint(mesh, "DriftGold", new Color(.99f, .64f, .11f));
        var copper = Tint(mesh, "DriftCopper", new Color(.88f, .29f, .055f));
        var group = new GameObject(GroupName);
        var paths = new List<StreetWindblownLeaves.Path>();
        var random = new Random(161110);
        Physics.SyncTransforms();
        int sites = 0;
        var anchors = wholeDistrict
            ? new[] { new Vector2(-25, 15), new Vector2(-11, -12), new Vector2(8, 14), new Vector2(15, -39),
                new Vector2(29, 38), new Vector2(48, 39), new Vector2(-110, 157), new Vector2(-44, -52),
                new Vector2(122, 8), new Vector2(170, 101), new Vector2(174, 34),
                new Vector2(-22, -103), new Vector2(12, -175), new Vector2(76, -106) }
            : new[] { new Vector2(29, 38), new Vector2(38, 38), new Vector2(48, 39), new Vector2(55, 39) };
        foreach (var anchor in anchors)
        {
            int added = 0;
            for (int attempt = 0; attempt < 65 && added < 11; attempt++)
            {
                var p = anchor + new Vector2(Between(random, -4.3f, 4.3f), Between(random, -4.3f, 4.3f));
                var q = p + new Vector2(Between(random, .9f, 2.9f), Between(random, .4f, 1.9f));
                if (!Ground(p, out Vector3 start) || !Ground(q, out Vector3 end)) continue;
                start.y += Between(random, 2.4f, 4.8f);
                end.y += .27f;
                Vector3 travel = end - start;
                if (Physics.CheckSphere(start, .21f, ~0, QueryTriggerInteraction.Ignore) ||
                    Physics.SphereCast(start, .21f, travel.normalized, out _, travel.magnitude, ~0,
                        QueryTriggerInteraction.Ignore)) continue;

                var leaf = new GameObject($"Лист {paths.Count + 1}");
                leaf.transform.SetParent(group.transform, false);
                leaf.transform.position = start;
                leaf.AddComponent<MeshFilter>().sharedMesh = added % 3 == 0 ? copper : gold;
                var renderer = leaf.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.enabled = false;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                float period = Between(random, 6.5f, 10.5f);
                paths.Add(new StreetWindblownLeaves.Path
                {
                    renderer = renderer, from = start, to = end, period = period,
                    phase = Between(random, 0, period), flightTime = Between(random, 3.5f, 5.6f),
                    scale = Between(random, .78f, 1.46f)
                });
                added++;
            }
            if (added > 0) sites++;
        }
        if (sites < (wholeDistrict ? 10 : 3) || paths.Count < (wholeDistrict ? 85 : 25))
            throw new InvalidOperationException("Too few clear trajectories for the district leaf drift.");
        group.AddComponent<StreetWindblownLeaves>().paths = paths.ToArray();
        Debug.Log($"DISTRICT_LEAF_DRIFT paths={paths.Count} sites={sites}");
    }

    private static bool Ground(Vector2 point, out Vector3 ground)
    {
        if (Physics.Raycast(new Vector3(point.x, 4.5f, point.y), Vector3.down, out var hit, 6f, ~0,
                QueryTriggerInteraction.Ignore) && hit.collider is MeshCollider && hit.point.y < .85f &&
            hit.normal.y > .78f && !hit.collider.GetComponentInParent<StreetBuilding>() &&
            !hit.collider.GetComponentInParent<StreetVehicle>())
        {
            ground = hit.point;
            return true;
        }
        ground = default;
        return false;
    }

    private static Mesh Tint(Mesh source, string name, Color color)
    {
        string path = $"{Root}/Meshes/{name}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved) return saved;
        var mesh = UnityEngine.Object.Instantiate(source);
        mesh.name = name;
        var colors = source.colors;
        for (int i = 0; i < colors.Length; i++)
            colors[i] = color * Mathf.Lerp(.78f, 1.06f, Mathf.Clamp01(colors[i].r));
        mesh.colors = colors;
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static float Between(Random random, float from, float to) =>
        Mathf.Lerp(from, to, (float)random.NextDouble());
}
