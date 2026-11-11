using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The surrounding OSM buildings retain their original extents/heights. Window/door positions remain
// the previous approximate facade pattern; these openings are geometry, not surveyed architecture.
internal static class BackgroundOpeningBuilder
{
    private sealed class Opening
    {
        internal Rect rect;
        internal bool door;
    }
    private sealed class InspectionCandidate
    {
        internal long buildingId;
        internal Vector3 door, window, outward;
        internal float closeDistance = 4.5f;
    }
    private sealed class Facade
    {
        internal Vector3 origin, across, outward;
        internal Material wall, foundation, frame, glass, metal, doorPaint, hardware;
        internal Vector3 P(Vector3 p) => origin + across * p.x + Vector3.up * p.y + outward * p.z;
        internal Vector3 N(Vector3 n) => across * n.x + Vector3.up * n.y + outward * n.z;
        internal void Face(Material material, Vector3 normal, bool collide, params Vector3[] points)
        {
            var world = points.Select(P).ToArray();
            geometry.Face(material, N(normal), world);
            if (collide) collision.Face(material, N(normal), world);
        }
        internal void Rect(Material material, Rect r, float z, bool collide = false)
        {
            if (r.width < .0001f || r.height < .0001f) return;
            Face(material, Vector3.forward, collide, new Vector3(r.xMin, r.yMin, z), new Vector3(r.xMax, r.yMin, z),
                new Vector3(r.xMax, r.yMax, z), new Vector3(r.xMin, r.yMax, z));
        }
        internal void Solid(float x0, float x1, float y0, float y1)
        {
            if (x1 - x0 < .0001f || y1 - y0 < .0001f) return;
            if (y0 < .7f) Rect(foundation, UnityEngine.Rect.MinMaxRect(x0, y0, x1, Mathf.Min(y1, .7f)), .015f, true);
            if (y1 > .7f) Rect(wall, UnityEngine.Rect.MinMaxRect(x0, Mathf.Max(y0, .7f), x1, y1), 0, true);
            if (y0 < .7f && y1 > .7f)
                Face(foundation, Vector3.up, true, new Vector3(x0, .7f, 0), new Vector3(x1, .7f, 0),
                    new Vector3(x1, .7f, .015f), new Vector3(x0, .7f, .015f));
        }
        internal void Box(Material material, Vector3 center, Vector3 size, bool collide = false)
        {
            var q = Quaternion.LookRotation(outward);
            geometry.Box(material, P(center), size, q);
            if (collide) collision.Box(material, P(center), size, q);
        }
        internal void Reveal(Rect r, float back)
        {
            // Separate lower plinth reveals retain the 15 mm offset of the original foundation.
            void Jamb(float x, Vector3 normal)
            {
                float y0 = r.yMin;
                if (y0 < .7f)
                {
                    float top = Mathf.Min(.7f, r.yMax);
                    Face(foundation, normal, true, new Vector3(x, y0, .015f), new Vector3(x, top, .015f),
                        new Vector3(x, top, back), new Vector3(x, y0, back));
                    y0 = top;
                }
                if (y0 < r.yMax)
                    Face(wall, normal, true, new Vector3(x, y0, 0), new Vector3(x, r.yMax, 0),
                        new Vector3(x, r.yMax, back), new Vector3(x, y0, back));
            }
            Jamb(r.xMin, Vector3.right); Jamb(r.xMax, Vector3.left);
            foreach (bool top in new[] { false, true })
            {
                float y = top ? r.yMax : r.yMin, front = y < .7f ? .015f : 0;
                Face(y < .7f ? foundation : wall, top ? Vector3.down : Vector3.up, true,
                    new Vector3(r.xMin, y, front), new Vector3(r.xMax, y, front), new Vector3(r.xMax, y, back), new Vector3(r.xMin, y, back));
            }
        }
        internal void Ring(Material material, Rect outer, float thickness, float front, float back)
        {
            var inner = UnityEngine.Rect.MinMaxRect(outer.xMin + thickness, outer.yMin + thickness, outer.xMax - thickness, outer.yMax - thickness);
            Rect(material, UnityEngine.Rect.MinMaxRect(outer.xMin, outer.yMin, outer.xMax, inner.yMin), front);
            Rect(material, UnityEngine.Rect.MinMaxRect(outer.xMin, inner.yMax, outer.xMax, outer.yMax), front);
            Rect(material, UnityEngine.Rect.MinMaxRect(outer.xMin, inner.yMin, inner.xMin, inner.yMax), front);
            Rect(material, UnityEngine.Rect.MinMaxRect(inner.xMax, inner.yMin, outer.xMax, inner.yMax), front);
            Face(material, Vector3.right, false, new Vector3(inner.xMin, inner.yMin, front), new Vector3(inner.xMin, inner.yMax, front), new Vector3(inner.xMin, inner.yMax, back), new Vector3(inner.xMin, inner.yMin, back));
            Face(material, Vector3.left, false, new Vector3(inner.xMax, inner.yMin, front), new Vector3(inner.xMax, inner.yMax, front), new Vector3(inner.xMax, inner.yMax, back), new Vector3(inner.xMax, inner.yMin, back));
            Face(material, Vector3.up, false, new Vector3(inner.xMin, inner.yMin, front), new Vector3(inner.xMax, inner.yMin, front), new Vector3(inner.xMax, inner.yMin, back), new Vector3(inner.xMin, inner.yMin, back));
            Face(material, Vector3.down, false, new Vector3(inner.xMin, inner.yMax, front), new Vector3(inner.xMax, inner.yMax, front), new Vector3(inner.xMax, inner.yMax, back), new Vector3(inner.xMin, inner.yMax, back));
        }
        internal void Bar(Material material, Rect r, float front, float back)
        {
            Rect(material, r, front);
            Face(material, Vector3.left, false, new Vector3(r.xMin, r.yMin, front), new Vector3(r.xMin, r.yMax, front), new Vector3(r.xMin, r.yMax, back), new Vector3(r.xMin, r.yMin, back));
            Face(material, Vector3.right, false, new Vector3(r.xMax, r.yMin, front), new Vector3(r.xMax, r.yMax, front), new Vector3(r.xMax, r.yMax, back), new Vector3(r.xMax, r.yMin, back));
            Face(material, Vector3.down, false, new Vector3(r.xMin, r.yMin, front), new Vector3(r.xMax, r.yMin, front), new Vector3(r.xMax, r.yMin, back), new Vector3(r.xMin, r.yMin, back));
            Face(material, Vector3.up, false, new Vector3(r.xMin, r.yMax, front), new Vector3(r.xMax, r.yMax, front), new Vector3(r.xMax, r.yMax, back), new Vector3(r.xMin, r.yMax, back));
        }
        internal void Window(Rect hole)
        {
            Reveal(hole, -.10f);
            Ring(frame, hole, .055f, -.10f, -.175f);
            Rect inner = UnityEngine.Rect.MinMaxRect(hole.xMin + .055f, hole.yMin + .055f, hole.xMax - .055f, hole.yMax - .055f);
            float x = hole.center.x, transom = hole.center.y + .36f;
            Bar(frame, new Rect(x - .02f, inner.yMin, .04f, inner.height), -.088f, -.178f);
            Bar(frame, UnityEngine.Rect.MinMaxRect(inner.xMin, transom - .0175f, x - .02f, transom + .0175f), -.088f, -.178f);
            Bar(frame, UnityEngine.Rect.MinMaxRect(x + .02f, transom - .0175f, inner.xMax, transom + .0175f), -.088f, -.178f);
            float seed = EnvironmentMesh.WindowCoordinates(new[] { P(new Vector3(x, hole.center.y, -.18f)) }, outward)[0].z;
            foreach (var pane in new[] {
                UnityEngine.Rect.MinMaxRect(inner.xMin, inner.yMin, x-.02f, transom-.0175f),
                UnityEngine.Rect.MinMaxRect(x+.02f, inner.yMin, inner.xMax, transom-.0175f),
                UnityEngine.Rect.MinMaxRect(inner.xMin, transom+.0175f, x-.02f, inner.yMax),
                UnityEngine.Rect.MinMaxRect(x+.02f, transom+.0175f, inner.xMax, inner.yMax) })
            {
                var points = new[] { new Vector3(pane.xMin, pane.yMin, -.18f), new Vector3(pane.xMax, pane.yMin, -.18f),
                    new Vector3(pane.xMax, pane.yMax, -.18f), new Vector3(pane.xMin, pane.yMax, -.18f) };
                var world = points.Select(P).ToArray();
                var uv = points.Select(p => new Vector4((p.x - inner.xMin) / inner.width, (p.y - inner.yMin) / inner.height, seed, 1)).ToArray();
                geometry.WindowFace(glass, outward, world, uv);
            }
            // One invisible closed pane collider also spans the thin muntins; there is no walk-through gap.
            collision.Face(glass, outward, P(new Vector3(hole.xMin, hole.yMin, -.18f)), P(new Vector3(hole.xMax, hole.yMin, -.18f)),
                P(new Vector3(hole.xMax, hole.yMax, -.18f)), P(new Vector3(hole.xMin, hole.yMax, -.18f)));
            Box(frame, new Vector3(x, hole.yMin - .035f, .015f), new Vector3(1.34f, .07f, .35f));
            WindowCount++;
        }
        internal void Door(Rect hole, Material roof)
        {
            Reveal(hole, -.10f);
            Ring(doorPaint, hole, .065f, -.10f, -.18f);
            Rect leaf = UnityEngine.Rect.MinMaxRect(hole.xMin + .065f, hole.yMin + .065f, hole.xMax - .065f, hole.yMax - .065f);
            Rect(doorPaint, hole, -.185f, true);
            // The slab is recessed behind its jambs; shallow raised panels and hardware give it scale.
            Bar(doorPaint, UnityEngine.Rect.MinMaxRect(leaf.xMin + .09f, leaf.yMin + .19f, leaf.xMax - .09f, leaf.yMax - .20f), -.176f, -.185f);
            Box(hardware, new Vector3(leaf.xMax - .10f, hole.center.y, -.133f), new Vector3(.035f, .23f, .065f));
            foreach (float y in new[] { leaf.yMin + .24f, leaf.yMax - .24f })
                Box(hardware, new Vector3(leaf.xMin + .012f, y, -.13f), new Vector3(.032f, .12f, .065f));
            // Keep the former step and the canopy's XZ centre/dimensions. Its small vertical lift
            // leaves clearance above the 2.1 m door now starting at the actual 0.24 m step top.
            Box(foundation, new Vector3(hole.center.x, .12f, .7f), new Vector3(2.3f, .24f, 1.5f), true);
            Box(roof, new Vector3(hole.center.x, 2.50f, .6f), new Vector3(2.1f, .13f, 1.35f));
            DoorCount++;
        }
    }

