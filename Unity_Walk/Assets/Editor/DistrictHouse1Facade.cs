using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictHouse1Facade
{
    private const string Root = "Assets/PhotoReconstruction/Stage12North";
    private const string GroupName = "Север · фронтон и входная площадка №1";
    private const string Panorama = "https://yandex.ru/maps/57/khanty-mansiysk/house/ulitsa_dunina_gorkavicha_1/YkAYdgFgTkADQFhrfXxxcH1lYA==/panorama/?panorama%5Bpoint%5D=69.060451%2C61.001011&panorama%5Bdirection%5D=146.460156%2C-13.047619";
    private const float OldWall = .36f, OldWallBand = 6.55f, OldWallTop = 7.65f;
    private const float PlatformHalfWidth = 1.57f, PlatformBack = .445f, PlatformFront = 2.23f;
    private const float RailSide = 1.44f, RailHeight = .86f;
    private static readonly Vector2 Oculus = new Vector2(0, 7.74f);
    private const float OculusRadius = .255f;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before editing the No.1 frontage.");
        if (GameObject.Find(GroupName)) return;

        var house = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(item => item.address == "1");
        var entry = house.transform.Cast<Transform>().Single(item => item.name.StartsWith("Access ·", StringComparison.Ordinal));
        Quaternion orientation = entry.rotation;
        Matrix4x4 front = Matrix4x4.TRS(entry.position - entry.forward * 1.27f, orientation, Vector3.one);
        Vector3 P(float x, float y, float z) => front.MultiplyPoint3x4(new Vector3(x, y, z));
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var route = access.routes.Single(item => item.address.StartsWith("1 ", StringComparison.Ordinal)).points;
        var lodGroup = house.GetComponent<LODGroup>();
        var lods = lodGroup.GetLODs();
        if (lods.Length != 3 || lods.Any(lod => lod.renderers.Length != 1) ||
            route.Length < 4 || Vector2.Distance(XZ(route[0]), XZ(entry.position)) > .05f)
            throw new InvalidOperationException("The saved No.1 frontage no longer matches its geometry and approach.");

        // Preflight against the saved meshes and ground before writing assets or changing the scene.
        var filters = lods.Select(lod => lod.renderers[0].GetComponent<MeshFilter>()).ToArray();
        var bases = filters.Select(filter => WithoutOldFronton(filter, front.inverse)).ToArray();
        Physics.SyncTransforms();
        float floor = Mathf.Max(PavedHeight(access, P(0, 0, 1.32f)), PavedHeight(access, P(0, 0, PlatformFront))) + .008f;
        float underside = float.PositiveInfinity;
        foreach (float x in new[] { -PlatformHalfWidth + .07f, 0, PlatformHalfWidth - .07f })
            foreach (float z in new[] { PlatformBack + .08f, 1.32f, PlatformFront - .06f })
            {
                Vector3 at = P(x, 0, z);
                if (!Physics.Raycast(at + Vector3.up, Vector3.down, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.normal.y < .7f || hit.point.y > floor + .002f)
                    throw new InvalidOperationException($"No.1 landing would cross the saved ground at {at:F3}.");
                underside = Mathf.Min(underside, hit.point.y - .025f);
            }
        if (floor < .08f || floor > .24f)
            throw new InvalidOperationException("Unexpected No.1 pavement level at the doorway.");

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var brick = Material("RedBrick");
        var recess = Material("WindowRecess");
        var concrete = Material("EntranceConcrete");
        var railMetal = Material("BalconyIron");
        var group = new GameObject(GroupName);
        group.transform.SetParent(house.transform, false);
        group.transform.SetPositionAndRotation(front.GetColumn(3), orientation);
        for (int i = 0; i < lods.Length; i++)
        {
            filters[i].sharedMesh = Save(bases[i], $"House1OriginalWithoutFrontonLOD{i}");
            PrefabUtility.RecordPrefabInstancePropertyModifications(filters[i]);
            var facade = Fronton(brick, recess, 36 - i * 8, 14 - i * 3);
            var model = facade.Create($"House1BrickFrontonLOD{i}", $"Кирпичный фронтон · LOD {i}", Root, true);
            model.transform.SetParent(group.transform, false);
            lods[i].renderers = new[] { lods[i].renderers[0], model.GetComponent<Renderer>() };
        }
        lodGroup.SetLODs(lods);
        lodGroup.RecalculateBounds();
        PrefabUtility.RecordPrefabInstancePropertyModifications(lodGroup);

        var paving = new EnvironmentMesh();
        paving.Box(concrete, new Vector3(0, (floor + underside) * .5f, (PlatformBack + PlatformFront) * .5f),
            new Vector3(PlatformHalfWidth * 2, floor - underside, PlatformFront - PlatformBack));
        var landing = MakePhysical(paving, group.transform, "House1EntryLanding", "Бетонная входная площадка");
        var rails = new EnvironmentMesh();
        foreach (float side in new[] { -RailSide, RailSide })
            Rail(rails, railMetal, side, floor);
        var railing = MakePhysical(rails, group.transform, "House1EntryRailings", "Парные стальные перила");

        Physics.SyncTransforms();
        float seamJump = 0;
        foreach (float x in new[] { -.43f, 0, .43f })
        {
            Vector3 inside = P(x, 0, PlatformFront - .03f);
            Vector3 outside = P(x, 0, PlatformFront + .03f);
            if (!landing.Raycast(new Ray(inside + Vector3.up, Vector3.down), out var top, 2) ||
                Mathf.Abs(top.point.y - floor) > .002f || top.normal.y < .99f)
                throw new InvalidOperationException("No.1 landing lacks support across the walking line.");
            seamJump = Mathf.Max(seamJump, Mathf.Abs(PavedHeight(access, outside) - floor));
        }
        foreach (float side in new[] { -RailSide, RailSide })
            if (!landing.Raycast(new Ray(P(side, floor + 1, 1.38f), Vector3.down), out _, 2))
                throw new InvalidOperationException("The No.1 railing is outside the landing.");
        if (seamJump > .035f || railing.bounds.size.y > RailHeight + .03f || RailSide * 2 - .04f < 2.8f)
            throw new InvalidOperationException("The No.1 landing seam or passage failed its geometry check.");
        for (int i = 1; i < route.Length; i++)
        {
            Vector3 center = route[i] + Vector3.up * .54f;
            if (Physics.CheckCapsule(center, center + Vector3.up * .52f, .30f, ~0, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException($"The No.1 walk to the saved gate is blocked at route point {i}.");
        }
        access.surfaces = access.surfaces.Append(landing).ToArray();
        EditorUtility.SetDirty(access);
        PrefabUtility.RecordPrefabInstancePropertyModifications(access);

        var evidence = new GameObject("Фотоориентир · фронтон и вход №1") { tag = "EditorOnly" };
        evidence.transform.SetParent(group.transform, false);
        var note = evidence.AddComponent<PhotoSurveyMarker>();
        note.category = "№1 · фигурный фронтон, круглое углубление и входная площадка";
        note.observation = "На панораме видны арочный гребень с уступами, круглое тёмное углубление над арочным окном и площадка со стальными перилами.";
        note.uncertainty = "Размеры оценены по панораме. Ширина фронтона 5,4 м, диаметр углубления 0,51 м, площадка 3,14 × 1,79 м, перила 0,86 м. Сохранена исходная отметка порога, геометрия ступеней и маршрут через зелёную ограду.";
        note.sourceUrls = new[] { Panorama };
        note.studyBounds = landing.bounds;
        note.studyBounds.Encapsulate(lods[0].renderers[1].bounds);
        note.geometryProvisional = true;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_HOUSE1_FACADE lods=3 oldTriangles=46x3 floor={floor:F3} seamJump={seamJump:F3} route={route.Length} landing={P(0, floor, 1.32f):F3}");
    }

    private static Mesh WithoutOldFronton(MeshFilter filter, Matrix4x4 worldToFront)
    {
        var source = filter.sharedMesh;
        var localToFront = worldToFront * filter.transform.localToWorldMatrix;
        var points = source.vertices.Select(localToFront.MultiplyPoint3x4).ToArray();
        var copy = UnityEngine.Object.Instantiate(source);
        int oldBoxes = 0, oldBand = 0;
        bool BoxVertex(Vector3 p) => Mathf.Abs(p.x) <= 2.103f && Mathf.Abs(p.z) <= .327f && p.y >= 6.874f;
        bool BandVertex(Vector3 p) => Mathf.Abs(p.x) <= 2.703f && Mathf.Abs(p.z - OldWall) <= .001f &&
            p.y >= OldWallBand - .002f && p.y <= OldWallTop + .002f;
        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            var indices = source.GetTriangles(sub);
            var kept = new List<int>(indices.Length);
            for (int j = 0; j < indices.Length; j += 3)
            {
                Vector3 a = points[indices[j]], b = points[indices[j + 1]], c = points[indices[j + 2]];
                if (BoxVertex(a) && BoxVertex(b) && BoxVertex(c)) { oldBoxes++; continue; }
                if (BandVertex(a) && BandVertex(b) && BandVertex(c) &&
                    Mathf.Max(a.y, b.y, c.y) > OldWallTop - .002f) { oldBand++; continue; }
                kept.Add(indices[j]);
                kept.Add(indices[j + 1]);
                kept.Add(indices[j + 2]);
            }
            copy.SetTriangles(kept, sub, false);
        }
        if (oldBoxes != 36 || oldBand != 10)
            throw new InvalidOperationException($"No.1 fronton selection changed in {source.name}: {oldBoxes} box and {oldBand} wall triangles.");
        return copy;
    }

    private static EnvironmentMesh Fronton(Material brick, Material recess, int sectors, int curve)
    {
        var mesh = new EnvironmentMesh();
        const float face = .51f, back = -.085f, shoulder = 7.40f, lowerStep = 7.71f, spring = 8.03f;
        // The two shoulder heights and the central curved crown come from the panoramic silhouette.
        const float panel = 1.13f;
        var centerPanel = new List<Vector2>
        {
            new Vector2(-panel, OldWallBand), new Vector2(panel, OldWallBand),
            new Vector2(panel, spring), new Vector2(.89f, spring)
        };
        for (int i = 1; i < curve; i++)
        {
            float angle = Mathf.PI * i / curve;
            centerPanel.Add(new Vector2(.89f * Mathf.Cos(angle), spring + .86f * Mathf.Sin(angle)));
        }
        centerPanel.Add(new Vector2(-.89f, spring));
        centerPanel.Add(new Vector2(-panel, spring));

        // Include every corner angle so the radial quads preserve the crown silhouette.
        // The ring is split at the projecting shoulders so the opening sits in a straight wall panel.
        AddRing(mesh, brick, recess, centerPanel, face, back, sectors);
        foreach (float side in new[] { -1f, 1f })
        {
            mesh.Box(brick, new Vector3(side * (1.86f + panel) * .5f, (OldWallBand + lowerStep) * .5f, (face + back) * .5f),
                new Vector3(1.86f - panel, lowerStep - OldWallBand, face - back));
            mesh.Box(brick, new Vector3(side * (2.70f + 1.86f) * .5f, (OldWallBand + shoulder) * .5f, (face + back) * .5f),
                new Vector3(2.70f - 1.86f, shoulder - OldWallBand, face - back));
            mesh.Box(brick, new Vector3(side * 2.45f, (shoulder + .13f) * .5f, .47f),
                new Vector3(.50f, shoulder - .13f, .29f));
            mesh.Box(brick, new Vector3(side * 2.45f, shoulder + .025f, .47f), new Vector3(.58f, .05f, .36f));
        }
        // A shallow masonry arch follows the broad recess around the original arched window.
        for (int i = 0; i < curve; i++)
        {
            float a = Mathf.PI * i / curve, b = Mathf.PI * (i + 1) / curve;
            Vector2 A(float angle, float width, float rise) => new Vector2(width * Mathf.Cos(angle), 5.92f + rise * Mathf.Sin(angle));
            Vector2 ia = A(a, 2.19f, .90f), ib = A(b, 2.19f, .90f);
            Vector2 oa = A(a, 2.40f, 1.10f), ob = A(b, 2.40f, 1.10f);
            mesh.Face(brick, Vector3.forward, At(ia, .618f), At(oa, .618f), At(ob, .618f), At(ib, .618f));
            mesh.Face(brick, Vector3.down, At(ia, .618f), At(ib, .618f), At(ib, .345f), At(ia, .345f));
            mesh.Face(brick, Vector3.up, At(oa, .618f), At(oa, .345f), At(ob, .345f), At(ob, .618f));
        }
        return mesh;
    }

    private static void AddRing(EnvironmentMesh mesh, Material brick, Material recess, List<Vector2> outline,
        float face, float back, int sectors)
    {
        const float turn = Mathf.PI * 2;
        var angles = new List<float>();
        for (int i = 0; i < sectors; i++) angles.Add(turn * i / sectors);
        angles.AddRange(outline.Select(p => Mathf.Repeat(Mathf.Atan2(p.y - Oculus.y, p.x - Oculus.x), turn)));
        angles.Sort();
        var unique = angles.Where((angle, i) => i == 0 || angle - angles[i - 1] > .0001f).ToList();
        unique.Add(turn);
        for (int i = 1; i < unique.Count; i++)
        {
            Vector2 directionA = Direction(unique[i - 1]), directionB = Direction(unique[i]);
            Vector2 outerA = Boundary(outline, directionA), outerB = Boundary(outline, directionB);
            Vector2 innerA = Oculus + directionA * OculusRadius, innerB = Oculus + directionB * OculusRadius;
            Vector2 lipA = Oculus + directionA * (OculusRadius + .047f), lipB = Oculus + directionB * (OculusRadius + .047f);
            mesh.Face(brick, Vector3.forward, At(lipA, face), At(outerA, face), At(outerB, face), At(lipB, face));
            mesh.Face(brick, Vector3.forward, At(innerA, face + .023f), At(lipA, face), At(lipB, face), At(innerB, face + .023f));
            Vector3 inward = -(Vector3)(directionA + directionB).normalized;
            mesh.Face(brick, inward, At(innerA, face + .023f), At(innerB, face + .023f), At(innerB, .075f), At(innerA, .075f));
            mesh.Face(recess, Vector3.forward, At(Oculus, .075f), At(innerA, .075f), At(innerB, .075f));
            mesh.Face(brick, Vector3.back, At(innerA, back), At(innerB, back), At(outerB, back), At(outerA, back));
            Vector3 outward = Vector3.Cross((Vector3)(outerB - outerA), Vector3.forward).normalized;
            mesh.Face(brick, outward, At(outerA, face), At(outerA, back), At(outerB, back), At(outerB, face));
        }
    }

    private static Vector2 Boundary(List<Vector2> outline, Vector2 direction)
    {
        float distance = float.PositiveInfinity;
        for (int i = 0; i < outline.Count; i++)
        {
            Vector2 a = outline[i] - Oculus, edge = outline[(i + 1) % outline.Count] - outline[i];
            float determinant = Cross(direction, edge);
            if (Mathf.Abs(determinant) < .000001f) continue;
            float t = Cross(a, edge) / determinant, u = Cross(a, direction) / determinant;
            if (t > 0 && u >= -.00001f && u <= 1.00001f) distance = Mathf.Min(distance, t);
        }
        if (float.IsInfinity(distance) || distance <= OculusRadius + .05f)
            throw new InvalidOperationException("The No.1 oculus intersects the crest boundary.");
        return Oculus + direction * distance;
    }

    private static void Rail(EnvironmentMesh mesh, Material paint, float x, float floor)
    {
        const float start = .63f, end = 2.13f;
        mesh.Box(paint, new Vector3(x, floor + RailHeight - .02f, (start + end) * .5f), new Vector3(.043f, .041f, end - start + .10f));
        mesh.Box(paint, new Vector3(x, floor + .19f, (start + end) * .5f), new Vector3(.026f, .025f, end - start));
        foreach (float z in new[] { start, (start + end) * .5f, end })
        {
            mesh.Box(paint, new Vector3(x, floor + .009f, z), new Vector3(.095f, .018f, .095f));
            mesh.Box(paint, new Vector3(x, floor + RailHeight * .5f, z), new Vector3(.036f, RailHeight, .036f));
        }
        for (int i = 1; i < 10; i++)
        {
            if (i == 5) continue;
            mesh.Box(paint, new Vector3(x, floor + .51f, Mathf.Lerp(start, end, i / 10f)),
                new Vector3(.015f, .64f, .015f));
        }
    }

    private static MeshCollider MakePhysical(EnvironmentMesh mesh, Transform parent, string asset, string label)
    {
        var item = mesh.Create(asset, label, Root, true);
        item.transform.SetParent(parent, false);
        var collider = item.AddComponent<MeshCollider>();
        collider.sharedMesh = item.GetComponent<MeshFilter>().sharedMesh;
        return collider;
    }

    private static float PavedHeight(ResidentialAccess access, Vector3 point)
    {
        float top = float.NegativeInfinity;
        Ray ray = new Ray(point + Vector3.up, Vector3.down);
        foreach (var surface in access.surfaces)
            if (surface && surface.Raycast(ray, out var hit, 2f) && hit.normal.y > .85f)
                top = Mathf.Max(top, hit.point.y);
        if (float.IsInfinity(top))
            throw new InvalidOperationException($"The existing paved route at No.1 lacks support at {point:F3}.");
        return top;
    }

    private static Mesh Save(Mesh copy, string name)
    {
        copy.name = name;
        string path = $"{Root}/Meshes/{name}.asset";
        var asset = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!asset)
        {
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }
        EditorUtility.CopySerialized(copy, asset);
        UnityEngine.Object.DestroyImmediate(copy);
        return asset;
    }

    private static Material Material(string name) =>
        AssetDatabase.LoadAssetAtPath<Material>($"Assets/StreetGenerated/Buildings/Materials/{name}.mat") ??
        throw new InvalidOperationException("Missing original No.1 material " + name);

    private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
    private static Vector3 At(Vector2 point, float z) => new Vector3(point.x, point.y, z);
    private static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
