using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Parking footprints come from the saved OSM evidence. Cars are editable scenery,
// and their position is deliberately not represented as a surveyed parking-space layout.
internal static class ParkingEnvironmentBuilder
{
    [Serializable] private sealed class Document
    {
        public SiteRecord[] sites;
        public RoadRecord[] extraServiceRoads;
        public PhotoRow[] photoSupportedRows;
    }
    [Serializable] private sealed class RoadRecord
    {
        public long id;
        public float width;
        public StreetBuilder.Point[] points;
    }
    [Serializable] private sealed class SiteRecord
    {
        public long osmId;
        public long accessRoadId;
        public string id, address, evidenceLevel, accuracyNote;
        public string[] sourceUrls;
        public StreetBuilder.Point[] boundary, accessRoute;
        public float accessWidth;
        public int[] kerbEdges, openEdges;
    }
    [Serializable] private sealed class PhotoRow
    {
        public string id, address, evidenceLevel, accuracyNote;
        public string[] sourceUrls;
        public VehicleRecord[] vehicles;
    }
    [Serializable] private sealed class VehicleRecord
    {
        public float x, z, heading;
        public int style;
    }

    private static Material asphalt, kerb;
    private static readonly List<Vector3> occupied = new List<Vector3>();
    private static Transform[] entrances;
    private static Dictionary<long, RoadRecord> serviceRoads;
    private const float Height = .065f;

    internal static void Generate(Transform parent)
    {
        EnvironmentMesh.EnsureFolders();
        var document = JsonUtility.FromJson<Document>(File.ReadAllText("Assets/StreetSource/parking_evidence.json"));
        if (document.sites == null || document.sites.Length == 0)
            throw new InvalidOperationException("No geographically verified parking footprints");
        asphalt = EnvironmentMesh.Material("MappedParkingAsphalt", Color.white);
        StreetSurfaceLibrary.ApplyNamed(asphalt, "Asphalt");
        kerb = EnvironmentMesh.Material("MappedParkingKerb", Color.white);
        StreetSurfaceLibrary.ApplyNamed(kerb, "RoadKerb");
        occupied.Clear();
        entrances = UnityEngine.Object.FindObjectsByType<Transform>().Where(t =>
            t.name.StartsWith("Access · ", StringComparison.Ordinal) || t.name.StartsWith("Подход ", StringComparison.Ordinal)).ToArray();
        var root = new GameObject("Парковки · подтверждённые контуры OSM").transform;
        root.SetParent(parent, false);
        serviceRoads = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"))
            .roads.Where(r => r.kind == "service").ToDictionary(r => r.id,
                r => new RoadRecord { id = r.id, width = r.width, points = r.points });
        foreach (var road in document.extraServiceRoads ?? Array.Empty<RoadRecord>()) serviceRoads[road.id] = road;
        BuildMissingServiceRoads(root, document.extraServiceRoads ?? Array.Empty<RoadRecord>());
        Physics.SyncTransforms();
        int cars = 0, checks = 0;
        foreach (var record in document.sites.OrderBy(s => s.id))
        {
            var polygon = CleanPolygon(record.boundary);
            if (polygon.Length < 3) throw new InvalidOperationException("Invalid parking boundary " + record.id);
            var siteRoot = new GameObject("Парковка OSM " + record.id + " · " + record.address);
            siteRoot.transform.SetParent(root, false);
            var site = siteRoot.AddComponent<ParkingSite>();
            site.osmId = record.osmId; site.address = record.address; site.evidenceLevel = record.evidenceLevel;
            site.sourceUrls = record.sourceUrls ?? Array.Empty<string>();
            site.accuracyNote = record.accuracyNote + " Расстановка автомобилей демонстрационная: только внутри подтверждённого покрытия, с сохранением входов. Разметка не добавлена без фотографии.";
            site.accuracyNote += " Примыкания открытых сторон к оси существующего проезда — оценочная реконструкция маневрового фартука; исходный контур парковки сохранён.";
            site.boundary = polygon;
            site.accessRoute = ExpandShortAccess((record.accessRoute ?? Array.Empty<StreetBuilder.Point>()).Select(p => p.V).ToArray(), polygon);
            var mesh = new EnvironmentMesh();
            var surfaceChecks = new List<Vector3>(); var accessChecks = new List<Vector3>();
            var transitionChecks = new List<Vector3>();
            Triangulate(mesh, polygon, record.osmId);
            SampleInterior(polygon, surfaceChecks);
            BuildAccess(mesh, site.accessRoute, Mathf.Max(3, record.accessWidth), accessChecks);
            BuildOpenEdgeTransitions(mesh, record, polygon, site, transitionChecks);
            surfaceChecks.AddRange(transitionChecks); site.transitionChecks = transitionChecks.ToArray();
            BuildConfirmedKerbs(mesh, polygon, record.kerbEdges ?? Array.Empty<int>());
            var surface = mesh.Create("ParkingSurface_" + record.id, "Асфальт парковки и открытый въезд");
            surface.transform.SetParent(siteRoot.transform, false);
            var collider = surface.AddComponent<MeshCollider>(); collider.sharedMesh = surface.GetComponent<MeshFilter>().sharedMesh;
            GameObjectUtility.SetStaticEditorFlags(surface, StaticEditorFlags.BatchingStatic);
            site.surfaces = new[] { collider }; site.surfaceChecks = surfaceChecks.ToArray();
            site.accessChecks = accessChecks.ToArray(); site.connectedToServiceRoad = site.accessRoute.Length >= 2;
            Physics.SyncTransforms();
            site.vehicles = PlaceCars(site, polygon);
            site.inspectionViews = Views(siteRoot.transform, polygon, site.accessRoute);
            cars += site.vehicles.Length; checks += site.surfaceChecks.Length;
        }
        foreach (var row in document.photoSupportedRows ?? Array.Empty<PhotoRow>())
        {
            var site = BuildPhotoSupportedRow(root, row);
            cars += site.vehicles.Length; checks += site.surfaceChecks.Length;
        }
        Debug.Log("MAPPED_PARKING sites=" + document.sites.Length + " vehicles=" + cars + " surfaceChecks=" + checks
            + " restoredServiceRoads=" + (document.extraServiceRoads ?? Array.Empty<RoadRecord>()).Length
            + " openEdgeSegments=" + UnityEngine.Object.FindObjectsByType<ParkingSite>().Sum(s => s.connectedOpenEdgeSegments));
    }