    private static EnvironmentMesh geometry, collision;
    private static Transform parent;
    private static Material paintedDoor, doorHardware;
    private static readonly List<InspectionCandidate> inspections = new List<InspectionCandidate>();
    internal static int BuildingCount { get; private set; }
    internal static int WindowCount { get; private set; }
    internal static int DoorCount { get; private set; }
    internal static int SuppressedGroundWindows { get; private set; }

    internal static void Initialize(Transform targetParent)
    {
        EnvironmentMesh.EnsureFolders(); parent = targetParent;
        // The shared general Metal is partly conductive and extremely dark in linear space.
        // A painted door is a dielectric coating; keep a local material instead of altering scene light.
        paintedDoor = EnvironmentMesh.Material("BackgroundDoorPaint", new Color(.30f, .35f, .36f), 0, .2f, 0);
        StreetSurfaceLibrary.ApplyNamed(paintedDoor, "EntranceSteel");
        paintedDoor.SetFloat("_Metallic", 0);
        paintedDoor.SetFloat("_RoughnessScale", .50f); paintedDoor.SetFloat("_RoughnessBias", .35f);
        paintedDoor.SetFloat("_BumpStrength", .14f); paintedDoor.SetFloat("_Parallax", .001f);
        paintedDoor.SetFloat("_OcclusionStrength", .20f); paintedDoor.SetFloat("_Weathering", .10f);
        paintedDoor.SetColor("_EmissionColor", Color.black);
        doorHardware = EnvironmentMesh.Material("BackgroundDoorHardware", new Color(.60f, .64f, .65f), 0, .3f, .25f);
        StreetSurfaceLibrary.ApplyNamed(doorHardware, "GraphiteMetal");
        doorHardware.SetFloat("_Metallic", .25f);
        doorHardware.SetFloat("_RoughnessScale", .45f); doorHardware.SetFloat("_RoughnessBias", .28f);
        doorHardware.SetFloat("_BumpStrength", .14f); doorHardware.SetFloat("_Parallax", .0006f);
        doorHardware.SetFloat("_OcclusionStrength", .20f); doorHardware.SetFloat("_Weathering", .06f);
        doorHardware.SetColor("_EmissionColor", Color.black);
        EditorUtility.SetDirty(paintedDoor); EditorUtility.SetDirty(doorHardware);
        geometry = new EnvironmentMesh(); collision = new EnvironmentMesh();
        inspections.Clear();
        BuildingCount = WindowCount = DoorCount = SuppressedGroundWindows = 0;
    }

