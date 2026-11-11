using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Conservative gap filling: connects existing entrance geometry to existing paved surfaces.
// New connectors are a designed provision, not surveyed courtyard alignments.
internal static class ResidentialAccessBuilder
{
    private sealed class Entrance { internal string address; internal Vector3 point, outward; internal bool porch, side; }
    private sealed class Landing
    {
        internal Vector3 point, outward, right;
        internal float halfWidth, back, front, height;
        internal bool Contains(Vector3 p) { var d = p - point; float along = Vector3.Dot(d, outward);
            return Mathf.Abs(Vector3.Dot(d, right)) <= halfWidth + .001f && along >= -back - .001f && along <= front + .001f; }
        internal Vector3 Closest(Vector3 p) { var d = p - point;
            return point + right * Mathf.Clamp(Vector3.Dot(d, right), -halfWidth, halfWidth)
                + outward * Mathf.Clamp(Vector3.Dot(d, outward), -back, front); }
    }
    private static readonly List<Landing> landings = new List<Landing>();
    private static readonly Dictionary<Vector2Int, float> heightCache = new Dictionary<Vector2Int, float>();
    private sealed class SurfaceRange { internal int lastTriangle; internal bool accepted; }
    private static MeshCollider[] paved;
    private static Dictionary<MeshCollider, Bounds> pavedBounds;
    private static Dictionary<MeshCollider, SurfaceRange[]> surfaceRanges;
    private static HashSet<Collider> obstacles;
    private static readonly Collider[] overlap = new Collider[128];
    private static int searchCandidates;
    private const float Width = 1.6f;