    private static Vector3[] CleanPolygon(StreetBuilder.Point[] points)
    {
        var polygon = (points ?? Array.Empty<StreetBuilder.Point>()).Select(p => p.V).ToList();
        if (polygon.Count > 1 && Vector3.Distance(polygon[0], polygon[polygon.Count - 1]) < .01f) polygon.RemoveAt(polygon.Count - 1);
        for (int i = polygon.Count - 1; i > 0; i--) if (Vector3.Distance(polygon[i], polygon[i - 1]) < .01f) polygon.RemoveAt(i);
        return polygon.ToArray();
    }

    private static float Cross(Vector3 a, Vector3 b) => a.x * b.z - a.z * b.x;
    private static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c) =>
        Cross(b - a, p - a) >= -.00001f && Cross(c - b, p - b) >= -.00001f && Cross(a - c, p - c) >= -.00001f;

    private static void Triangulate(EnvironmentMesh mesh, Vector3[] polygon, long id)
    {
        var remaining = Enumerable.Range(0, polygon.Length).ToList();
        float signed = 0;
        for (int i = 0; i < polygon.Length; i++) signed += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
        if (signed < 0) remaining.Reverse();
        int guard = polygon.Length * polygon.Length;
        while (remaining.Count >= 3 && guard-- > 0)
        {
            bool clipped = false;
            for (int k = 0; k < remaining.Count; k++)
            {
                int ia = remaining[(k + remaining.Count - 1) % remaining.Count], ib = remaining[k], ic = remaining[(k + 1) % remaining.Count];
                Vector3 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                if (Cross(b - a, c - b) <= .00001f) continue;
                if (remaining.Any(j => j != ia && j != ib && j != ic && InTriangle(polygon[j], a, b, c))) continue;
                mesh.Face(asphalt, Vector3.up, a + Vector3.up * Height, b + Vector3.up * Height, c + Vector3.up * Height);
                remaining.RemoveAt(k); clipped = true; break;
            }
            if (!clipped) throw new InvalidOperationException("Cannot triangulate parking OSM " + id);
        }
        // Buried sides close the asphalt slab without adding a kerb across its open vehicle edge.
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
            Vector3 normal = new Vector3(b.z - a.z, 0, a.x - b.x).normalized * (signed >= 0 ? 1 : -1);
            mesh.Face(asphalt, normal, a - Vector3.up * .015f, b - Vector3.up * .015f,
                b + Vector3.up * Height, a + Vector3.up * Height);
        }
    }

    private static void SampleInterior(Vector3[] polygon, List<Vector3> checks)
    {
        float minX = polygon.Min(p => p.x), minZ = polygon.Min(p => p.z);
        for (float x = minX + .35f; x < polygon.Max(p => p.x); x += 1.25f)
            for (float z = minZ + .35f; z < polygon.Max(p => p.z); z += 1.25f)
            {
                var point = new Vector3(x, Height, z);
                if (ParkingContains(polygon, point, .15f)) checks.Add(point);
            }
        if (checks.Count == 0)
        {
            Vector3 center = polygon.Aggregate(Vector3.zero, (sum, p) => sum + p) / polygon.Length;
            if (GeographicLandscape.Contains(polygon, center)) checks.Add(center + Vector3.up * Height);
        }
    }

    private static Vector3[] ExpandShortAccess(Vector3[] points, Vector3[] polygon)
    {
        if (points.Length != 2 || (points[1] - points[0]).sqrMagnitude >= .04f) return points;
        // A service axis may nearly coincide with the mapped boundary. The evidence's
        // centimetre-long axis-to-edge line is not a buildable transition. Keep the source
        // data intact and widen this derived ramp within the existing service-road width.
        Vector3 target = points[1], nearest = target, inward = Vector3.zero;
        float best = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i], delta = polygon[(i + 1) % polygon.Length] - a;
            if (delta.sqrMagnitude < .000001f) continue;
            Vector3 boundaryPoint = a + delta * Mathf.Clamp01(Vector3.Dot(target - a, delta) / delta.sqrMagnitude);
            float distance = (target - boundaryPoint).sqrMagnitude;
            if (distance >= best) continue;
            Vector3 normal = new Vector3(delta.z, 0, -delta.x).normalized;
            if (!GeographicLandscape.Contains(polygon, boundaryPoint + normal * .15f)) normal = -normal;
            best = distance; nearest = boundaryPoint; inward = normal;
        }
        if (inward.sqrMagnitude < .5f) throw new InvalidOperationException("Cannot resolve parking access boundary");
        return new[] { nearest - inward * .80f, nearest + inward * .10f };
    }

    private static void BuildAccess(EnvironmentMesh mesh, Vector3[] points, float width, List<Vector3> checks)
    {
        for (int i = 1; i < points.Length; i++)
        {
            Vector3 a = points[i - 1], b = points[i], delta = b - a;
            if (delta.sqrMagnitude < .000001f) continue;
            Vector3 side = new Vector3(delta.z, 0, -delta.x).normalized * width * .5f;
            // Taper from the service asphalt to the parking slab, with no transverse kerb.
            float fromHeight = i == 1 ? .038f : Height;
            mesh.Face(asphalt, Vector3.up, a - side + Vector3.up * fromHeight, b - side + Vector3.up * Height,
                b + side + Vector3.up * Height, a + side + Vector3.up * fromHeight);
            int count = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / .65f));
            for (int step = 0; step <= count; step++) checks.Add(Vector3.Lerp(a, b, (float)step / count)
                + Vector3.up * Mathf.Lerp(fromHeight, Height, (float)step / count));
        }
    }

    private static void BuildOpenEdgeTransitions(EnvironmentMesh mesh, SiteRecord record, Vector3[] polygon,
        ParkingSite site, List<Vector3> checks)
    {
        if (!serviceRoads.TryGetValue(record.accessRoadId, out var road)) return;
        Vector3[] line = road.points.Select(p => p.V).ToArray();
        if (line.Length < 2) return;
        float halfWidth = Mathf.Max(3, road.width) * .5f;
        float recordedAccessLength = 0;
        var recordedAccess = record.accessRoute ?? Array.Empty<StreetBuilder.Point>();
        for (int i = 1; i < recordedAccess.Length; i++)
            recordedAccessLength += Vector3.Distance(recordedAccess[i - 1].V, recordedAccess[i].V);
        float maximumGap = Mathf.Min(8, recordedAccessLength + .5f);
        Vector3 Axis(Vector3 point)
        {
            Vector3 nearest = line[0]; float distance = float.MaxValue;
            for (int i = 1; i < line.Length; i++)
            {
                Vector3 a = line[i - 1], delta = line[i] - a;
                Vector3 q = a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / Mathf.Max(.000001f, delta.sqrMagnitude));
                if ((q - point).sqrMagnitude >= distance) continue;
                nearest = q; distance = (q - point).sqrMagnitude;
            }
            return nearest;
        }
        Vector3 Landing(Vector3 boundary)
        {
            Vector3 axis = Axis(boundary), offset = boundary - axis;
            if (offset.sqrMagnitude < .000001f) return boundary;
            float coefficient = Mathf.Clamp(Mathf.Min(halfWidth - .08f, offset.magnitude - .90f), -halfWidth + .08f, halfWidth - .08f);
            return axis + offset.normalized * coefficient;
        }
        Vector3 RoadDirection(Vector3 point)
        {
            Vector3 direction = line[1] - line[0]; float distance = float.MaxValue;
            for (int i = 1; i < line.Length; i++)
            {
                Vector3 a = line[i - 1], delta = line[i] - a;
                if (delta.sqrMagnitude < .000001f) continue;
                Vector3 q = a + delta * Mathf.Clamp01(Vector3.Dot(point - a, delta) / delta.sqrMagnitude);
                if ((q - point).sqrMagnitude >= distance) continue;
                distance = (q - point).sqrMagnitude; direction = delta;
            }
            return direction.normalized;
        }
        foreach (int edge in record.openEdges ?? Array.Empty<int>())
        {
            if (edge < 0 || edge >= polygon.Length) continue;
            Vector3 from = polygon[edge], to = polygon[(edge + 1) % polygon.Length];
            Vector3 edgeDirection = (to - from).normalized, edgeMiddle = (from + to) * .5f;
            Vector3 roadDirection = RoadDirection(edgeMiddle), entryAxis = Axis(edgeMiddle);
            bool endOn = Mathf.Abs(Vector3.Dot(edgeDirection, roadDirection)) < .55f;
            Vector3 outward = new Vector3(edgeDirection.z, 0, -edgeDirection.x);
            if (GeographicLandscape.Contains(polygon, edgeMiddle + outward * .15f)) outward = -outward;
            if (Vector3.Dot(roadDirection, outward) < 0) roadDirection = -roadDirection;
            Vector3 roadRight = new Vector3(roadDirection.z, 0, -roadDirection.x);
            Vector3 EndOnLanding(Vector3 boundary) => entryAxis + roadDirection * .90f
                + roadRight * Mathf.Clamp(Vector3.Dot(boundary - entryAxis, roadRight), -halfWidth + .08f, halfWidth - .08f);
            int count = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .75f));
            for (int part = 0; part < count; part++)
            {
                Vector3 a = Vector3.Lerp(from, to, (float)part / count), b = Vector3.Lerp(from, to, (float)(part + 1) / count);
                Vector3 center = (a + b) * .5f;
                // A nearest road on the inside of this edge is reached through the parking,
                // not through an exterior apron. Its join belongs to the other open edge.
                if (Vector3.Dot(Axis(center) - center, outward) < -.20f)
                { site.rejectedOpenEdgeSegments++; continue; }
                // Only close a small local join to the actual mapped drive. A remote road is
                // insufficient evidence for paving a courtyard or cutting through a building.
                if (Vector3.Distance(center, Axis(center)) > halfWidth + maximumGap)
                { site.rejectedOpenEdgeSegments++; continue; }
                // When the drive enters a parking edge end-on, radial projection runs ALONG
                // that edge and produces near-vertical slivers or a self-crossing quad.
                // Flare to a real road-width cross-section 90 cm outside the parking instead.
                Vector3 x = endOn ? EndOnLanding(a) : Landing(a), y = endOn ? EndOnLanding(b) : Landing(b);
                bool blocked = Physics.RaycastAll(center + Vector3.up * .6f,
                    ((x + y) * .5f - center).normalized, Vector3.Distance(center, (x + y) * .5f), ~0,
                    QueryTriggerInteraction.Ignore).Any(hit => BuildingObstacle(hit.collider));
                if (blocked || CrossesExistingFootway(center, (x + y) * .5f))
                { site.rejectedOpenEdgeSegments++; continue; }
                Vector3 aTop = a + Vector3.up * Height, bTop = b + Vector3.up * Height;
                Vector3 xTop = x + Vector3.up * .038f, yTop = y + Vector3.up * .038f;
                if (Vector3.Cross(bTop - aTop, xTop - aTop).sqrMagnitude < .0000001f)
                { site.rejectedOpenEdgeSegments++; continue; }
                Vector3 normal = Vector3.Cross(bTop - aTop, xTop - aTop).normalized;
                if (normal.y < 0) normal = -normal;
                if ((xTop - yTop).sqrMagnitude < .000001f)
                    mesh.Face(asphalt, normal, aTop, bTop, xTop);
                else mesh.Face(asphalt, normal, aTop, bTop, yTop, xTop);
                for (int sample = 1; sample <= 3; sample++)
                    checks.Add(Vector3.Lerp((aTop + bTop) * .5f, (xTop + yTop) * .5f, sample * .25f));
                site.connectedOpenEdgeSegments++;
            }
        }
    }

    private static bool CrossesExistingFootway(Vector3 from, Vector3 to)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(from, to) / .4f));
        for (int step = 0; step <= steps; step++)
        {
            Vector3 point = Vector3.Lerp(from, to, (float)step / steps);
            foreach (var hit in Physics.RaycastAll(point + Vector3.up, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!(hit.collider is MeshCollider collider) || !collider.sharedMesh || hit.triangleIndex < 0 || hit.normal.y < .85f) continue;
                var renderer = collider.GetComponent<MeshRenderer>(); if (!renderer) continue;
                int end = 0;
                for (int index = 0; index < collider.sharedMesh.subMeshCount; index++)
                {
                    end += (int)collider.sharedMesh.GetIndexCount(index) / 3;
                    if (hit.triangleIndex >= end) continue;
                    var materials = renderer.sharedMaterials;
                    string name = index < materials.Length && materials[index] ? materials[index].name : "";
                    if (name == "Path" || name == "Pavement" || name == "WarmPaving"
                        || name.Contains("Footpath") || name.Contains("RedPaving") || name.Contains("ResidentialAccessPaving")) return true;
                    break;
                }
            }
        }
        return false;
    }

    private static void BuildConfirmedKerbs(EnvironmentMesh mesh, Vector3[] polygon, int[] edges)
    {
        foreach (int edge in edges)
        {
            if (edge < 0 || edge >= polygon.Length) continue;
            Vector3 a = polygon[edge], b = polygon[(edge + 1) % polygon.Length], direction = b - a;
            int count = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude));
            for (int i = 0; i < count; i++)
            {
                Vector3 x = Vector3.Lerp(a, b, (float)i / count), y = Vector3.Lerp(a, b, (float)(i + 1) / count);
                mesh.Box(kerb, (x + y) * .5f + Vector3.up * .07f,
                    new Vector3(.15f, .20f, Vector3.Distance(x, y) - .008f), Quaternion.LookRotation(direction));
            }
        }
    }

    private static void BuildMissingServiceRoads(Transform parent, RoadRecord[] roads)
    {
        foreach (var road in roads)
        {
            var points = (road.points ?? Array.Empty<StreetBuilder.Point>()).Select(p => p.V).ToArray();
            if (points.Length < 2) continue;
            var mesh = new EnvironmentMesh();
            mesh.Path(asphalt, null, points, Mathf.Max(3, road.width), .035f);
            var part = mesh.Create("ParkingSurface_Service_" + road.id, "Дворовой проезд OSM " + road.id);
            part.transform.SetParent(parent, false);
            var collider = part.AddComponent<MeshCollider>(); collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic);
        }
    }

    private static StreetVehicle[] PlaceCars(ParkingSite site, Vector3[] polygon)
    {
        int longest = Enumerable.Range(0, polygon.Length).OrderByDescending(i =>
            (polygon[(i + 1) % polygon.Length] - polygon[i]).sqrMagnitude).First();
        Vector3 across = (polygon[(longest + 1) % polygon.Length] - polygon[longest]).normalized;
        Vector3 forward = new Vector3(across.z, 0, -across.x);
        float minU = polygon.Min(p => Vector3.Dot(p, across)), maxU = polygon.Max(p => Vector3.Dot(p, across));
        float minV = polygon.Min(p => Vector3.Dot(p, forward)), maxV = polygon.Max(p => Vector3.Dot(p, forward));
        bool parallel = maxV - minV < 5.4f;
        Vector3 heading = parallel ? across : forward;
        float spacing = parallel ? 6.2f : 3.4f;
        var cars = new List<StreetVehicle>();
        int target = Mathf.Clamp(Mathf.FloorToInt(Area(polygon) / 65), 1, 4);
        // Leave the first and last part of the strip empty, and avoid dense repetitive rows.
        for (float u = minU + (parallel ? 3.3f : 1.8f); u < maxU - (parallel ? 3.0f : 1.6f) && cars.Count < target; u += spacing)
        {
            Vector3 candidate = across * u + forward * ((minV + maxV) * .5f);
            if (!CanPlace(candidate, heading, polygon)) { site.rejectedVehiclePlacements++; continue; }
            // The tyre bottom is local y=.020; Place() adds .045. Together they meet the .065 surface.
            var car = StreetVehicleBuilder.Place(site.transform, candidate + Vector3.up * (Height - .065f), heading,
                (int)((site.osmId + cars.Count) % 3), false);
            cars.Add(car.GetComponent<StreetVehicle>()); occupied.Add(candidate);
        }
        return cars.ToArray();
    }

    private static bool CanPlace(Vector3 point, Vector3 heading, Vector3[] polygon)
    {
        Vector3 right = Vector3.Cross(Vector3.up, heading);
        foreach (int a in new[] { -1, 1 }) foreach (int b in new[] { -1, 1 })
            if (!ParkingContains(polygon, point + right * (a * 1.20f) + heading * (b * 2.40f), .08f)) return false;
        return ClearForVehicle(point, heading);
    }

    private static bool ClearForVehicle(Vector3 point, Vector3 heading)
    {
        if (occupied.Any(p => Vector3.Distance(p, point) < 3.1f)) return false;
        foreach (var entry in entrances)
        {
            Vector3 entrance = entry.position; entrance.y = 0;
            Vector3 direction = entry.name.StartsWith("Подход ", StringComparison.Ordinal) ? -entry.forward : entry.forward;
            Vector3 corridor = point - entrance;
            float along = Vector3.Dot(corridor, direction), lateral = Mathf.Abs(Vector3.Dot(corridor, new Vector3(direction.z, 0, -direction.x)));
            if (corridor.sqrMagnitude < 4.4f * 4.4f || along > -1 && along < 8 && lateral < 3) return false;
        }
        var hits = Physics.OverlapBox(point + Vector3.up * .85f, new Vector3(1.18f, .66f, 2.35f),
            Quaternion.LookRotation(heading), ~0, QueryTriggerInteraction.Ignore);
        return hits.All(c => c.bounds.max.y < .25f || c.isTrigger);
    }

    private static ParkingSite BuildPhotoSupportedRow(Transform parent, PhotoRow record)
    {
        var root = new GameObject("Парковочное использование по фото · " + record.address);
        root.transform.SetParent(parent, false);
        var site = root.AddComponent<ParkingSite>();
        site.address = record.address; site.evidenceLevel = record.evidenceLevel;
        site.sourceUrls = record.sourceUrls ?? Array.Empty<string>(); site.accuracyNote = record.accuracyNote;
        site.usesExistingSurface = true; site.boundary = Array.Empty<Vector3>();
        site.accessRoute = site.accessChecks = site.transitionChecks = Array.Empty<Vector3>(); site.connectedToServiceRoad = true;
        var checks = new List<Vector3>(); var colliders = new HashSet<MeshCollider>(); var cars = new List<StreetVehicle>();
        foreach (var placement in record.vehicles ?? Array.Empty<VehicleRecord>())
        {
            Vector3 point = new Vector3(placement.x, 0, placement.z);
            Vector3 heading = Quaternion.Euler(0, placement.heading, 0) * Vector3.forward;
            if (!ClearForVehicle(point, heading)) { site.rejectedVehiclePlacements++; continue; }
            Vector3 side = Vector3.Cross(Vector3.up, heading);
            var support = new List<RaycastHit>();
            foreach (int a in new[] { -1, 1 }) foreach (int b in new[] { -1, 1 })
            {
                Vector3 corner = point + side * (a * 1.20f) + heading * (b * 2.40f);
                if (ServiceAsphaltBelow(corner, out var hit)) support.Add(hit);
            }
            if (support.Count != 4 || support.Max(h => h.point.y) - support.Min(h => h.point.y) > .035f)
            { site.rejectedVehiclePlacements++; continue; }
            float height = support.Average(h => h.point.y);
            var car = StreetVehicleBuilder.Place(root.transform, point + Vector3.up * (height - .065f), heading,
                Mathf.Clamp(placement.style, 0, 2), false);
            cars.Add(car.GetComponent<StreetVehicle>()); occupied.Add(point);
            foreach (var hit in support) { checks.Add(hit.point); colliders.Add((MeshCollider)hit.collider); }
        }
        site.vehicles = cars.ToArray(); site.surfaceChecks = checks.ToArray(); site.surfaces = colliders.ToArray();
        if (cars.Count > 0)
        {
            Vector3 center = cars.Aggregate(Vector3.zero, (sum, car) => sum + car.transform.position) / cars.Count;
            var view = new GameObject("ParkingView · фото-подтверждённый асфальтовый двор").transform;
            view.SetParent(root.transform, false); view.localPosition = center + new Vector3(-12, 5.5f, 9);
            view.localRotation = Quaternion.LookRotation(center + Vector3.up * .6f - view.localPosition);
            site.inspectionViews = new[] { view };
        }
        else site.inspectionViews = Array.Empty<Transform>();
        Debug.Log("PHOTO_PARKING " + record.id + " accepted=" + cars.Count + " rejected=" + site.rejectedVehiclePlacements);
        return site;
    }

    private static bool ServiceAsphaltBelow(Vector3 point, out RaycastHit result)
    {
        foreach (var hit in Physics.RaycastAll(point + Vector3.up * 2, Vector3.down, 2.3f, ~0, QueryTriggerInteraction.Ignore)
            .OrderByDescending(h => h.point.y))
        {
            if (hit.normal.y < .85f || hit.point.y < -.011f || hit.point.y > .25f) continue;
            if (!(hit.collider is MeshCollider collider) || !collider.sharedMesh || hit.triangleIndex < 0)
            { result = default(RaycastHit); return false; }
            var renderer = collider.GetComponent<MeshRenderer>();
            if (!renderer) { result = default(RaycastHit); return false; }
            int end = 0;
            for (int i = 0; i < collider.sharedMesh.subMeshCount; i++)
            {
                end += (int)collider.sharedMesh.GetIndexCount(i) / 3;
                if (hit.triangleIndex >= end) continue;
                var materials = renderer.sharedMaterials;
                var material = i < materials.Length ? materials[i] : null;
                var texture = material && material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                if (texture && AssetDatabase.GetAssetPath(texture).Replace('\\', '/').Contains("/asphalt_01/"))
                { result = hit; return true; }
                break;
            }
            // A pavement or grass face over asphalt is not a valid place to park.
            result = default(RaycastHit); return false;
        }
        result = default(RaycastHit); return false;
    }

    private static bool ParkingContains(Vector3[] polygon, Vector3 point, float margin)
    {
        if (!GeographicLandscape.Contains(polygon, point)) return false;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector3 a = polygon[i], d = polygon[(i + 1) % polygon.Length] - a;
            Vector3 flat = point; flat.y = 0;
            if (Vector3.Distance(flat, a + d * Mathf.Clamp01(Vector3.Dot(flat - a, d) / Mathf.Max(.00001f, d.sqrMagnitude))) < margin) return false;
        }
        return true;
    }

    private static float Area(Vector3[] polygon)
    {
        float area = 0;
        for (int i = 0; i < polygon.Length; i++) area += Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
        return Mathf.Abs(area) * .5f;
    }

    private static Transform[] Views(Transform parent, Vector3[] polygon, Vector3[] access)
    {
        Vector3 center = InteriorFocus(polygon);
        float radius = Mathf.Clamp(Mathf.Sqrt(Area(polygon)), 7, 22);
        Vector3 toward = access.Length > 1 ? (access[0] - center).normalized : Vector3.back;
        if (toward.sqrMagnitude < .5f) toward = Vector3.back;
        Transform View(string name, Vector3 position, Vector3 target)
        {
            var view = new GameObject(name).transform; view.SetParent(parent, false);
            view.localPosition = position; view.localRotation = Quaternion.LookRotation(target - position);
            return view;
        }
        return new[] {
            View("ParkingView · общий вид", ClearOverview(center, toward, radius), center + Vector3.up * .7f),
            View("ParkingView · въезд", ClearEntranceView(center, toward, access),
                center + Vector3.up * .6f)
        };
    }

    private static Vector3 InteriorFocus(Vector3[] polygon)
    {
        Vector3 average = polygon.Aggregate(Vector3.zero, (sum, p) => sum + p) / polygon.Length;
        if (ParkingContains(polygon, average, .4f)) return average;
        // An arithmetic average can be inside the building enclosed by an L-shaped apron.
        // Select a real interior point, preferring a wide patch close to that average.
        Vector3 best = polygon[0]; float score = float.NegativeInfinity;
        for (float x = polygon.Min(p => p.x) + .25f; x < polygon.Max(p => p.x); x += .75f)
            for (float z = polygon.Min(p => p.z) + .25f; z < polygon.Max(p => p.z); z += .75f)
            {
                Vector3 p = new Vector3(x, 0, z);
                if (!GeographicLandscape.Contains(polygon, p)) continue;
                float distance = float.MaxValue;
                for (int i = 0; i < polygon.Length; i++)
                {
                    Vector3 a = polygon[i], delta = polygon[(i + 1) % polygon.Length] - a;
                    distance = Mathf.Min(distance, Vector3.Distance(p,
                        a + delta * Mathf.Clamp01(Vector3.Dot(p - a, delta) / Mathf.Max(.000001f, delta.sqrMagnitude))));
                }
                float value = distance - Vector3.Distance(p, average) * .04f;
                if (value > score) { best = p; score = value; }
            }
        return best;
    }

    private static bool BuildingObstacle(Collider collider) => collider && collider.enabled && !collider.isTrigger
        && collider.bounds.max.y > 2.4f && !(collider is CapsuleCollider) && collider.name != "Grass";

    private static bool CameraClear(Vector3 position, Vector3 target)
    {
        if (Physics.OverlapSphere(position, .35f, ~0, QueryTriggerInteraction.Ignore).Any(BuildingObstacle)) return false;
        Vector3 delta = target - position;
        return !Physics.RaycastAll(position, delta.normalized, Mathf.Max(0, delta.magnitude - .35f), ~0,
            QueryTriggerInteraction.Ignore).Any(hit => BuildingObstacle(hit.collider));
    }

    private static Vector3 ClearOverview(Vector3 center, Vector3 toward, float radius)
    {
        foreach (float height in new[] { 8f, 12f, 18f, 24f, 32f })
            foreach (float angle in new[] { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f })
            {
                Vector3 position = center + Quaternion.Euler(0, angle, 0) * toward * (radius + 7) + Vector3.up * height;
                foreach (var roof in Physics.RaycastAll(new Vector3(position.x, 80, position.z), Vector3.down, 80, ~0,
                    QueryTriggerInteraction.Ignore).Where(hit => BuildingObstacle(hit.collider) && hit.normal.y > .7f))
                    position.y = Mathf.Max(position.y, roof.point.y + 3);
                if (CameraClear(position, center + Vector3.up * .7f)) return position;
            }
        // A vertical view from within the confirmed footprint is clear even between tall facades.
        Vector3 overhead = center + toward * .2f + Vector3.up * 36;
        if (CameraClear(overhead, center + Vector3.up * .7f)) return overhead;
        throw new InvalidOperationException("Cannot find unobstructed parking overview at " + center);
    }

    private static Vector3 ClearEntranceView(Vector3 center, Vector3 toward, Vector3[] access)
    {
        Vector3 entrance = access.Length > 0 ? access[0] : center + toward * 5;
        foreach (float height in new[] { 1.72f, 2.6f, 4.2f })
            foreach (float setback in new[] { 2f, 0f, -1f })
            {
                Vector3 position = entrance + toward * setback + Vector3.up * height;
                if (CameraClear(position, center + Vector3.up * .6f)) return position;
            }
        return ClearOverview(center, toward, 7);
    }
}