    internal static void Append(StreetBuilder.Building building, Vector3[] footprint, float height, int floors,
        Material wall, Material foundation, Material frame, Material glass, Material metal, Material roof, bool garage)
    {
        if (geometry == null || !parent) throw new InvalidOperationException("Initialize BackgroundOpeningBuilder before Append.");
        if (footprint == null || footprint.Length < 3 || height <= 0) return;
        var p = footprint;
        if ((p[0] - p[p.Length - 1]).sqrMagnitude < .000001f) p = p.Take(p.Length - 1).ToArray();
        float area = 0;
        for (int i = 0; i < p.Length; i++) area += p[i].x * p[(i + 1) % p.Length].z - p[(i + 1) % p.Length].x * p[i].z;
        if (Mathf.Abs(area) < 10) return;
        for (int i = 0; i < p.Length; i++)
        {
            Vector3 a = p[i], b = p[(i + 1) % p.Length];
            float length = Vector3.Distance(a, b);
            if (length < .001f) continue;
            Vector3 across = (b - a) / length;
            Vector3 outward = new Vector3(across.z, 0, -across.x) * (area > 0 ? 1 : -1);
            var facade = new Facade { origin = a, across = across, outward = outward,
                wall = wall, foundation = foundation, frame = frame, glass = glass, metal = metal,
                doorPaint = paintedDoor, hardware = doorHardware };
            var openings = new List<Opening>();
            bool hasDoor = !garage && length > 12 && height >= 2.54f;
            Rect doorway = new Rect(length * .5f - .6f, .24f, 1.2f, 2.1f);
            if (!garage)
            {
                int count = Mathf.Max(0, Mathf.FloorToInt((length - 1.4f) / 2.75f));
                for (int floor = 0; floor < floors; floor++)
                    for (int column = 0; column < count; column++)
                    {
                        float x = length * (column + 1f) / (count + 1), y = 2 + floor * 3.1f;
                        var window = new Rect(x - .6f, y - .75f, 1.2f, 1.5f);
                        if (window.xMin < .15f || window.xMax > length - .15f || window.yMax > height - .15f) continue;
                        // Preserve door centres. Ground-floor windows yield to the door AND its existing hood.
                        if (floor == 0 && hasDoor && window.xMax > doorway.xMin - .5f && window.xMin < doorway.xMax + .5f)
                        { SuppressedGroundWindows++; continue; }
                        openings.Add(new Opening { rect = window });
                    }
            }
            if (hasDoor) openings.Add(new Opening { rect = doorway, door = true });
            if (hasDoor)
            {
                var nearestWindow = openings.Where(o => !o.door).OrderBy(o =>
                    Mathf.Abs(o.rect.center.x - doorway.center.x) + Mathf.Abs(o.rect.center.y - doorway.center.y)).FirstOrDefault();
                Vector3 doorPoint = facade.P(new Vector3(doorway.center.x, doorway.center.y, -.10f));
                inspections.Add(new InspectionCandidate { buildingId = building.id, door = doorPoint, outward = outward,
                    window = nearestWindow == null ? doorPoint + Vector3.up * .5f
                        : facade.P(new Vector3(nearestWindow.rect.center.x, nearestWindow.rect.center.y, -.10f)) });
            }
            var cuts = new SortedSet<float> { 0, length };
            foreach (var opening in openings) { cuts.Add(opening.rect.xMin); cuts.Add(opening.rect.xMax); }
            var xs = cuts.ToArray();
            for (int strip = 1; strip < xs.Length; strip++)
            {
                float x0 = xs[strip - 1], x1 = xs[strip], x = (x0 + x1) * .5f, y = 0;
                foreach (var opening in openings.Where(o => x > o.rect.xMin && x < o.rect.xMax).OrderBy(o => o.rect.yMin))
                {
                    facade.Solid(x0, x1, y, opening.rect.yMin);
                    y = Mathf.Max(y, opening.rect.yMax);
                }
                facade.Solid(x0, x1, y, height);
            }
            foreach (var opening in openings)
                if (opening.door) facade.Door(opening.rect, roof); else facade.Window(opening.rect);
            facade.Box(roof, new Vector3(length * .5f, height + .1f, 0), new Vector3(length + .12f, .22f, .35f));
        }
        foreach (var tri in StreetBuilder.Triangulate(p))
        {
            Vector3 a = p[tri[0]] + Vector3.up * height, b = p[tri[1]] + Vector3.up * height, c = p[tri[2]] + Vector3.up * height;
            geometry.Face(roof, Vector3.up, a, b, c); collision.Face(roof, Vector3.up, a, b, c);
        }
        BuildingCount++;
    }

