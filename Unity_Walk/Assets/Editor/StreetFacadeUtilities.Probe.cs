using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class StreetFacadeUtilities
{
    private sealed class Site
    {
        internal StreetBuilder.Building Source;
        internal Vector3 Center;
        internal float Area;
        internal bool Detailed;
        internal readonly List<Wall> Walls = new();
        internal Transform[] Entrances = Array.Empty<Transform>();
    }

    private sealed class Wall
    {
        internal int Index;
        internal Vector3 Start, Across, Out;
        internal float Height, Width;
        internal bool Veranda;
        internal WallProbe Geometry;
        internal Site Site;
        internal float Y(float height) => Start.y + height;
        internal Vector3 At(float x, float y, float depth = 0)
        {
            var point = Start + Across * x + Out * depth;
            point.y = y;
            return point;
        }

        internal bool NearEntrance(float x, float y)
        {
            float radius = Site.Source.id == House5 ? 3.02f : 1.65f;
            float clearance = Site.Source.id == House5 ? 4.45f : 3.05f;
            var p = At(x, y);
            return Site.Entrances.Any(entry =>
                Vector3.Dot(entry.forward, Out) > .75f &&
                Mathf.Abs(Vector3.Dot(entry.position - p, Across)) < radius &&
                Vector3.Dot(entry.position - p, Out) is > -.65f and < 5.3f &&
                y < entry.position.y + clearance);
        }
    }

    private enum Surface { Other, Wall, Gutter }
    private sealed class WallProbe
    {
        private sealed class Part
        {
            internal MeshCollider Collider;
            internal int[] SubmeshEnds;
            internal Surface[] Surfaces;
        }

        private readonly Part[] parts;

        internal WallProbe(Transform probeRoot, IEnumerable<MeshRenderer> renderers, bool house5 = false)
        {
            parts = renderers.Select(renderer =>
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                var obj = new GameObject("Facade probe") { layer = 2, hideFlags = HideFlags.HideAndDontSave };
                var source = renderer.transform;
                var tr = obj.transform;
                tr.SetParent(probeRoot, false);
                tr.SetPositionAndRotation(source.position, source.rotation);
                tr.localScale = source.lossyScale;
                var collider = obj.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                var materials = renderer.sharedMaterials;
                var ends = new int[mesh.subMeshCount];
                var tags = new Surface[mesh.subMeshCount];
                int count = 0;
                for (int i = 0; i < mesh.subMeshCount; i++)
                {
                    count += (int)mesh.GetIndexCount(i) / 3;
                    ends[i] = count;
                    tags[i] = GetSurface(materials[Mathf.Min(i, materials.Length - 1)].name, house5 ? source.name : "");
                }
                return new Part { Collider = collider, SubmeshEnds = ends, Surfaces = tags };
            }).ToArray();
            if (parts.Length == 0) throw new InvalidOperationException("Expected a visible building surface.");
        }

        internal bool Sample(Ray ray, float range, out RaycastHit nearest, out Surface surface)
        {
            nearest = default;
            surface = Surface.Other;
            bool found = false;
            foreach (var part in parts)
            {
                if (!part.Collider.Raycast(ray, out var hit, range)) continue;
                range = hit.distance;
                nearest = hit;
                var submesh = Array.FindIndex(part.SubmeshEnds, end => end > hit.triangleIndex);
                surface = submesh >= 0 && hit.triangleIndex >= 0 ? part.Surfaces[submesh] : Surface.Other;
                found = true;
            }
            return found;
        }

        private static Surface GetSurface(string material, string part)
        {
            string name = (material + " " + part).ToLowerInvariant();
            if (name.Contains("roofedge") || name.Contains("водосток")) return Surface.Gutter;
            if (name.Contains("brick") || name.Contains("plinth") || name.Contains("foundation") ||
                name.Contains("masonry") || name.Contains("кирпич") || name.Contains("штукатурка") ||
                name.Contains("цоколь") || name.Contains("стена"))
                return Surface.Wall;
            return Surface.Other;
        }
    }

    private static Site[] FindSites(Transform probeRoot)
    {
        var data = JsonUtility.FromJson<StreetBuilder.Data>(File.ReadAllText("Assets/StreetSource/street.json"));
        var reference = Object.FindObjectsByType<StreetBuilding>().ToDictionary(building => building.osmId);
        var background = Object.FindObjectsByType<MeshRenderer>().Single(renderer =>
            renderer.name.StartsWith("Фоновые здания · проёмы", StringComparison.Ordinal));
        var backgroundGeometry = new WallProbe(probeRoot, new[] { background });
        var entrances = Object.FindObjectsByType<PilotBlock>().Single().entrances.Select(view => view.parent).ToArray();
        var house = entrances[0].parent;
        if (entrances.Any(entry => entry.parent != house)) throw new InvalidOperationException("House 5 entrance hierarchy changed.");

        var sites = new List<Site>();
        foreach (var input in data.buildings.OrderBy(building => building.id))
        {
            if (input.kind is "garage" or "garages" or "shed" or "roof" or "service" or "warehouse") continue;
            var footprint = input.points.Select(point => point.V).ToArray();
            if ((footprint[0] - footprint[^1]).sqrMagnitude < .000001f) footprint = footprint[..^1];
            float area = Mathf.Abs(SignedArea(footprint));
            if (area < 5f) continue; // The saved background generator omits these outlines.
            bool detailed = input.id == House5 || reference.ContainsKey(input.id);
            var site = new Site
            {
                Source = input, Center = footprint.Aggregate(Vector3.zero, (sum, p) => sum + p) / footprint.Length,
                Area = area, Detailed = detailed
            };
            sites.Add(site);
            if (!detailed)
            {
                var points = footprint.Select(background.transform.TransformPoint).ToArray();
                AddPolygon(site, points, 1 + input.levels * 3.1f, backgroundGeometry);
                continue;
            }
            if (input.id == House5)
            {
                var meshRenderers = house.GetComponentsInChildren<MeshRenderer>().Where(MeshAvailable);
                var walls = new WallProbe(probeRoot, meshRenderers, true);
                site.Entrances = entrances;
                foreach (var box in house.GetComponents<BoxCollider>().Where(box => box.size.y > 7))
                    AddBox(site, box, walls);
                continue;
            }
            var building = reference[input.id];
            var mainLod = building.GetComponent<LODGroup>().GetLODs()[0].renderers.OfType<MeshRenderer>();
            var geometry = new WallProbe(probeRoot, mainLod);
            site.Entrances = building.GetComponentsInChildren<Transform>().Where(tr => tr.name.StartsWith("Access · ")).ToArray();
            foreach (var box in building.bodyColliders.OfType<BoxCollider>()) AddBox(site, box, geometry);
            if (input.id is 110515918 or 114788560)
            {
                // The two saved villa models have a wraparound veranda outside their body colliders.
                foreach (var wall in site.Walls.ToArray())
                    site.Walls.Add(new Wall
                    {
                        Index = site.Walls.Count, Start = wall.Start + wall.Out * 1.525f,
                        Across = wall.Across, Out = wall.Out, Width = wall.Width, Height = 2.90f,
                        Geometry = geometry, Site = site, Veranda = true
                    });
            }
        }
        if (sites.All(site => site.Source.id != House5) || reference.Keys.Any(id => sites.All(site => site.Source.id != id)))
            throw new InvalidOperationException("A numbered façade is missing from the input.");
        return sites.ToArray();
    }

    private static bool MeshAvailable(MeshRenderer renderer) =>
        renderer.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh && renderer.sharedMaterials.Length > 0;

    private static void AddBox(Site site, BoxCollider box, WallProbe geometry)
    {
        var half = box.size * .5f;
        var points = new[]
        {
            new Vector3(-half.x, -half.y, -half.z), new Vector3(half.x, -half.y, -half.z),
            new Vector3(half.x, -half.y, half.z), new Vector3(-half.x, -half.y, half.z)
        }.Select(offset => box.transform.TransformPoint(box.center + offset)).ToArray();
        AddPolygon(site, points, box.size.y * Mathf.Abs(box.transform.lossyScale.y), geometry);
    }

    private static void AddPolygon(Site site, Vector3[] points, float height, WallProbe geometry)
    {
        float sign = Mathf.Sign(SignedArea(points));
        for (int i = 0; i < points.Length; i++)
        {
            var delta = points[(i + 1) % points.Length] - points[i];
            delta.y = 0;
            if (delta.sqrMagnitude < 1.5f * 1.5f) continue;
            Vector3 across = delta.normalized;
            site.Walls.Add(new Wall
            {
                Index = site.Walls.Count, Start = points[i], Across = across,
                Out = new Vector3(across.z, 0, -across.x) * sign,
                Height = height, Width = delta.magnitude, Geometry = geometry, Site = site
            });
        }
    }

    private static float SignedArea(Vector3[] points)
    {
        float area = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.x * b.z - b.x * a.z;
        }
        return area * .5f;
    }

    private static bool HitFacade(Wall wall, float x, float y, float front, out RaycastHit hit, out Surface surface) =>
        wall.Geometry.Sample(new Ray(wall.At(x, y, front), -wall.Out), front + .37f, out hit, out surface);

    private static bool WallDepth(Wall wall, float x, float y, bool acceptGutters, out float depth)
    {
        const float front = 2.20f;
        depth = 0;
        if (wall.NearEntrance(x, y) || !HitFacade(wall, x, y, front, out var hit, out var surface)) return false;
        depth = front - hit.distance;
        if (acceptGutters && surface == Surface.Gutter && depth is >= .074f and <= .225f)
        {
            const float behindGutter = .065f;
            if (!HitFacade(wall, x, y, behindGutter, out hit, out surface)) return false;
            depth = behindGutter - hit.distance;
        }
        bool verandaPost = wall.Veranda && y > wall.Y(1.10f) && y < wall.Y(2.89f)
            && depth is > -.055f and < .035f;
        return (surface == Surface.Wall || verandaPost) &&
            depth is > -.31f and < 1.72f && Vector3.Dot(hit.normal, wall.Out) > .92f;
    }

    private static float Ground(Vector3 at, float baseHeight)
    {
        at.y = baseHeight + 2.55f;
        return Physics.Raycast(at, Vector3.down, out var hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && hit.normal.y > .62f ? hit.point.y : baseHeight;
    }

    private static bool NearSameWall(Wall a, Wall b) =>
        Vector3.Dot(a.Out, b.Out) > .95f &&
        Mathf.Abs(Vector3.Dot(a.Start - b.Start, a.Out)) < .55f;
}
