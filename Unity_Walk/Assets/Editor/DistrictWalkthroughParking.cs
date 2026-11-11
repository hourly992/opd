using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class DistrictWalkthroughParking
{
    internal static void RefreshSouthApron()
    {
        // Only the five samples measured on the rebuilt No.15 apron.
        var surveyed = new[]
        {
            new Vector2(102.3870f, -208.5480f), new Vector2(102.3870f, -207.2980f),
            new Vector2(103.6370f, -211.0480f), new Vector2(103.6370f, -209.7980f),
            new Vector2(103.6370f, -208.5480f)
        };
        bool Matches(Vector3 point, Vector2 target) =>
            (new Vector2(point.x, point.z) - target).sqrMagnitude < .002f * .002f;

        var site = UnityEngine.Object.FindObjectsByType<ParkingSite>().Single(parking =>
            parking.address == "ул. Дунина-Горкавича, 15" &&
            surveyed.All(target => parking.surfaceChecks.Count(point => Matches(point, target)) == 1));
        var road = site.surfaces.Single(collider => collider && collider.enabled && collider.sharedMesh &&
            collider.sharedMesh.name == "ReferenceRoad");
        var filter = road.GetComponent<MeshFilter>();
        if (!filter || filter.sharedMesh != road.sharedMesh)
            throw new InvalidOperationException("The No.15 apron must use the same asphalt mesh for render and physics.");

        Physics.SyncTransforms();
        var probes = (Vector3[])site.surfaceChecks.Clone();
        float largestChange = 0;
        int updated = 0;
        foreach (var target in surveyed)
        {
            int index = Array.FindIndex(probes, point => Matches(point, target));
            Vector3 point = probes[index];
            var ray = new Ray(point + Vector3.up * 1.5f, Vector3.down);
            if (!road.Raycast(ray, out var hit, 2) || hit.normal.y < .98f || hit.point.y < .025f || hit.point.y > .033f)
                throw new InvalidOperationException($"Expected the surveyed asphalt plane at {point:F4}.");
            float change = Mathf.Abs(point.y - hit.point.y);
            if (change < .0001f) continue;
            if (Mathf.Abs(point.y - .065f) > .001f || change > .038f)
                throw new InvalidOperationException($"Unexpected baseline level at the No.15 apron probe {point:F4}.");
            largestChange = Mathf.Max(largestChange, change);
            probes[index].y = hit.point.y;
            updated++;
        }
        site.surfaceChecks = probes;
        EditorUtility.SetDirty(site);
        PrefabUtility.RecordPrefabInstancePropertyModifications(site);

        int checks = 0;
        foreach (var parking in UnityEngine.Object.FindObjectsByType<ParkingSite>())
        foreach (Vector3 point in parking.surfaceChecks.Concat(parking.accessChecks))
        {
            var ray = new Ray(point + Vector3.up * 1.5f, Vector3.down);
            if (!parking.surfaces.Any(collider => collider && collider.Raycast(ray, out var hit, 2) &&
                hit.normal.y > .85f && Mathf.Abs(hit.point.y - point.y) < .035f))
                throw new InvalidOperationException($"Parking support failed at {parking.address}, {point:F4}.");
            checks++;
        }
        Debug.Log($"STAGE13_PARKING updated={updated} largestChange={largestChange:F4} supportedProbes={checks}");
    }
}