    internal static void Flush()
    {
        if (geometry == null || BuildingCount == 0) return;
        // Two stable mesh assets, one renderer: simplified collision omits decorative frame/handle faces.
        var rendered = geometry.Create("BackgroundBuildings_Openings", "Фоновые здания · проёмы, откосы и закрытые створки");
        rendered.transform.SetParent(parent, false);
        var collisionObject = collision.Create("BackgroundBuildings_OpeningCollision", "Временная подготовка коллизии");
        var collisionMesh = collisionObject.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(collisionObject);
        rendered.AddComponent<MeshCollider>().sharedMesh = collisionMesh;
        GameObjectUtility.SetStaticEditorFlags(rendered, StaticEditorFlags.BatchingStatic);
        CreateInspectionViews(rendered.transform);
        Debug.Log("BACKGROUND_OPENINGS buildings=" + BuildingCount + " windows=" + WindowCount + " doors=" + DoorCount
            + " suppressedOverlaps=" + SuppressedGroundWindows + " renderTriangles=" + rendered.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3
            + " collisionTriangles=" + collisionMesh.triangles.Length / 3);
        geometry = null; collision = null;
    }

    private static void CreateInspectionViews(Transform owner)
    {
        Physics.SyncTransforms();
        float Distance(InspectionCandidate c) => new Vector2(c.door.x, c.door.z).sqrMagnitude;
        bool ClearRay(Vector3 camera, Vector3 target)
        {
            Vector3 delta = target - camera;
            return !Physics.Raycast(camera, delta.normalized, Mathf.Max(.01f, delta.magnitude - .35f), ~0, QueryTriggerInteraction.Ignore);
        }
        bool Clear(InspectionCandidate c)
        {
            foreach (float distance in new[] { 4.5f, 5.5f, 4f, 6f })
            {
                Vector3 target = c.door + Vector3.up * .25f;
                Vector3 camera = c.door + c.outward * distance; camera.y = 1.9f;
                if (!ClearRay(camera, target)) continue;
                c.closeDistance = distance; return true;
            }
            return false;
        }
        var choices = inspections.GroupBy(c => c.buildingId)
            .Select(g => g.OrderBy(Distance).FirstOrDefault(Clear)).Where(c => c != null).OrderBy(Distance).ToList();
        // The attached annex is also a useful regression view of the previously overlaid door/window.
        var selected = choices.Take(4).ToList();
        var annex = choices.FirstOrDefault(c => c.buildingId == 520207927);
        if (annex != null && !selected.Contains(annex))
        {
            if (selected.Count == 4) selected.RemoveAt(3);
            selected.Add(annex);
        }
        void View(string name, Vector3 position, Vector3 target)
        {
            var view = new GameObject(name).transform;
            view.SetParent(owner, false); view.position = position;
            view.rotation = Quaternion.LookRotation(target - position);
        }
        foreach (var sample in selected.OrderBy(Distance))
        {
            Vector3 close = sample.door + sample.outward * sample.closeDistance; close.y = 1.9f;
            View("Opening QA · background " + sample.buildingId + " · close door", close, sample.door + Vector3.up * .25f);
            Vector3 target = (sample.door + sample.window) * .5f + Vector3.up * .12f;
            Vector3 context = close; context.y = 2.2f;
            foreach (float distance in new[] { 6f, 5f, 4f })
            {
                Vector3 candidate = target + sample.outward * distance; candidate.y = 2.2f;
                if (ClearRay(candidate, target)) { context = candidate; break; }
            }
            View("Opening QA · background " + sample.buildingId + " · context window and door", context, target);
        }
        Debug.Log("BACKGROUND_OPENING_VIEWS buildings=" + selected.Count + " views=" + selected.Count * 2
            + " ids=" + string.Join(",", selected.Select(c => c.buildingId.ToString())));
    }
}
