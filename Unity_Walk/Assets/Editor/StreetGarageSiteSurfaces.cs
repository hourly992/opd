using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class StreetGarageLevelRepair
{
    private const string WastePadMesh = "Assets/Art/StreetRepair/Surfaces/Retained_1053760653.asset";
    private const string MainRoadMesh = "Assets/Art/StreetRepair/Surfaces/ReferenceRoad.asset";

    private static bool NearSite(Bounds b)
    {
        return b.max.x >= Left && b.min.x <= Right &&
            b.max.z >= Front && b.min.z <= Rear &&
            b.min.y <= .30f && b.max.y >= -.50f;
    }

    private static IEnumerable<MeshCollider> SelectGround(Scene scene) =>
        UnityEngine.Object.FindObjectsByType<MeshCollider>().Where(c => c.gameObject.scene == scene && c.enabled &&
            c.sharedMesh && c.sharedMesh.isReadable && c.bounds.max.y < 1f && NearSite(c.bounds) &&
            GroundMesh(AssetDatabase.GetAssetPath(c.sharedMesh)));

    private static bool GroundMesh(string asset) =>
        asset.StartsWith(Art + "/", StringComparison.Ordinal) &&
            !asset.Contains("/SouthGarageCourt/", StringComparison.Ordinal) &&
            !asset.Contains("/Garage", StringComparison.Ordinal) ||
        asset.StartsWith("Assets/PhotoReconstruction/Stage03Surfaces/Meshes/", StringComparison.Ordinal) ||
        asset.StartsWith("Assets/PhotoReconstruction/Stage04Courtyard/Meshes/", StringComparison.Ordinal);

    private static float SampleHeight(MeshCollider[] candidates, float x, float z)
    {
        float level = float.NegativeInfinity;
        var ray = new Ray(new Vector3(x,2,z),Vector3.down);
        foreach (var collider in candidates)
        {
            var b = collider.bounds;
            if (x < b.min.x-.002f || x > b.max.x+.002f || z < b.min.z-.002f || z > b.max.z+.002f)
                continue;
            if (x > 21.1f && x < 29.9f && z > -41.7f && z < -35.1f &&
                AssetDatabase.GetAssetPath(collider.sharedMesh) == WastePadMesh) continue;
            if (collider.Raycast(ray, out var hit, 3f) && hit.normal.y > .35f)
                level = Mathf.Max(level, hit.point.y);
        }
        if (!float.IsFinite(level))
            throw new InvalidOperationException($"No retained support sampled at ({x:F2}, {z:F2}).");
        return level;
    }

    private static List<string> HideWastePad(float level)
    {
        var screen = UnityEngine.Object.FindObjectsByType<MeshFilter>()
            .Single(f => f.sharedMesh && f.sharedMesh.name == "ContainerScreen").transform;
        var group = screen.parent;
        if (group.childCount != 4 || Mathf.Abs(screen.position.x-25.5f) > .02f)
            throw new InvalidOperationException("The original waste enclosure group could not be identified.");
        group.gameObject.SetActive(false);
        var pilot = UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        if (!pilot || !pilot.containerApproach)
            throw new InvalidOperationException("Missing original waste approach anchor.");
        pilot.containerApproach.position = new Vector3(Center, level+Rise, Mouth+10);
        EditorUtility.SetDirty(group.gameObject);
        EditorUtility.SetDirty(pilot.containerApproach);
        return new List<string> { "Ограждение, табличка и контейнеры отключены. Плита и бордюр убраны из покрытий двора." };
    }

    private static IEnumerable<string> SeatProps(Transform root, Mesh court)
    {
        var lamp = UnityEngine.Object.FindObjectsByType<MeshFilter>().Single(f => f.sharedMesh &&
            f.sharedMesh.name == "CourtyardLamp" && Vector2.Distance(new Vector2(f.transform.position.x,f.transform.position.z),
                new Vector2(21,-34)) < .10f).transform;
        const float newX = 19.995f;
        var old = lamp.position;
        var support = root.GetComponentsInChildren<MeshCollider>().Single(c => c.sharedMesh == court);
        Physics.SyncTransforms();
        if (!support.Raycast(new Ray(new Vector3(newX,3,old.z),Vector3.down),out var hit,4))
            throw new InvalidOperationException("The retained courtyard lamp has no surface support.");
        lamp.position = new Vector3(newX,hit.point.y,old.z);
        EditorUtility.SetDirty(lamp);
        yield return $"Дворовый фонарь: {old:F3} → {lamp.position:F3}";
    }

    private static string[] CutOldSurfaces(Scene scene, MeshCollider[] ground, Result result)
    {
        var affected = new List<string>();
        var originalGround = new HashSet<MeshCollider>(ground);
        var handled = new HashSet<MeshCollider>();
        var filterList = UnityEngine.Object.FindObjectsByType<MeshFilter>()
            .Where(f => f.gameObject.scene == scene && f.sharedMesh && f.sharedMesh.isReadable &&
                f.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled && NearSite(renderer.bounds))
            .OrderBy(f => f.name,StringComparer.Ordinal).ThenBy(f => f.transform.position.x).ToArray();

        Mesh Recut(Mesh source,Transform t)
        {
            Mesh current = GarageSurfaceCut.Clip(source,t.localToWorldMatrix,Footprint,out int total);
            if (!current) return null;
            result.alteredTriangles += total;
            string originalPath = AssetDatabase.GetAssetPath(source);
            affected.Add($"{originalPath} · {t.name} · {total} пересечённых треугольников");
            return Save(current,$"Retained_{affected.Count:00}_{source.name}");
        }

        foreach (var filter in filterList)
        {
            Mesh source = filter.sharedMesh;
            string path = AssetDatabase.GetAssetPath(source);
            var renderer = filter.GetComponent<MeshRenderer>();
            if (path == MainRoadMesh || path.StartsWith(Output + "/",StringComparison.Ordinal) ||
                renderer.bounds.max.y > 1f) continue;
            bool lowDetail = path.StartsWith("Assets/Art/PaintedDistrict/Leaves/",StringComparison.Ordinal) ||
                path.StartsWith("Assets/Art/AutumnGrass/Cell_",StringComparison.Ordinal) ||
                source.name.StartsWith("WoodlandFloor_",StringComparison.Ordinal);
            if (!GroundMesh(path) && !lowDetail) continue;
            var replacement = Recut(source,filter.transform);
            if (!replacement) continue;
            filter.sharedMesh = replacement;
            EditorUtility.SetDirty(filter);
            foreach (var collider in filter.GetComponents<MeshCollider>())
                if (collider.sharedMesh == source && originalGround.Contains(collider))
                {
                    collider.sharedMesh = replacement;
                    handled.Add(collider);
                    EditorUtility.SetDirty(collider);
                }
        }
        foreach (var collider in ground.OrderBy(c => c.name,StringComparer.Ordinal))
        {
            if (handled.Contains(collider) || AssetDatabase.GetAssetPath(collider.sharedMesh) == MainRoadMesh) continue;
            var replacement = Recut(collider.sharedMesh,collider.transform);
            if (!replacement) continue;
            collider.sharedMesh = replacement;
            EditorUtility.SetDirty(collider);
        }
        return affected.ToArray();
    }
}
