using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class DistrictDriveSeams
{
    private const string Root = "Assets/PhotoReconstruction/Stage12Drive";
    private const string AsphaltName = "Объединённый асфальт проездов и въездов";

    internal static void Apply()
    {
        var asphalt = Surface(AsphaltName);
        if (AssetDatabase.GetAssetPath(asphalt.sharedMesh).StartsWith(Root + "/", StringComparison.Ordinal))
            return;
        var road = Surface("Асфальт, парковочные уширения и корона дороги");
        var paving = Surface("Скруглённое мощение · 1");
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        Physics.SyncTransforms();

        var filter = asphalt.GetComponent<MeshFilter>();
        // Use the measured eastern edge of ReferenceRoad as the clipping plane.
        var edge = new Vector3(105.2338f, 0, -210.8762f);
        var along = new Vector3(103.8297f, 0, -208.3619f) - edge;
        var roadFrame = Matrix4x4.TRS(edge, Quaternion.LookRotation(along), Vector3.one);
        var overlap = Rect.MinMaxRect(-10.35f, -1.3f, 0, 4.6f);
        var southernProbes = new List<(Vector3 point, float height)>();
        for (float x = -10.45f; x <= .15f; x += .10f)
        for (float z = -1.4f; z <= 4.7f; z += .10f)
        {
            Vector3 point = roadFrame.MultiplyPoint3x4(new Vector3(x, 0, z));
            if (Highest(asphalt, road, point, out float height))
                southernProbes.Add((point, height));
        }
        var clipped = PhotoCourtSeamRepairStage05.ClipOverPaving(filter, road, overlap, roadFrame.inverse, true,
            out int southernTriangles);
        if (!clipped || southernTriangles == 0)
            throw new InvalidOperationException("The measured southern asphalt overlap is absent.");
        // The east pass persists the final mesh. Keep the intermediate copy in memory.
        Assign(filter, asphalt, clipped);
        Physics.SyncTransforms();

        var replacedEdge = new[] { new Vector3(99.2072f, 0, -214.7429f), new Vector3(105.15f, 0, -210.866f) };
        for (int step = 0; step <= 50; step++)
        {
            Vector3 point = Vector3.Lerp(replacedEdge[0], replacedEdge[1], step / 50f);
            if (!Cast(road, point, out var support) ||
                (Cast(asphalt, point, out var oldTop) && oldTop.point.y > support.point.y + .002f))
                throw new InvalidOperationException($"The southern asphalt lip still protrudes at {point:F3}.");
        }
        float maxSouthernChange = 0;
        foreach (var (point, oldHeight) in southernProbes)
        {
            if (!Highest(asphalt, road, point, out float newHeight))
                throw new InvalidOperationException($"The southern approach lost hard support at {point:F3}.");
            maxSouthernChange = Mathf.Max(maxSouthernChange, Mathf.Abs(oldHeight - newHeight));
        }
        if (maxSouthernChange > .04f)
            throw new InvalidOperationException($"The southern road edit changed a saved approach by {maxSouthernChange:F3} m.");
        CheckSouthernEdge(asphalt, road);

        int noseTriangles = TrimEastNose(paving, asphalt);
        int curbTriangles = TrimEastCurb(paving);

        var areas = new[] { Rect.MinMaxRect(211.3f, 31.4f, 213.15f, 34.5f),
            Rect.MinMaxRect(207.5f, 50f, 212f, 53.8f) };
        var polygons = new List<List<Vector2>>();
        var eastProbes = new List<Vector3>();
        var vertices = paving.sharedMesh.vertices.Select(paving.transform.TransformPoint).ToArray();
        var indices = paving.sharedMesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = vertices[indices[i]], b = vertices[indices[i + 1]], c = vertices[indices[i + 2]];
            if (!areas.Any(area => area.Contains(XZ(a)) && area.Contains(XZ(b)) && area.Contains(XZ(c))))
                continue;
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.y < .00006f || normal.normalized.y < .9f)
                continue;
            var center = (a + b + c) / 3;
            var samples = new[] { center, Vector3.Lerp(center, a, .55f),
                Vector3.Lerp(center, b, .55f), Vector3.Lerp(center, c, .55f) };
            var covered = samples.Where(point => Cast(asphalt, point, out var hit)
                && Mathf.Abs(hit.point.y - point.y) <= .018f).ToArray();
            if (covered.Length == 0)
                continue;
            polygons.Add(new List<Vector2> { XZ(a), XZ(c), XZ(b) });
            eastProbes.AddRange(covered);
        }
        if (polygons.Count == 0)
            throw new InvalidOperationException("The measured eastern paving overlaps are absent.");
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(new[] { filter }, polygons, Root);
        if (filter.sharedMesh == clipped || filter.sharedMesh != asphalt.sharedMesh)
            throw new InvalidOperationException("The finished renderer and collider need the same new asphalt mesh.");
        UnityEngine.Object.DestroyImmediate(clipped);
        Physics.SyncTransforms();
        foreach (var point in eastProbes)
        {
            bool hasPaving = Cast(paving, point, out var support);
            bool hasAsphalt = Cast(asphalt, point, out var oldTop);
            if (!hasPaving || Mathf.Abs(support.point.y - point.y) > .002f || hasAsphalt)
                throw new InvalidOperationException($"East driveway joint at {point:F5}: " +
                    $"paving={hasPaving} ({support.point.y:F5}), asphalt={hasAsphalt} ({oldTop.point.y:F5}).");
        }
        Debug.Log($"DISTRICT_STAGE12_DRIVE southernTriangles={southernTriangles} southernProbes={southernProbes.Count} " +
            $"maxSouthernChange={maxSouthernChange:F4} eastNose={noseTriangles} eastCurb={curbTriangles} " +
            $"easternPaverTriangles={polygons.Count} easternProbes={eastProbes.Count}");
    }

    private static void CheckSouthernEdge(MeshCollider asphalt, MeshCollider road)
    {
        // Inspect the repaired cut at a shallow angle, which detects height mismatches
        // between adjacent triangles that vertical ground probes cannot see.
        var start = new Vector3(101.2f, .06f, -212.1f);
        var end = new Vector3(102.5f, .03f, -214.2f);
        Vector3 eye = end + Vector3.up * 1.6f + (end - start).normalized * 1.25f;
        Quaternion aim = Quaternion.LookRotation(start - eye);
        float scale = Mathf.Tan(27.5f * Mathf.Deg2Rad);
        foreach (var pixel in new[] { new Vector2(1235, 257), new Vector2(1260, 269), new Vector2(1312, 294) })
        {
            Vector3 direction = aim * new Vector3((pixel.x / 800 - 1) * scale * 1600 / 900,
                (1 - pixel.y / 450) * scale, 1);
            var ray = new Ray(eye, direction);
            if (!asphalt.Raycast(ray, out _, 20) && !road.Raycast(ray, out _, 20))
                throw new InvalidOperationException($"The southern asphalt seam has a visible gap at {pixel}.");
        }
    }

    private static int TrimEastNose(MeshCollider paving, MeshCollider asphalt)
    {
        var filter = paving.GetComponent<MeshFilter>();
        var corner = new Vector3(210.3524f, 0, 50.7860f);
        var before = new Vector3(212.0076f, 0, 47.4809f);
        var after = new Vector3(208.0601f, 0, 53.7865f);
        var oldBulge = new Vector3(210.5846f, .0449f, 50.6086f);
        if (!Cast(paving, oldBulge, out _) || !Cast(asphalt, oldBulge, out _))
            throw new InvalidOperationException("The rounded eastern paving tip has lost its saved asphalt support.");
        int cuts = 0;
        var original = filter.sharedMesh;
        foreach (var section in new[] {
            (start: before, end: corner, from: Vector3.Distance(before, corner) - .97f, to: Vector3.Distance(before, corner)),
            (start: corner, end: after, from: 0f, to: 1.86f)
        })
        {
            var frame = Matrix4x4.TRS(section.start, Quaternion.LookRotation(section.end - section.start), Vector3.one);
            var bounds = Rect.MinMaxRect(.006f, section.from, .93f, section.to);
            var replacement = PhotoCourtSeamRepairStage05.ClipOverPaving(filter, asphalt, bounds, frame.inverse, true,
                out int trimmed);
            if (!replacement)
                continue;
            var previous = filter.sharedMesh;
            Assign(filter, paving, replacement);
            if (previous != original)
                UnityEngine.Object.DestroyImmediate(previous);
            Physics.SyncTransforms();
            cuts += trimmed;
        }
        // The two perpendicular end planes leave a small wedge at the bend.
        // Its entire footprint sits inside the saved asphalt.
        var bend = Rect.MinMaxRect(210.366f, 50.79f, 210.76f, 50.98f);
        var bendMesh = PhotoCourtSeamRepairStage05.ClipOverPaving(filter, asphalt, bend, out int bendCuts);
        if (bendMesh)
        {
            var previous = filter.sharedMesh;
            Assign(filter, paving, bendMesh);
            if (previous != original)
                UnityEngine.Object.DestroyImmediate(previous);
            Physics.SyncTransforms();
            cuts += bendCuts;
        }
        if (cuts == 0 || Cast(paving, oldBulge, out _))
            throw new InvalidOperationException("The round paving extension is still visible in the eastern drive.");
        foreach (var point in new[] { new Vector3(210.4867f, 0, 50.8590f), new Vector3(210.5264f, 0, 50.8965f) })
            if (Cast(paving, point, out _) || !Cast(asphalt, point, out _))
                throw new InvalidOperationException($"The eastern bend still has an overlap or a gap at {point:F4}.");
        SaveCurrent(filter, paving, "EastApproachPaving");
        return cuts;
    }

    private static int TrimEastCurb(MeshCollider paving)
    {
        var curb = Surface("Низкие кромки скруглённых подходов");
        var filter = curb.GetComponent<MeshFilter>();
        var bounds = Rect.MinMaxRect(210.36f, 50.28f, 210.59f, 50.49f);
        var trimmed = PhotoCourtSeamRepairStage05.ClipOverPaving(filter, paving, bounds, out int cuts);
        if (trimmed)
        {
            Assign(filter, curb, trimmed);
            SaveCurrent(filter, curb, "EastDriveApproachCurb");
            Physics.SyncTransforms();
        }
        return cuts;
    }

    private static void SaveCurrent(MeshFilter filter, MeshCollider collider, string name)
    {
        var mesh = filter.sharedMesh;
        mesh.name = name;
        string path = Root + "/Meshes/" + name + ".asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!saved)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return;
        }
        EditorUtility.CopySerialized(mesh, saved);
        Assign(filter, collider, saved);
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    private static MeshCollider Surface(string name)
    {
        var target = GameObject.Find(name);
        var collider = target ? target.GetComponent<MeshCollider>() : null;
        var filter = target ? target.GetComponent<MeshFilter>() : null;
        if (!collider || !filter || !collider.enabled || !filter.sharedMesh || collider.sharedMesh != filter.sharedMesh)
            throw new InvalidOperationException($"'{name}' must share its rendering and collision mesh.");
        return collider;
    }

    private static void Assign(MeshFilter filter, MeshCollider collider, Mesh mesh)
    {
        filter.sharedMesh = mesh;
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
    }

    private static bool Highest(MeshCollider first, MeshCollider second, Vector3 point, out float height)
    {
        bool onFirst = Cast(first, point, out var a);
        bool onSecond = Cast(second, point, out var b);
        height = Mathf.Max(onFirst ? a.point.y : float.NegativeInfinity, onSecond ? b.point.y : float.NegativeInfinity);
        return onFirst || onSecond;
    }

    private static bool Cast(MeshCollider floor, Vector3 point, out RaycastHit hit) =>
        floor.Raycast(new Ray(new Vector3(point.x, .6f, point.z), Vector3.down), out hit, .9f) && hit.normal.y > .82f;

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
}
