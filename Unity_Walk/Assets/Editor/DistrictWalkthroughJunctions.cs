using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class DistrictWalkthroughJunctions
{
    private const string Root = "Assets/PhotoReconstruction/Stage13Junctions";
    private const string MeshFolder = Root + "/Meshes";
    private const string AssetPath = MeshFolder + "/WalkthroughApproachCurb.asset";
    private const string BlendName = "Двор №7 · плавный край серой площади";

    internal static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before repairing the junctions.");
        var curb = Surface("Низкие кромки скруглённых подходов");
        var paving = Surface("Скруглённое мощение · 1");
        var plaza = Surface("Серое прямоугольное мощение · внутренний двор");
        var driveway = Surface("Объединённый асфальт проездов и въездов");
        BlendCentralPlaza(plaza, paving);
        var filter = curb.GetComponent<MeshFilter>();
        if (AssetDatabase.GetAssetPath(filter.sharedMesh) == AssetPath)
            return;
        if (filter.sharedMesh.subMeshCount != 1)
            throw new InvalidOperationException("The curb is no longer the inspected single-material mesh.");
        Physics.SyncTransforms();

        // Both measured patches extend past an intact planar end cap. Keep the
        // existing cap and remove only the clipped hook and the tapered remnant.
        var central = Rect.MinMaxRect(26.735f, 45.335f, 27.194f, 45.696f);
        var east = Rect.MinMaxRect(210.217f, 50.167f, 210.470f, 50.361f);
        var points = filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
        var indices = filter.sharedMesh.GetTriangles(0);
        var retained = new List<int>(indices.Length);
        int centerCuts = 0, eastCuts = 0;
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = points[indices[i]], b = points[indices[i + 1]], c = points[indices[i + 2]];
            if (Mathf.Min(a.x, b.x, c.x) < 27.05f && Inside(central, a, b, c))
                centerCuts++;
            else if (Mathf.Max(a.x, b.x, c.x) > 210.26f && Inside(east, a, b, c))
                eastCuts++;
            else
            {
                retained.Add(indices[i]);
                retained.Add(indices[i + 1]);
                retained.Add(indices[i + 2]);
            }
        }
        var clearances = new[] {
            (point: new Vector2(27.13693f, 45.62923f), floor: plaza, low: .13f),
            (point: new Vector2(210.44382f, 50.33551f), floor: paving, low: .035f)
        };
        var eastCap = new Vector2(210.20634f, 50.19150f);
        foreach (var sample in clearances)
            if (!Cast(sample.floor, sample.point, out var hit) || hit.point.y < sample.low)
                throw new InvalidOperationException($"No saved pavement below the stray curb at {sample.point:F4}.");
        // A subsequent entrance pass may clone the shared curb mesh again.
        // In that case verify the local geometry instead of restoring our copy.
        if (centerCuts == 0 && eastCuts == 0 && Cast(curb, eastCap, out _) &&
            !clearances.Any(sample => Cast(curb, sample.point, out _)))
            return;
        if (centerCuts != 5 || eastCuts != 30)
            throw new InvalidOperationException($"Expected 5 central and 30 eastern remnant triangles, found {centerCuts}/{eastCuts}.");
        var routePoints = new[] { new Vector2(28.8f, 43.2f), new Vector2(27.1f, 44.9f),
            new Vector2(209.95f, 51.2f), new Vector2(211.35f, 51.2f) };
        var hardscape = new[] { paving, plaza, driveway };
        var before = routePoints.Select(p => SupportHeight(p, hardscape)).ToArray();
        if (before.Any(y => !float.IsFinite(y)))
            throw new InvalidOperationException("A saved junction crossing lacks hardscape support.");

        Directory.CreateDirectory(MeshFolder);
        AssetDatabase.Refresh();
        var original = filter.sharedMesh;
        var mesh = UnityEngine.Object.Instantiate(original);
        mesh.name = "WalkthroughApproachCurb";
        mesh.SetTriangles(retained, 0);
        Assign(filter, curb, mesh);
        Physics.SyncTransforms();
        foreach (var sample in clearances)
            if (Cast(curb, sample.point, out _))
                throw new InvalidOperationException($"A curb triangle still covers the pavement at {sample.point:F4}.");
        for (int i = 0; i < routePoints.Length; i++)
            if (Mathf.Abs(SupportHeight(routePoints[i], hardscape) - before[i]) > .001f)
                throw new InvalidOperationException($"Hardscape height changed on the route at {routePoints[i]:F4}.");
        if (!Cast(curb, eastCap, out _))
            throw new InvalidOperationException("The retained east curb cap lost its approach edge.");

        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(AssetPath);
        if (saved)
        {
            EditorUtility.CopySerialized(mesh, saved);
            Assign(filter, curb, saved);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(saved);
        }
        else
            AssetDatabase.CreateAsset(mesh, AssetPath);
        EditorUtility.SetDirty(filter);
        EditorUtility.SetDirty(curb);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"WALKTHROUGH_JUNCTIONS centerCurb={centerCuts} eastCurb={eastCuts} unchangedRouteSupports={routePoints.Length}");
    }

    private static void BlendCentralPlaza(MeshCollider plaza, MeshCollider paving)
    {
        if (GameObject.Find(BlendName))
            return;
        Physics.SyncTransforms();
        var renderer = plaza.GetComponent<MeshRenderer>();
        var filter = plaza.GetComponent<MeshFilter>();
        var materials = renderer.sharedMaterials;
        if (materials.Length != 1 || materials[0].name != "CourtGreyRectangularPavers")
            throw new InvalidOperationException("The inspected plaza shoulder must have grey paving.");

        // The Stage04 edge is straight in plan, z = 46.60 + (x - 30)/3.
        // Match the adjoining red slab by fading the existing grey profile up
        // within that footprint. Do not extend paving into the lawn.
        const float start = 30.0f, end = 31.0f, width = .60f;
        const int lengthCells = 25, widthCells = 8;
        var points = new Vector3[lengthCells + 1, widthCells + 1];
        var mesh = new EnvironmentMesh();
        float maxChange = 0;
        for (int i = 0; i <= lengthCells; i++)
        {
            float x = Mathf.Lerp(start, end, (float)i / lengthCells);
            float edge = 46.60f + (x - start) / 3 - .001f;
            float weight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, 30.25f, x)) *
                (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(30.54f, end, x)));
            // East of the contact, sample the last section of red paving that
            // still touches the grey slab, then fade to the grey edge again.
            float joinX = Mathf.Min(x, 30.54f);
            float joinZ = 46.60f + (joinX - start) / 3 + .015f;
            if (!Cast(paving, new Vector2(joinX, joinZ), out var red) || red.normal.y < .95f)
                throw new InvalidOperationException($"The red route has lost the central grade anchor at {joinX:F3}.");
            if (!Cast(plaza, new Vector2(x, edge), out var grey))
                throw new InvalidOperationException($"The saved plaza is missing its edge at {x:F3}.");
            float lift = Mathf.Max(0, red.point.y - grey.point.y) * weight;
            maxChange = Mathf.Max(maxChange, lift);
            for (int j = 0; j <= widthCells; j++)
            {
                float fraction = (float)j / widthCells;
                float z = edge - width * fraction;
                if (!Cast(plaza, new Vector2(x, z), out var original))
                    throw new InvalidOperationException($"The plaza grade transition cannot match the saved floor at {x:F3}, {z:F3}.");
                points[i, j] = new Vector3(x,
                    original.point.y + lift * (1 - Mathf.SmoothStep(0, 1, fraction)), z);
            }
        }
        if (maxChange < .035f || maxChange > .10f)
            throw new InvalidOperationException($"The measured plaza drop is outside the expected local range: {maxChange:F3} m.");
        for (int i = 0; i < lengthCells; i++)
        for (int j = 0; j < widthCells; j++)
        {
            var a = points[i, j];
            var b = points[i + 1, j];
            var c = points[i + 1, j + 1];
            var d = points[i, j + 1];
            AddFloorTriangle(mesh, materials[0], a, b, c);
            AddFloorTriangle(mesh, materials[0], a, c, d);
        }

        var outline = new List<Vector2> {
            XZ(points[0, widthCells]), XZ(points[lengthCells, widthCells]),
            XZ(points[lengthCells, 0]), XZ(points[0, 0])
        };
        Directory.CreateDirectory(MeshFolder);
        AssetDatabase.Refresh();
        var old = filter.sharedMesh;
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(
            new[] { filter }, new List<List<Vector2>> { outline }, Root);
        if (old == filter.sharedMesh || plaza.sharedMesh != filter.sharedMesh)
            throw new InvalidOperationException("The corrected centre grade overlaps the saved grey floor.");
        var obj = mesh.Create("WalkthroughCentralPlazaJoin", BlendName, Root);
        obj.transform.SetParent(plaza.transform.parent, true);
        var support = obj.AddComponent<MeshCollider>();
        support.sharedMesh = obj.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        var surface = obj.AddComponent<SavedPavedSurface>();
        surface.existingNetworkDestination = true;
        surface.purpose = "Smooth local transition at the existing central grey paving edge";
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if (access && access.surfaces != null)
        {
            access.surfaces = access.surfaces.Append(support).ToArray();
            EditorUtility.SetDirty(access);
        }
        Physics.SyncTransforms();
        foreach (int i in Enumerable.Range(1, lengthCells - 1))
        {
            Vector3 point = (points[i, 0] + points[i, 1]) * .5f;
            if (!Cast(support, XZ(point), out var top) || Mathf.Abs(top.point.y - point.y) > .003f ||
                Cast(plaza, XZ(point), out _))
                throw new InvalidOperationException($"The central transition is discontinuous or stacked at {point:F3}.");
        }
        Debug.Log($"WALKTHROUGH_JUNCTIONS centralGreyBlend={maxChange:F4} m continuous={lengthCells - 1}");
    }

    private static void AddFloorTriangle(EnvironmentMesh mesh, Material material, Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = Vector3.Cross(b - a, c - a).normalized;
        if (normal.y < .89f)
            throw new InvalidOperationException($"The local centre shoulder becomes too steep at {a:F3}.");
        mesh.Face(material, normal, a, b, c);
    }

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);

    private static bool Inside(Rect bounds, Vector3 a, Vector3 b, Vector3 c) =>
        bounds.Contains(new Vector2(a.x, a.z)) && bounds.Contains(new Vector2(b.x, b.z)) &&
        bounds.Contains(new Vector2(c.x, c.z));

    private static float SupportHeight(Vector2 point, MeshCollider[] surfaces)
    {
        float height = float.NegativeInfinity;
        foreach (var surface in surfaces)
            if (Cast(surface, point, out var hit) && hit.normal.y > .82f)
                height = Mathf.Max(height, hit.point.y);
        return height;
    }

    private static bool Cast(MeshCollider surface, Vector2 point, out RaycastHit hit) =>
        surface.Raycast(new Ray(new Vector3(point.x, .6f, point.y), Vector3.down), out hit, .95f);

    private static MeshCollider Surface(string name)
    {
        var target = GameObject.Find(name);
        var collider = target ? target.GetComponent<MeshCollider>() : null;
        var filter = target ? target.GetComponent<MeshFilter>() : null;
        if (!collider || !collider.enabled || !filter || !filter.sharedMesh || collider.sharedMesh != filter.sharedMesh)
            throw new InvalidOperationException($"The saved surface '{name}' must share its renderer and collision mesh.");
        return collider;
    }

    private static void Assign(MeshFilter filter, MeshCollider collider, Mesh mesh)
    {
        filter.sharedMesh = mesh;
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
    }
}
