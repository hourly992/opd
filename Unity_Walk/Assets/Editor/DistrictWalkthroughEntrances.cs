using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictWalkthroughEntrances
{
    private const string Root = "Assets/PhotoReconstruction/Stage13Entrances";
    private const string Group = "Обход · примыкания входов №1, 6, 8, 10, 13 и 15";
    private const string Asphalt = "Объединённый асфальт проездов и въездов";
    private const string Edging = "Низкие кромки скруглённых подходов";
    private const string NorthPaving = "Скруглённое мощение · 1";
    private const string SouthPaving = "Скруглённое мощение · 2";

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before applying the walkthrough entrance repairs.");
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        if (GameObject.Find(Group))
        {
            ClearLowerApronOverlap();
            SurveyNorthLanding(access);
            return;
        }

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        Physics.SyncTransforms();
        var parent = new GameObject(Group).transform;
        RepairNorthCanopy(access, parent);
        RepairLibrary(access, parent);
        RepairSouthernApproaches();
        BlendHouse15Apron(parent);
        RepairNorthGate(parent);
        ClearLowerApronOverlap();
        SurveyNorthLanding(access);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void ClearLowerApronOverlap()
    {
        var asphalt = Surface(Asphalt);
        var apron = Surface("№13 · бетонная площадка за ступенями");
        var verified = new[] { new Vector3(165.4976f, 0, 97.2342f), new Vector3(165.3821f, 0, 97.4614f),
            new Vector3(165.5992f, 0, 97.0625f) };
        if (verified.Any(point => !TryHeight(apron, point, out float height) || Mathf.Abs(height - .018f) > .002f))
            throw new InvalidOperationException("The lower No.13 apron no longer supports the witnessed overlap.");
        if (verified.All(point => !TryHeight(asphalt, point, out _))) return;
        Physics.SyncTransforms();

        // The diagonal across the slab on the captured frame is a coplanar remnant
        // of the old apron. Keep the concrete as the sole floor inside that outline.
        var area = Rect.MinMaxRect(164.916f, 96.397f, 169.004f, 98.137f);
        var clipped = PhotoCourtSeamRepairStage05.ClipOverPaving(Filter(asphalt), apron, area, out int triangles);
        if (!clipped || triangles == 0)
            throw new InvalidOperationException("The visible No.13 asphalt remnant could not be clipped.");
        var mesh = Save(clipped, "House13ApronRetainedAsphalt");
        Filter(asphalt).sharedMesh = mesh;
        asphalt.sharedMesh = null;
        asphalt.sharedMesh = mesh;
        Physics.SyncTransforms();
        if (verified.Any(point => TryHeight(asphalt, point, out _)))
            throw new InvalidOperationException("The concrete and old asphalt still overlap on the No.13 apron.");
        EditorUtility.SetDirty(Filter(asphalt));
        EditorUtility.SetDirty(asphalt);
        Debug.Log($"STAGE13_ENTRANCE_13_APRON removedCoplanarTriangles={triangles} clearFloorPoints={verified.Length}");
    }

    private static void SurveyNorthLanding(ResidentialAccess access)
    {
        var landing = Surface("Серая площадка, две ступени и красный скат");
        var movedShoulderProbes = new List<Vector3>();
        int routeChanges = 0, landingChanges = 0;
        var routes = Recheck(access.surfaceChecks, false, ref routeChanges);
        var platforms = Recheck(access.entranceLandingChecks, true, ref landingChanges);
        if (routeChanges == 0 && landingChanges == 0) return;
        access.surfaceChecks = routes.Concat(movedShoulderProbes).ToArray();
        access.entranceLandingChecks = platforms;
        EditorUtility.SetDirty(access);
        PrefabUtility.RecordPrefabInstancePropertyModifications(access);
        Debug.Log($"STAGE13_ENTRANCE_13_SURVEY routes={routeChanges} landings={landingChanges} " +
            $"preservedShoulderProbes={movedShoulderProbes.Count}");

        Vector3[] Recheck(Vector3[] original, bool platform, ref int changes)
        {
            var updated = (Vector3[])original.Clone();
            for (int i = 0; i < original.Length; i++)
            {
                Vector3 point = original[i];
                if (point.x < 164.85f || point.x > 169.05f || point.z < 98.13f || point.z > 100.17f)
                    continue;
                var ray = new Ray(point + Vector3.up * 2, Vector3.down);
                if (access.surfaces.Any(surface => surface && surface.Raycast(ray, out var hit, platform ? 2.3f : 2.2f)
                    && (!platform || hit.normal.y > .85f) && Mathf.Abs(hit.point.y - point.y) < (platform ? .025f : .06f)))
                    continue;
                if (!landing.Raycast(ray, out var actual, 2.3f) || actual.normal.y < .76f ||
                    Mathf.Abs(actual.point.y - point.y) > .125f)
                    throw new InvalidOperationException($"The measured No.13 landing has no supported revised height at {point:F4}.");
                if (platform && actual.normal.y <= .85f)
                {
                    // The old sample was on the asphalt below the ramp shoulder. Keep
                    // the original location as a slope probe and sample the nearby deck
                    // for the check which requires a flatter standing surface.
                    movedShoulderProbes.Add(actual.point);
                    float towardDeck = Mathf.Sign(167.45f - point.x);
                    bool foundDeck = false;
                    for (int step = 1; step <= 10; step++)
                    {
                        var inside = point + Vector3.right * (towardDeck * step * .024f);
                        if (!landing.Raycast(new Ray(inside + Vector3.up * 2, Vector3.down), out var deck, 2.3f) ||
                            deck.normal.y <= .85f || Mathf.Abs(deck.point.y - actual.point.y) > .09f)
                            continue;
                        actual = deck;
                        foundDeck = true;
                        break;
                    }
                    if (!foundDeck)
                        throw new InvalidOperationException($"No flat walking surface beside the No.13 ramp shoulder at {point:F4}.");
                }
                updated[i] = actual.point;
                changes++;
            }
            return updated;
        }
    }

    private static void RepairNorthCanopy(ResidentialAccess access, Transform parent)
    {
        var landing = Surface("Серая площадка, две ступени и красный скат");
        var asphalt = Surface(Asphalt);
        var red = Surface(NorthPaving);
        var edges = Surface(Edging);
        var shoulder = Surface("Переход асфальт–грунт · north");
        Vector3[] samples = {
            new Vector3(165.55f, 0, 98.70f), new Vector3(165.55f, 0, 99.14f),
            new Vector3(166.85f, 0, 99.07f), new Vector3(167.25f, 0, 99.55f)
        };
        float[] saved = samples.Select(point => Height(landing, point)).ToArray();
        if (Mathf.Abs(saved[0] - .139f) > .003f || Mathf.Abs(saved[1] - .08f) > .003f)
            throw new InvalidOperationException("The photographed two step north approach at No.13 no longer matches the saved levels.");

        // The upper deck, step and ramp provide the actual walkable surface here.
        // Remove their exact plan from the older road, pavement and verge below.
        var old = new[] { asphalt, red, shoulder, edges }.Select(Filter).ToArray();
        var footprint = TopTriangles(landing, Rect.MinMaxRect(164.85f, 98.13f, 169.05f, 100.17f));
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(old, footprint, Root, _ => true);
        Physics.SyncTransforms();

        // Refinish the saved lower apron behind the steps. The existing asphalt outline
        // and heights are reused, so the door sill and both photographed steps stay put.
        Rect back = Rect.MinMaxRect(164.91f, 96.39f, 169.01f, 98.14f);
        var apron = new EnvironmentMesh();
        var concrete = landing.GetComponent<MeshRenderer>().sharedMaterials
            .First(material => material && material.name.StartsWith("EntranceConcrete", StringComparison.Ordinal));
        var sections = new List<List<Vector2>>();
        foreach (var triangle in WorldTriangles(asphalt))
        {
            if (Vector3.Cross(triangle[1] - triangle[0], triangle[2] - triangle[0]).normalized.y < .94f ||
                triangle.Any(p => Mathf.Abs(p.y - .018f) > .003f))
                continue;
            var clipped = ClipRectangle(triangle, back);
            if (clipped.Count < 3) continue;
            var polygon = CounterClockwise(clipped);
            if (polygon == null) continue;
            sections.Add(polygon);
            apron.Face(concrete, Vector3.up, clipped.ToArray());
        }
        if (sections.Count == 0)
            throw new InvalidOperationException("Could not match the saved lower asphalt apron of No.13.");
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(new[] { Filter(asphalt) }, sections, Root, _ => true);
        var lower = CreatePhysical(apron, "House13LowerEntranceApron", "№13 · бетонная площадка за ступенями", parent, access);
        Physics.SyncTransforms();
        for (int i = 0; i < samples.Length; i++)
            if (Mathf.Abs(Height(landing, samples[i]) - saved[i]) > .001f)
                throw new InvalidOperationException("A saved No.13 step or ramp changed elevation.");
        if (Mathf.Abs(Height(lower, new Vector3(166.8544f, 0, 97.70f)) - .018f) > .002f)
            throw new InvalidOperationException("The lower No.13 apron lost its paved support.");
        Debug.Log($"STAGE13_ENTRANCE_13 supportPolygons={footprint.Count} lowerApronPolygons={sections.Count}");
    }

    private static void RepairLibrary(ResidentialAccess access, Transform parent)
    {
        var floor = Surface("Спуск и бетонная площадка");
        var concrete = floor.GetComponent<MeshRenderer>().sharedMaterial;
        const float doorX = -46.623085f, wallZ = -45.897f, half = .675f, floorY = -.18f;
        var sample = new Vector3(doorX, 0, wallZ - .035f);
        if (Mathf.Abs(Height(floor, sample) - floorY) > .002f)
            throw new InvalidOperationException("The library threshold is no longer at the audited level.");

        // The older verge remains above the recess as a brown strip. Keep its
        // continuation outside the concrete outline and remove its covered part.
        var verge = Surface("Переход асфальт–грунт · west");
        var recess = Rect.MinMaxRect(-45.691f, -48.988f, -42.982f, wallZ - .002f);
        var leftover = PhotoCourtSeamRepairStage05.ClipOverPaving(Filter(verge), floor, recess, out int removed);
        if (!leftover || removed == 0)
            throw new InvalidOperationException("The audited verge strip over the library concrete was not found.");
        Filter(verge).sharedMesh = verge.sharedMesh = Save(leftover, "Library6VergeOutsideRecess");

        // Close the reveal where the old paving stopped at the facade plane.
        // The extension carries both render and collision to the closed door.
        var threshold = new EnvironmentMesh();
        threshold.Box(concrete, new Vector3(doorX, floorY - .018f, wallZ + .035f),
            new Vector3(half * 2, .036f, .070f));
        var thresholdCollider = CreatePhysical(threshold, "Library6ThresholdContinuation",
            "Библиотека №6 · стык площадки с порогом", parent, access);
        Physics.SyncTransforms();
        if (Mathf.Abs(Height(thresholdCollider, new Vector3(doorX, 0, wallZ + .03f)) - floorY) > .002f)
            throw new InvalidOperationException("The new library reveal floor failed its level check.");
        var formerStrip = new Vector3(-44.8764f, .6f, -46.1386f);
        if (verge.Raycast(new Ray(formerStrip, Vector3.down), out var vergeHit, 1f) &&
            vergeHit.point.y > floorY + .002f)
            throw new InvalidOperationException("The older verge still crosses the library floor.");

        // Shorten the bottom door-frame member while keeping the door, jambs,
        // sill and plinth at the positions of the existing recess model.
        var door = GameObject.Find("Дверь, откосы и закрытый порог").GetComponent<MeshFilter>();
        var clone = UnityEngine.Object.Instantiate(door.sharedMesh);
        var vertices = clone.vertices;
        int adjusted = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            var p = door.transform.TransformPoint(vertices[i]);
            if (Mathf.Abs(p.y - (floorY + .05f)) > .001f || Mathf.Abs(p.x - doorX) > half + .001f ||
                p.z < wallZ + .022f || p.z > wallZ + .078f) continue;
            p.y = floorY + .025f;
            vertices[i] = door.transform.InverseTransformPoint(p);
            adjusted++;
        }
        if (adjusted < 4 || adjusted > 24)
            throw new InvalidOperationException($"Unexpected number of library bottom frame vertices: {adjusted}.");
        clone.vertices = vertices;
        clone.RecalculateBounds();
        door.sharedMesh = Save(clone, "Library6DoorClosedThreshold");
        Debug.Log($"STAGE13_ENTRANCE_6 threshold={floorY:F3} frameVertices={adjusted} vergeTriangles={removed}");
    }

    private static void RepairSouthernApproaches()
    {
        var asphalt = Surface(Asphalt);
        var pavers = Surface(SouthPaving);
        var edges = Surface(Edging);
        var shoulder = Surface("Переход асфальт–грунт · south");
        var sites = new[] {
            (address: "8", area: Rect.MinMaxRect(-21.42f, -105.19f, -18.70f, -102.94f), floor: pavers),
            (address: "10", area: Rect.MinMaxRect(13.90f, -176.00f, 16.33f, -173.47f), floor: pavers),
            (address: "15", area: Rect.MinMaxRect(72.68f, -108.10f, 74.41f, -106.30f),
                floor: Surface("Асфальт, бордюр, брусчатка и отмостка · узел №15"))
        };
        foreach (var site in sites)
        {
            var patches = TopTriangles(site.floor, site.area);
            if (patches.Count == 0)
                throw new InvalidOperationException($"No surveyed paving was found on approach {site.address}.");
            var before = asphalt.sharedMesh;
            PhotoCentralCourtyardStage04.RemoveCoveredHardscape(new[] { Filter(asphalt), Filter(shoulder) },
                patches, Root, _ => true);
            Physics.SyncTransforms();
            var kerb = PhotoCourtSeamRepairStage05.ClipOverPaving(Filter(edges), site.floor, site.area, out int trimmed);
            if (kerb)
            {
                var saved = Save(kerb, $"Entrance{site.address}JoinedEdges");
                Filter(edges).sharedMesh = edges.sharedMesh = saved;
                Physics.SyncTransforms();
            }
            Debug.Log($"STAGE13_ENTRANCE_SOUTH address={site.address} pavingPolygons={patches.Count} " +
                $"asphaltClipped={before != asphalt.sharedMesh} edgingTriangles={trimmed}");
        }
    }

    private static void BlendHouse15Apron(Transform parent)
    {
        var asphalt = Surface(Asphalt);
        var shoulder = Surface("Переход асфальт–грунт · south");
        var ground = Surface("Grass");
        var turf = ground.GetComponent<MeshRenderer>().sharedMaterial;
        var arris = shoulder.GetComponent<MeshRenderer>().sharedMaterials
            .First(material => material && material.name.StartsWith("AsphaltArris", StringComparison.Ordinal));
        // Actual outline of the exposed asphalt outside the rebuilt tiled entrance.
        // The short concave gap on its east side joins the unchanged driveway.
        var rim = new[] {
            new Vector2(74.2103f, -104.6088f), new Vector2(74.1978f, -104.4820f),
            new Vector2(74.1608f, -104.3601f), new Vector2(74.1008f, -104.2477f),
            new Vector2(74.0199f, -104.1492f), new Vector2(73.9214f, -104.0683f),
            new Vector2(73.8090f, -104.0083f), new Vector2(73.6871f, -103.9713f),
            new Vector2(73.5603f, -103.9588f), new Vector2(73.2353f, -103.9588f),
            new Vector2(72.9103f, -103.9588f), new Vector2(72.5853f, -103.9588f)
        };
        var mesh = new EnvironmentMesh();
        var patches = new List<List<Vector2>>();
        var profiles = new Vector3[rim.Length][];
        for (int i = 0; i < rim.Length; i++)
        {
            Vector2 axis = (rim[Mathf.Min(i + 1, rim.Length - 1)] - rim[Mathf.Max(i - 1, 0)]).normalized;
            var outward = new Vector3(axis.y, 0, -axis.x);
            var edge = new Vector3(rim[i].x, 0, rim[i].y);
            edge.y = Height(asphalt, edge - outward * .012f);
            if (edge.y < .053f || edge.y > .105f)
                throw new InvalidOperationException($"The No.15 side outline moved at {edge:F4}.");
            float width = .36f;
            float road = 0;
            bool reconnects = false;
            for (float d = .038f; d <= width; d += .016f)
            {
                var test = edge + outward * d;
                if (!TryHeight(asphalt, test, out road)) continue;
                width = d - .005f;
                reconnects = true;
                break;
            }
            var toe = edge + outward * width;
            toe.y = reconnects ? road - .001f : Height(ground, toe) - .002f;
            // Match the paving at the inner edge, then feather the soil into its
            // actual support. This also fills the uncovered gap next to the curve.
            edge -= outward * .002f;
            edge.y -= .001f;
            var mid = Vector3.Lerp(edge, toe, Mathf.Min(.30f, .061f / width));
            mid.y = Mathf.Lerp(edge.y, toe.y, .19f);
            profiles[i] = new[] { edge, mid, toe };
        }
        for (int i = 1; i < profiles.Length; i++)
            for (int band = 0; band < 2; band++)
            {
                Vector3 a = profiles[i - 1][band], b = profiles[i][band];
                Vector3 c = profiles[i][band + 1], d = profiles[i - 1][band + 1];
                var polygon = CounterClockwise(new List<Vector3> { a, b, c, d });
                if (polygon == null) continue;
                patches.Add(polygon);
                AddTop(mesh, band == 0 ? arris : turf, a, b, c);
                AddTop(mesh, band == 0 ? arris : turf, a, c, d);
            }
        var oldShoulder = shoulder.sharedMesh;
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(new[] { Filter(shoulder) }, patches, Root, _ => true);
        if (shoulder.sharedMesh == oldShoulder && !AssetDatabase.GetAssetPath(shoulder.sharedMesh).StartsWith(Root, StringComparison.Ordinal))
            throw new InvalidOperationException("The old dark side of the No.15 asphalt was not clipped.");
        var blend = CreatePhysical(mesh, "House15ApronSideBlend", "№15 · скруглённый стык асфальта и газона", parent);
        Physics.SyncTransforms();
        int supports = 0;
        for (int i = 1; i < profiles.Length; i++)
        {
            var center = (profiles[i - 1][1] + profiles[i][1] + profiles[i - 1][2] + profiles[i][2]) * .25f;
            if (!TryHeight(blend, center, out _))
                throw new InvalidOperationException($"The No.15 apron blend has no support at {center:F4}.");
            supports++;
        }
        Debug.Log($"STAGE13_ENTRANCE_15 sideBlendSpans={supports} retainedRim={rim.Length}");
    }

    private static void RepairNorthGate(Transform parent)
    {
        var red = Surface(NorthPaving);
        var lawn = Surface("Grass");
        var turf = lawn.GetComponent<MeshRenderer>().sharedMaterial;
        var paving = red.GetComponent<MeshRenderer>().sharedMaterial;
        // This is the exposed side of the red connector in the gate opening.
        // Its upper edge comes from the audited paving triangle, its toe from the grass.
        var edgeA = new Vector3(-111.3424f, .1941f, 155.2007f);
        var edgeB = new Vector3(-110.30f, .1869f, 155.4801f);
        Vector3 outwards = new Vector3(-.259f, 0, .966f);
        var mesh = new EnvironmentMesh();
        var near = new Vector3[10];
        var far = new Vector3[10];
        for (int i = 0; i < near.Length; i++)
        {
            float t = i / (float)(near.Length - 1);
            Vector3 inner = Vector3.Lerp(edgeA, edgeB, t) - outwards * .012f;
            float top = Height(red, inner);
            near[i] = Vector3.Lerp(edgeA, edgeB, t);
            near[i].y = top - Mathf.Lerp(.008f, .15f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.68f, 1, t)));
            far[i] = near[i] + outwards * Mathf.Lerp(.43f, .23f, t);
            far[i].y = Height(lawn, far[i]) - .005f;
        }
        for (int i = 1; i < near.Length; i++)
        {
            // The first four spans touch the old public walk's descending flank.
            // Keep that join brick, with grass only on the verge farther from the walk.
            Material finish = i <= 4 ? paving : turf;
            AddTop(mesh, finish, near[i - 1], far[i - 1], near[i]);
            AddTop(mesh, finish, far[i - 1], far[i], near[i]);
        }
        var verge = CreatePhysical(mesh, "House1GateVergeTie", "№1 · откос газона у входной дорожки", parent);
        Physics.SyncTransforms();
        if (Height(verge, (near[3] + far[3]) * .5f) > near[3].y)
            throw new InvalidOperationException("The verge at the No.1 gate crosses the walking surface.");
        Debug.Log("STAGE13_ENTRANCE_1 joinedGateVerge=9");
    }

    private static MeshCollider Surface(string name)
    {
        var obj = GameObject.Find(name);
        var collider = obj ? obj.GetComponent<MeshCollider>() : null;
        if (!collider || !collider.sharedMesh || Filter(collider).sharedMesh != collider.sharedMesh)
            throw new InvalidOperationException("Expected a shared render and collision mesh at " + name);
        return collider;
    }

    private static MeshFilter Filter(MeshCollider collider) => collider.GetComponent<MeshFilter>();

    private static float Height(MeshCollider surface, Vector3 point)
    {
        if (!TryHeight(surface, point, out float height))
            throw new InvalidOperationException($"No floor on {surface.name} at {point:F4}.");
        return height;
    }

    private static bool TryHeight(MeshCollider surface, Vector3 point, out float height)
    {
        var ray = new Ray(new Vector3(point.x, .85f, point.z), Vector3.down);
        bool hitFloor = surface.Raycast(ray, out var hit, 1.25f) && hit.normal.y > .55f;
        height = hitFloor ? hit.point.y : 0;
        return hitFloor;
    }

    private static IEnumerable<Vector3[]> WorldTriangles(MeshCollider surface)
    {
        var vertices = surface.sharedMesh.vertices.Select(surface.transform.TransformPoint).ToArray();
        var indices = surface.sharedMesh.triangles;
        for (int i = 0; i < indices.Length; i += 3)
            yield return new[] { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
    }

    private static List<List<Vector2>> TopTriangles(MeshCollider surface, Rect bounds)
    {
        var polygons = new List<List<Vector2>>();
        foreach (var tri in WorldTriangles(surface))
        {
            if (Vector3.Cross(tri[1] - tri[0], tri[2] - tri[0]).normalized.y < .62f) continue;
            var polygon = CounterClockwise(ClipRectangle(tri, bounds));
            if (polygon != null) polygons.Add(polygon);
        }
        return polygons;
    }

    private static List<Vector3> ClipRectangle(Vector3[] triangle, Rect bounds)
    {
        var points = new List<Vector3>(triangle);
        foreach (var plane in new[] { (axis: 0, edge: bounds.xMin, side: 1f), (axis: 0, edge: bounds.xMax, side: -1f),
            (axis: 2, edge: bounds.yMin, side: 1f), (axis: 2, edge: bounds.yMax, side: -1f) })
        {
            if (points.Count == 0) break;
            var kept = new List<Vector3>();
            Vector3 previous = points[points.Count - 1];
            float last = (previous[plane.axis] - plane.edge) * plane.side;
            foreach (var point in points)
            {
                float current = (point[plane.axis] - plane.edge) * plane.side;
                if ((current >= 0) != (last >= 0))
                    kept.Add(Vector3.LerpUnclamped(previous, point, last / (last - current)));
                if (current >= 0) kept.Add(point);
                previous = point;
                last = current;
            }
            points = kept;
        }
        return points;
    }

    private static List<Vector2> CounterClockwise(List<Vector3> vertices)
    {
        if (vertices.Count < 3) return null;
        var points = vertices.Select(p => new Vector2(p.x, p.z)).ToList();
        float twiceArea = 0;
        for (int i = 1; i + 1 < points.Count; i++)
        {
            Vector2 a = points[i] - points[0], b = points[i + 1] - points[0];
            twiceArea += a.x * b.y - a.y * b.x;
        }
        if (Mathf.Abs(twiceArea) < .000004f) return null;
        if (twiceArea < 0) points.Reverse();
        return points;
    }

    private static void AddTop(EnvironmentMesh mesh, Material material, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        mesh.Face(material, n.y < 0 ? -n : n, a, b, c);
    }

    private static MeshCollider CreatePhysical(EnvironmentMesh mesh, string asset, string label, Transform parent,
        ResidentialAccess access = null)
    {
        var obj = mesh.Create(asset, label, Root, true);
        obj.transform.SetParent(parent, false);
        var collider = obj.AddComponent<MeshCollider>();
        collider.sharedMesh = obj.GetComponent<MeshFilter>().sharedMesh;
        GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic);
        if (access)
        {
            access.surfaces = access.surfaces.Append(collider).ToArray();
            EditorUtility.SetDirty(access);
            PrefabUtility.RecordPrefabInstancePropertyModifications(access);
        }
        return collider;
    }

    private static Mesh Save(Mesh mesh, string name)
    {
        mesh.name = name;
        string path = $"{Root}/Meshes/{name}.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!saved)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
        EditorUtility.CopySerialized(mesh, saved);
        UnityEngine.Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(saved);
        return saved;
    }
}
