using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class DistrictEntrancePavingRepairs
{
    private const string AssetRoot = "Assets/PhotoReconstruction/Stage15EntrancePaving";
    private const string ReportPath = "street_qa/district_entrances/paving.tsv";
    private const float Interval = .05f;
    private const float MaximumApproachLength = 5f;
    private static readonly float[] Lanes = { -.45f, 0, .45f };

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if (scene.path != "Assets/Scenes/Street.unity" || !access)
            throw new InvalidOperationException("Open the saved street before repairing entrance paving.");

        Physics.SyncTransforms();
        RepairNorthApron();
        SurveyApproaches(access);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void RepairNorthApron()
    {
        const string repairedName = "House13PavingOutsideConcreteApron";
        var red = GameObject.Find("Скруглённое мощение · 1")?.GetComponent<MeshCollider>();
        var apron = GameObject.Find("№13 · бетонная площадка за ступенями")?.GetComponent<MeshCollider>();
        if (!red || !apron || red.GetComponent<MeshFilter>().sharedMesh != red.sharedMesh)
            throw new InvalidOperationException("The No.13 concrete and red paving surfaces are required.");
        if (red.sharedMesh.name == repairedName)
            return;

        // Camera rays through the visible red triangle intersect the former
        // walkway at .021 m and the separate concrete apron at .018 m.
        var witnesses = new[]
        {
            new Vector3(167.454f, 0, 97.918f), new Vector3(167.463f, 0, 97.973f),
            new Vector3(167.620f, 0, 97.964f)
        };
        foreach (var point in witnesses)
            if (!Cast(apron, point, out var floor) || !Cast(red, point, out var paver) ||
                Mathf.Abs(floor.point.y - .018f) > .002f || paver.point.y - floor.point.y < .001f ||
                paver.point.y - floor.point.y > .008f)
                throw new InvalidOperationException($"The old red paving overlap moved at {point:F3}.");

        // The original sloping red approach starts outside this clipping box.
        // The helper also tests support on the actual concrete, so the cut is
        // confined to the overlap and cannot remove an unsupported path.
        var filter = red.GetComponent<MeshFilter>();
        var bounds = Rect.MinMaxRect(164.916f, 96.397f, 169.004f, 98.137f);
        var trimmed = PhotoCourtSeamRepairStage05.ClipOverPaving(filter, apron, bounds, out int count);
        if (!trimmed || count == 0)
            throw new InvalidOperationException("Could not clear the obsolete red paving from the No.13 apron.");

        Directory.CreateDirectory(AssetRoot + "/Meshes");
        AssetDatabase.Refresh();
        trimmed.name = repairedName;
        string path = $"{AssetRoot}/Meshes/{repairedName}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh)
        {
            EditorUtility.CopySerialized(trimmed, mesh);
            UnityEngine.Object.DestroyImmediate(trimmed);
        }
        else
        {
            mesh = trimmed;
            AssetDatabase.CreateAsset(mesh, path);
        }
        filter.sharedMesh = mesh;
        red.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);
        EditorUtility.SetDirty(red);
        Physics.SyncTransforms();

        if (witnesses.Any(point => Cast(red, point, out _) || !Cast(apron, point, out _)))
            throw new InvalidOperationException("The No.13 apron still exposes the old paver or has lost support.");
        Debug.Log($"DISTRICT_ENTRANCE_PAVING_REPAIR address=13 clippedTriangles={count} retainedApronProbes={witnesses.Length}");
    }

    private static void SurveyApproaches(ResidentialAccess access)
    {
        var surfaces = access.surfaces.Where(surface => surface && surface.enabled && surface.gameObject.activeInHierarchy)
            .Distinct().Select(surface => (surface, surface.bounds)).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        using var report = File.CreateText(ReportPath);
        report.WriteLine("address\tprobes\tsupported\tmaxAdjacentRiseM\tjunctionsOver5cm");
        int checkedRoutes = 0, probes = 0, supported = 0, abrupt = 0;
        foreach (var route in access.routes)
        {
            if (route.points == null || route.points.Length < 2)
                continue;
            int routeProbes = 0, routeSupported = 0, routeAbrupt = 0;
            float distance = 0, maximumRise = 0;
            for (int i = 1; i < route.points.Length && distance < MaximumApproachLength; i++)
            {
                Vector3 a = route.points[i - 1], b = route.points[i];
                Vector3 forward = Vector3.ProjectOnPlane(b - a, Vector3.up);
                float length = forward.magnitude;
                if (length < .001f)
                    continue;
                Vector3 right = Vector3.Cross(Vector3.up, forward / length);
                float span = Mathf.Min(length, MaximumApproachLength - distance);
                int samples = Mathf.CeilToInt(span / Interval);
                foreach (float lane in Lanes)
                {
                    float? previous = null;
                    for (int sample = 0; sample <= samples; sample++)
                    {
                        Vector3 point = Vector3.Lerp(a, b, span * sample / samples / length) + right * lane;
                        float highest = float.NegativeInfinity;
                        foreach (var (surface, box) in surfaces)
                        {
                            if (point.x < box.min.x || point.x > box.max.x || point.z < box.min.z || point.z > box.max.z ||
                                !Cast(surface, point, out var hit) || hit.point.y < point.y - .25f ||
                                hit.point.y > point.y + .23f)
                                continue;
                            highest = Mathf.Max(highest, hit.point.y);
                        }
                        routeProbes++;
                        if (float.IsNegativeInfinity(highest))
                        {
                            previous = null;
                            continue;
                        }
                        routeSupported++;
                        if (previous.HasValue)
                        {
                            float rise = Mathf.Abs(highest - previous.Value);
                            maximumRise = Mathf.Max(maximumRise, rise);
                            if (rise > .05f)
                                routeAbrupt++;
                        }
                        previous = highest;
                    }
                }
                distance += length;
            }
            checkedRoutes++;
            probes += routeProbes;
            supported += routeSupported;
            abrupt += routeAbrupt;
            report.WriteLine(FormattableString.Invariant($"{route.address}\t{routeProbes}\t{routeSupported}\t{maximumRise:F4}\t{routeAbrupt}"));
        }
        Debug.Log($"DISTRICT_ENTRANCE_PAVING_SURVEY routes={checkedRoutes} supported={supported}/{probes} adjacentJumpsOver5cm={abrupt}");
    }

    private static bool Cast(MeshCollider surface, Vector3 point, out RaycastHit hit) =>
        surface.Raycast(new Ray(new Vector3(point.x, point.y + 1f, point.z), Vector3.down), out hit, 1.4f) &&
        hit.normal.y > .78f;
}