    internal static void Generate(Transform parent)
    {
        EnvironmentMesh.EnsureFolders(); Physics.SyncTransforms();
        landings.Clear();
        heightCache.Clear();
        paved = UnityEngine.Object.FindObjectsByType<MeshCollider>().Where(c => c.sharedMesh &&
            (c.sharedMesh.name.StartsWith("ReferenceRoad", StringComparison.Ordinal)
            || c.sharedMesh.name.StartsWith("ReferenceWalkways", StringComparison.Ordinal)
            || c.sharedMesh.name.StartsWith("ParkingSurface_", StringComparison.Ordinal)
            || c.sharedMesh.name == "PilotSurfaces" || c.sharedMesh.name == "Map_Asphalt"
            || c.sharedMesh.name == "Map_Path" || c.sharedMesh.name == "Map_Pavement")).ToArray();
        surfaceRanges = new Dictionary<MeshCollider, SurfaceRange[]>();
        pavedBounds = paved.ToDictionary(c => c, c => { var bounds = c.bounds; bounds.Expand(.02f); return bounds; });
        foreach (var collider in paved)
        {
            var renderer = collider.GetComponent<MeshRenderer>();
            var materials = renderer ? renderer.sharedMaterials : Array.Empty<Material>();
            var pavedMesh = collider.sharedMesh; var ranges = new SurfaceRange[pavedMesh.subMeshCount];
            int end = 0;
            for (int i = 0; i < ranges.Length; i++)
            {
                end += (int)pavedMesh.GetIndexCount(i) / 3;
                var material = i < materials.Length ? materials[i] : null;
                float kind = material && material.HasProperty("_SurfaceClass") ? material.GetFloat("_SurfaceClass") : 0;
                // ReferenceWalkways also carries green backfill; PilotSurfaces includes mulched beds.
                // Neither is an existing pedestrian connection merely because it shares a collider.
                ranges[i] = new SurfaceRange { lastTriangle = end, accepted = material && !(kind > .5f && kind < 1.5f) };
            }
            surfaceRanges.Add(collider, ranges);
        }
        obstacles = new HashSet<Collider>(UnityEngine.Object.FindObjectsByType<Collider>().Where(c => !c.isTrigger && c.enabled
            && c.name != "Grass" && !paved.Contains(c as MeshCollider) && c.bounds.max.y > .45f && c.bounds.min.y < 2));
        var entries = new List<Entrance>();
        foreach (var house in UnityEngine.Object.FindObjectsByType<StreetBuilding>())
            foreach (Transform marker in house.GetComponentsInChildren<Transform>())
                if (marker.name.StartsWith("Access · ", StringComparison.Ordinal))
                {
                    var entry = new Entrance { address = house.address + " (OSM " + house.osmId + ") / " + marker.name,
                        point = marker.position, outward = marker.forward,
                        porch = marker.name.Contains("к крыльцу"), side = marker.name.Contains("торцевой вход") };
                    if (house.osmId == 114788560) ResolveAnnexEntrance(entry);
                    entries.Add(entry);
                }
        var pilot = UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        if (pilot)
            foreach (var approach in pilot.entrances) entries.Add(new Entrance {
                address = "5 / " + approach.name, point = approach.position + approach.forward * 2.1f, outward = -approach.forward, porch = true });
        // Some repeated end details are inside the neighbouring section's projecting body.
        // Keep the buildings, but do not invent a passage through that solid geometry.
        var occluded = entries.Where(e => e.side && !Clear(e.point) && !Clear(e.point + e.outward * .8f)).ToArray();
        foreach (var entry in occluded)
            Debug.Log("ACCESS_OCCLUDED_SIDE_DETAIL " + entry.address + " obstacles=" + ObstacleNames(entry.point));
        entries.RemoveAll(e => occluded.Contains(e));
        var root = new GameObject("Пешеходные связи · существующие входы домов"); root.transform.SetParent(parent, false);
        var metadata = root.AddComponent<ResidentialAccess>();
        metadata.entranceCount = entries.Count;
        metadata.occludedSideDetails = occluded.Select(e => e.address).ToArray();
        metadata.accuracyNote = "Соединения существующих входов с существующими покрытиями. Новые участки спроектированы по свободному коридору; расположение не подтверждено обмером или панорамой.";
        var paving = EnvironmentMesh.Material("ResidentialAccessPaving", Color.white);
        StreetSurfaceLibrary.ApplyNamed(paving, "Pavement");
        var edge = EnvironmentMesh.Material("ResidentialAccessEdge", Color.white);
        StreetSurfaceLibrary.ApplyNamed(edge, "RoadKerb");
        var mesh = new EnvironmentMesh(); var routes = new List<ResidentialAccess.Route>();
        var checks = new List<Vector3>(); var blocked = new List<string>();
        var apron = EnvironmentMesh.Material("ResidentialEntranceAsphalt", Color.white);
        StreetSurfaceLibrary.ApplyNamed(apron, "Asphalt");
        foreach (var entry in entries.Where(e => e.porch || e.side))
        {
            var pad = new Landing { point = new Vector3(entry.point.x, 0, entry.point.z), outward = entry.outward,
                right = new Vector3(entry.outward.z, 0, -entry.outward.x), halfWidth = entry.side ? 1.78f : 3.25f,
                back = entry.side ? 1.84f : 1.85f, front = 1.4f, height = entry.side ? .018f : .09f };
            // The landing closes the whole space beside the stair and under the canopy.
            // A thin connector from a distant marker left an unintended patch of lawn here.
            if (Existing(pad.point, out float existing)) pad.height = existing;
            landings.Add(pad);
        }
        foreach (var pad in landings)
        {
            int across = Mathf.CeilToInt(pad.halfWidth * 2 / .4f), along = Mathf.CeilToInt((pad.back + pad.front) / .4f);
            Vector3 P(int x, int z) => pad.point + pad.right * Mathf.Lerp(-pad.halfWidth, pad.halfWidth, (float)x / across)
                + pad.outward * Mathf.Lerp(-pad.back, pad.front, (float)z / along);
            for (int x = 0; x < across; x++) for (int z = 0; z < along; z++)
            {
                var corners = new[] { P(x, z), P(x + 1, z), P(x + 1, z + 1), P(x, z + 1) };
                Vector3 middle = corners.Aggregate(Vector3.zero, (sum, p) => sum + p) * .25f;
                if (corners.All(p => Existing(p, out _)) && Existing(middle, out _)) continue;
                for (int c = 0; c < corners.Length; c++) corners[c].y = Height(corners[c]) - (Existing(corners[c], out _) ? .003f : 0);
                mesh.Face(apron, Vector3.up, corners);
                checks.Add((corners[0] + corners[1] + corners[2]) / 3);
                checks.Add((corners[0] + corners[2] + corners[3]) / 3);
            }
            var inside = new[] { new Vector2(-pad.halfWidth, -pad.back), new Vector2(pad.halfWidth, -pad.back),
                new Vector2(pad.halfWidth, pad.front), new Vector2(-pad.halfWidth, pad.front) };
            var outside = new[] { new Vector2(-pad.halfWidth - .08f, -pad.back - .08f), new Vector2(pad.halfWidth + .08f, -pad.back - .08f),
                new Vector2(pad.halfWidth + .08f, pad.front + .08f), new Vector2(-pad.halfWidth - .08f, pad.front + .08f) };
            Vector3 Border(Vector2 p) => pad.point + pad.right * p.x + pad.outward * p.y;
            for (int side = 0; side < 4; side++)
            {
                int next = (side + 1) % 4;
                int count = Mathf.CeilToInt(Vector2.Distance(inside[side], inside[next]) / .4f);
                for (int part = 0; part < count; part++)
                {
                    float a = (float)part / count, b = (float)(part + 1) / count;
                    var ring = new[] { Border(Vector2.Lerp(inside[side], inside[next], a)), Border(Vector2.Lerp(inside[side], inside[next], b)),
                        Border(Vector2.Lerp(outside[side], outside[next], b)), Border(Vector2.Lerp(outside[side], outside[next], a)) };
                    if (ring.All(p => Existing(p, out _))) continue;
                    for (int c = 0; c < ring.Length; c++) ring[c].y = Height(ring[c]) - (Existing(ring[c], out _) ? .003f : 0);
                    ring[2].y = ring[1].y; ring[3].y = ring[0].y;
                    // A flush concrete edge closes the asphalt to the ground. It never forms
                    // a raised bar across an entrance, existing pavement or driveway.
                    mesh.Face(edge, Vector3.up, ring);
                    Vector3 normal = (ring[2] + ring[3]) * .5f - pad.point; normal.y = 0;
                    mesh.Face(edge, normal.normalized, ring[3], ring[2], new Vector3(ring[2].x, -.015f, ring[2].z),
                        new Vector3(ring[3].x, -.015f, ring[3].z));
                    checks.Add((ring[0] + ring[1] + ring[2]) / 3); checks.Add((ring[0] + ring[2] + ring[3]) / 3);
                }
            }
        }
        metadata.entranceLandingChecks = checks.ToArray();
        foreach (var entry in entries.OrderBy(e => e.address, StringComparer.Ordinal))
        {
            Vector3 start = entry.point; start.y = 0;
            if (!Clear(start)) start += entry.outward * .8f;
            Vector3[] path = Connect(start, entry.outward);
            if (path == null)
            {
                blocked.Add(entry.address + " @ " + start.ToString("F2") + (Clear(start) ? " · нет свободного соединения" : " · препятствие у входа"));
                Debug.Log("ACCESS_BLOCKED " + entry.address + " start=" + start.ToString("F2") + " goals=" + searchCandidates
                    + " startHits=" + ObstacleNames(start) + " outwardHits=" + ObstacleNames(start + entry.outward * 3));
                continue;
            }
            var samples = new List<Vector3>(); int previousChecks = checks.Count;
            for (int segment = 1; segment < path.Length; segment++)
            {
                Vector3 a = path[segment - 1], b = path[segment];
                int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .5f));
                Vector3 tangent = (b - a).normalized, right = new Vector3(tangent.z, 0, -tangent.x);
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, (float)i / count), q = Vector3.Lerp(a, b, (float)(i + 1) / count);
                    p.y = Height(p); q.y = Height(q);
                    if (samples.Count == 0) samples.Add(p); samples.Add(q);
                    bool coveredP = Existing(p, out _), coveredQ = Existing(q, out _);
                    if (coveredP && coveredQ && Existing((p + q) * .5f, out _)) continue;
                    // A crossing segment must meet the actual surface edge. Testing only its
                    // midpoint left up to 25 cm of lawn at the end of an otherwise paved route.
                    Vector3 OnSurface(Vector3 point) { point.y = Height(point) + (Existing(point, out _) ? -.003f : .003f); return point; }
                    const int across = 8;
                    for (int strip = 0; strip < across; strip++)
                    {
                        float left = Mathf.Lerp(-Width * .5f, Width * .5f, (float)strip / across);
                        float rightEdge = Mathf.Lerp(-Width * .5f, Width * .5f, (float)(strip + 1) / across);
                        var corners = new[] { OnSurface(p + right * left), OnSurface(p + right * rightEdge),
                            OnSurface(q + right * rightEdge), OnSurface(q + right * left) };
                        mesh.Face(paving, Vector3.up, corners);
                        checks.Add((corners[0] + corners[1] + corners[2]) / 3);
                        checks.Add((corners[0] + corners[2] + corners[3]) / 3);
                    }
                    foreach (int side in new[] { -1, 1 })
                    {
                        Vector3 x = OnSurface(p + right * (side * Width * .5f)), y = OnSurface(q + right * (side * Width * .5f));
                        // Flush border; never a raised kerb across a service road or entrance.
                        mesh.Face(edge, right * side, x, y, new Vector3(y.x, -.015f, y.z), new Vector3(x.x, -.015f, x.z));
                    }
                }
            }
            foreach (var turn in path.Skip(1).Take(Mathf.Max(0, path.Length - 2)))
                if (!Existing(turn, out _) && !landings.Any(pad => pad.Contains(turn)))
                {
                    float height = Height(turn);
                    mesh.Cylinder(paving, turn - Vector3.up * .015f, turn + Vector3.up * height, Width * .5f, Width * .5f, 12);
                    checks.Add(turn + Vector3.up * height);
                }
            routes.Add(new ResidentialAccess.Route { address = entry.address, points = samples.ToArray(), usesExistingSurface = checks.Count == previousChecks });
        }
        if (checks.Count > 0)
        {
            var part = mesh.Create("ResidentialAccessSurfaces", "Мощёные подходы к существующим входам");
            part.transform.SetParent(root.transform, false);
            var collider = part.AddComponent<MeshCollider>(); collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            metadata.surfaces = new[] { collider };
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic);
        }
        else metadata.surfaces = Array.Empty<MeshCollider>();
        metadata.routes = routes.ToArray(); metadata.surfaceChecks = checks.ToArray();
        metadata.connectedEntrances = routes.Count; metadata.blockedEntrances = blocked.ToArray();
        Physics.SyncTransforms();
        Debug.Log("RESIDENTIAL_ACCESS entrances=" + entries.Count + " connected=" + routes.Count + " blocked=" + blocked.Count
            + " newSurfaceChecks=" + checks.Count + (blocked.Count > 0 ? " blockedNames=" + string.Join(";", blocked) : ""));
    }

    private static bool Existing(Vector3 p, out float height)
    {
        height = -.1f; bool found = false;
        var ray = new Ray(new Vector3(p.x, 2, p.z), Vector3.down);
        foreach (var collider in paved)
        {
            var bounds = pavedBounds[collider];
            if (p.x < bounds.min.x || p.x > bounds.max.x || p.z < bounds.min.z || p.z > bounds.max.z) continue;
            if (collider.Raycast(ray, out var hit, 2.4f) && hit.normal.y > .85f && hit.point.y < .4f && hit.point.y > height
                && hit.triangleIndex >= 0)
            {
                bool accepted = false;
                foreach (var range in surfaceRanges[collider])
                    if (hit.triangleIndex < range.lastTriangle) { accepted = range.accepted; break; }
                if (!accepted) continue;
                height = hit.point.y; found = true;
            }
        }
        return found;
    }
    private static float Height(Vector3 p)
    {
        var key = new Vector2Int(Mathf.RoundToInt(p.x * 1000), Mathf.RoundToInt(p.z * 1000));
        if (heightCache.TryGetValue(key, out float height)) return height;
        height = SurfaceHeight(p); heightCache[key] = height; return height;
    }
    private static float SurfaceHeight(Vector3 p)
    {
        if (Existing(p, out float existing)) return existing;
        var containing = landings.FirstOrDefault(pad => pad.Contains(p));
        float baseHeight = containing != null ? containing.height : .14f;
        float best = 1.5f, target = baseHeight;
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI / 4;
            Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            for (float distance = .3f; distance <= 1.5f; distance += .3f)
                if (distance - .3f < best && Existing(p + direction * distance, out float h))
                {
                    // Locate the pavement edge continuously. Rounding distance to 30-cm
                    // rings produced visible 3-cm steps when connecting to a higher walk.
                    float low = Mathf.Max(0, distance - .3f), high = distance;
                    for (int refine = 0; refine < 7; refine++)
                    {
                        float middle = (low + high) * .5f;
                        if (Existing(p + direction * middle, out float refined)) { high = middle; h = refined; }
                        else low = middle;
                    }
                    if (high < best) { best = high; target = Mathf.Lerp(h, baseHeight, high / 1.5f); }
                    break;
                }
        }
        if (containing != null) return target;
        foreach (var pad in landings)
        {
            float distance = Vector3.Distance(new Vector3(p.x, 0, p.z), pad.Closest(p));
            if (distance < best) { best = distance; target = Mathf.Lerp(Height(pad.Closest(p)), .14f, distance / 1.5f); }
        }
        return target;
    }
    private static bool Clear(Vector3 p)
    {
        // Broad bounds of a city-wide non-convex building batch cover most of the map.
        // Query the real collider triangles; ClosestPoint is unsupported for these colliders.
        int count = Physics.OverlapBoxNonAlloc(new Vector3(p.x, 1.05f, p.z), new Vector3(.72f, .50f, .72f),
            overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (obstacles.Contains(overlap[i])) return false;
        if (count == overlap.Length) return false;
        return true;
    }
    private static bool ClearSegment(Vector3 a, Vector3 b)
    {
        int count = Mathf.CeilToInt(Vector3.Distance(a, b) / .45f);
        for (int i = 0; i <= count; i++) if (!Clear(Vector3.Lerp(a, b, count == 0 ? 0 : (float)i / count))) return false;
        return true;
    }
    private static string ObstacleNames(Vector3 p)
    {
        int count = Physics.OverlapBoxNonAlloc(new Vector3(p.x, 1.05f, p.z), new Vector3(.72f, .50f, .72f),
            overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        return string.Join(" | ", overlap.Take(count).Where(c => obstacles.Contains(c)).Select(c =>
            c.transform.root.name + "/" + c.name + (c is MeshCollider m && m.sharedMesh ? "[" + m.sharedMesh.name + "]" : "")).Distinct());
    }
    private static Vector3[] Connect(Vector3 start, Vector3 outward)
    {
        searchCandidates = 0;
        var goals = new List<Vector3>();
        if (Existing(start, out _))
        {
            Vector3 end = start + outward * 2;
            if (ClearSegment(start, end) && Existing(end, out _)) return new[] { start, end };
        }
        // Stop at the first free existing surface, not an arbitrary distant street point.
        for (float radius = 2; radius <= 52; radius += 2)
            for (int i = 0; i < 48; i++)
            {
                float angle = i * Mathf.PI / 24;
                Vector3 goal = start + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                if (!Existing(goal, out _) || !Clear(goal)) continue;
                searchCandidates++;
                if (!goals.Any(p => (p - goal).sqrMagnitude < 1)) goals.Add(goal);
                if (ClearSegment(start, goal)) return new[] { start, goal };
                Vector3 turn = start + outward * Mathf.Min(3, radius * .5f);
                if (ClearSegment(start, turn) && ClearSegment(turn, goal)) return new[] { start, turn, goal };
                if (!ClearSegment(start, turn)) continue;
                Vector3 along = new Vector3(outward.z, 0, -outward.x);
                foreach (float offset in new[] { -3f, 3f, -6f, 6f, -9f, 9f })
                {
                    Vector3 second = turn + along * offset;
                    if (ClearSegment(turn, second) && ClearSegment(second, goal)) return new[] { start, turn, second, goal };
                }
            }
        return Search(start, goals);
    }

    private static void ResolveAnnexEntrance(Entrance entry)
    {
        // The existing generic OSM annex encloses the villa's designed gate position.
        // Its long exterior facade already has a rendered door and 2.3 x 1.5 m entrance pad
        // (StreetBuilder.MakeBuilding). Connect to that saved exterior door, preserving both buildings.
        var data = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        var annex = data.buildings.FirstOrDefault(b => b.id == 520207927);
        if (annex == null) return;
        var points = annex.points.Select(p => p.V).ToArray();
        float area = 0;
        for (int i = 0; i < points.Length; i++) area += points[i].x * points[(i + 1) % points.Length].z - points[(i + 1) % points.Length].x * points[i].z;
        float best = float.MaxValue; Vector3 chosen = entry.point, outward = entry.outward;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 a = points[i], b = points[(i + 1) % points.Length], direction = (b - a).normalized;
            if (Vector3.Distance(a, b) <= 12) continue;
            Vector3 normal = new Vector3(direction.z, 0, -direction.x) * (area > 0 ? 1 : -1);
            if (Vector3.Dot(normal, entry.outward) < .8f) continue;
            Vector3 padEdge = (a + b) * .5f + normal * 1.5f;
            float distance = (padEdge - entry.point).sqrMagnitude;
            if (distance >= best) continue;
            best = distance; chosen = padEdge; outward = normal;
        }
        if (best > 16) return;
        Debug.Log("ACCESS_ANNEX_RESOLVED villa=114788560 annex=520207927 original=" + entry.point.ToString("F2") + " exteriorDoorApproach=" + chosen.ToString("F2"));
        entry.point = chosen; entry.outward = outward;
        entry.address += " / существующий наружный вход пристройки 520207927";
    }

    private static Vector3[] Search(Vector3 start, List<Vector3> goals)
    {
        if (goals.Count == 0 || !Clear(start)) return null;
        const float cell = 1.25f;
        var zero = Vector2Int.zero;
        Vector3 Position(Vector2Int p) => start + new Vector3(p.x * cell, 0, p.y * cell);
        float Heuristic(Vector3 p) => Mathf.Sqrt(goals.Min(g => (p - g).sqrMagnitude));
        var comparer = Comparer<(float score, int order, Vector2Int point)>.Create((a, b) => {
            int c = a.score.CompareTo(b.score); return c != 0 ? c : a.order.CompareTo(b.order);
        });
        var open = new SortedSet<(float score, int order, Vector2Int point)>(comparer);
        var costs = new Dictionary<Vector2Int, float> { [zero] = 0 };
        var previous = new Dictionary<Vector2Int, Vector2Int>();
        var clear = new Dictionary<Vector2Int, bool> { [zero] = true };
        var closed = new HashSet<Vector2Int>(); int order = 0;
        open.Add((Heuristic(start), order++, zero));
        var offsets = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(-1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1) };
        while (open.Count > 0 && closed.Count < 26000)
        {
            var item = open.Min; open.Remove(item); var current = item.point;
            if (!closed.Add(current)) continue;
            Vector3 p = Position(current);
            if (current != zero && Existing(p, out _))
            {
                var path = new List<Vector3> { p };
                while (previous.TryGetValue(current, out var parent)) { current = parent; path.Add(Position(current)); }
                path.Reverse();
                // The grid is a search aid only. Save the longest clear straight spans, not its zigzag.
                var simplified = new List<Vector3> { path[0] }; int index = 0;
                while (index < path.Count - 1)
                {
                    int next = path.Count - 1;
                    while (next > index + 1 && !ClearSegment(path[index], path[next])) next--;
                    simplified.Add(path[next]); index = next;
                }
                Debug.Log("ACCESS_LOCAL_SEARCH start=" + start.ToString("F2") + " visited=" + closed.Count
                    + " gridPoints=" + path.Count + " savedPoints=" + simplified.Count + " length=" + costs[item.point]);
                return simplified.ToArray();
            }
            foreach (var offset in offsets)
            {
                var next = current + offset;
                if (closed.Contains(next) || next.sqrMagnitude * cell * cell > 72 * 72) continue;
                Vector3 q = Position(next);
                if (!clear.TryGetValue(next, out bool available)) { available = Clear(q); clear[next] = available; }
                if (!available) continue;
                float cost = costs[current] + offset.magnitude * cell;
                if (cost > 150 || costs.TryGetValue(next, out float old) && old <= cost) continue;
                if (!ClearSegment(p, q)) continue;
                costs[next] = cost; previous[next] = current;
                open.Add((cost + Heuristic(q), order++, next));
            }
        }
        Debug.Log("ACCESS_LOCAL_SEARCH_FAILED start=" + start.ToString("F2") + " visited=" + closed.Count);
        return null;
    }
}
