using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Address-specific reconstructions, not a random facade applied to every OSM polygon.
// Sources and the distinction between observations and estimates are saved in the profiles.
internal static class ReferenceBuildingBuilder
{
    private const string Root = "Assets/StreetGenerated/Buildings";
    [Serializable] private class Source { public string id, url, observed; }
    [Serializable] private class Profiles { public Profile[] buildings; public Source[] sources; }
    [Serializable] private class Profile
    {
        public long osmId;
        public string address, referenceId, style, @base, note;
        public int[] storeys;
        public bool redCenter;
    }
    private sealed class Section
    {
        public Vector3 center;
        public Quaternion rotation;
        public float width, depth;
        public float height;
        public int floors;
        public float Height => height > 0 ? height : 1.55f + floors * 3.1f;
    }
    // A local facade coordinate system: X across, Y up, Z out of the wall.
    private sealed class Frame
    {
        public readonly EnvironmentMesh mesh;
        private readonly Vector3 origin;
        private readonly Quaternion rotation;
        public Frame(EnvironmentMesh mesh, Vector3 origin, Quaternion rotation)
        { this.mesh = mesh; this.origin = origin; this.rotation = rotation; }
        public Vector3 P(Vector3 p) => origin + rotation * p;
        public Vector3 Forward => rotation * Vector3.forward;
        public Frame At(Vector3 p, float yaw = 0) => new Frame(mesh, P(p), rotation * Quaternion.Euler(0, yaw, 0));
        public void Box(Material m, Vector3 p, Vector3 size) => mesh.Box(m, P(p), size, rotation);
        public void Rod(Material m, Vector3 a, Vector3 b, float radius, int sides = 6) => mesh.Cylinder(m, P(a), P(b), radius, radius, sides);
        public void Face(Material m, Vector3 normal, params Vector3[] p) => mesh.Face(m, rotation * normal, p.Select(P).ToArray());
        public void WindowFace(Material m, Rect bounds, Vector3 center, params Vector3[] p)
        {
            float seed = EnvironmentMesh.WindowCoordinates(new[] { P(center) }, rotation * Vector3.forward)[0].z;
            var coordinates = p.Select(v => new Vector4((v.x - bounds.xMin) / bounds.width, (v.y - bounds.yMin) / bounds.height, seed, 1)).ToArray();
            mesh.WindowFace(m, rotation * Vector3.forward, p.Select(P).ToArray(), coordinates);
        }
        public void Roof(params Vector3[] p) => Roof(roof, p);
        public void Roof(Material material, params Vector3[] p)
        {
            Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]).normalized;
            Face(material, n.y < 0 ? -n : n, p);
        }
    }
    private static Dictionary<long, Profile> profiles;
    private static Transform parent;
    private static Vector3[] mainRoad;
    private static Vector3[][] referenceFootprints;
    private static Material red, cream, roof, greyRoof, roofEdge, trim, dark, metal, door, concrete, brass, turquoise, darkBase;
    private static Material[] glass;
    private static Material fanGlass;

    internal static void Initialize(Transform map, StreetBuilder.Data data)
    {
        foreach (string folder in new[] { "Materials", "Meshes", "Prefabs" }) Directory.CreateDirectory(Root + "/" + folder);
        AssetDatabase.Refresh();
        var document = JsonUtility.FromJson<Profiles>(File.ReadAllText("Assets/StreetSource/building_profiles.json"));
        profiles = document.buildings.ToDictionary(p => p.osmId);
        foreach (var p in profiles.Values)
        {
            if (!data.buildings.Any(b => b.id == p.osmId) || !document.sources.Any(s => s.id == p.referenceId)
                || p.storeys == null || p.storeys.Length == 0 || p.storeys.Any(n => n < 1 || n > 8))
                throw new InvalidOperationException("Incomplete architectural profile: " + p.osmId);
        }
        parent = new GameObject("07 · Дома по адресным архитектурным референсам").transform;
        parent.SetParent(map, false);
        mainRoad = data.roads.First(r => r.main).points.Select(p => p.V).ToArray();
        referenceFootprints = data.buildings.Select(b => b.points.Select(p => p.V).ToArray()).ToArray();
        red = Mat("RedBrick", Color.white);
        cream = Mat("CreamBrick", Color.white);
        roof = Mat("GreenStandingSeam", new Color(.20f, .34f, .30f), 0, .32f, .45f);
        greyRoof = Mat("GreyStandingSeam", new Color(.38f, .39f, .36f), 0, .25f, .4f);
        roofEdge = Mat("RoofEdge", new Color(.28f, .36f, .33f), 0, .3f, .5f);
        trim = Mat("IvoryFrames", new Color(.76f, .78f, .75f), 0, .22f);
        dark = Mat("WindowRecess", new Color(.075f, .09f, .085f));
        metal = Mat("BalconyIron", new Color(.12f, .16f, .14f), 0, .25f, .65f);
        door = Mat("EntranceSteel", new Color(.22f, .32f, .27f), 0, .18f, 0);
        // The visible layer is paint, so it retains diffuse skylight beneath a deep entrance hood.
        // Conductive metal plus strong material AO made the closed leaf read as a black hole.
        door.SetFloat("_Metallic", 0); door.SetFloat("_OcclusionStrength", .25f);
        door.SetFloat("_RoughnessScale", .5f); door.SetFloat("_RoughnessBias", .35f);
        door.SetFloat("_BumpStrength", .14f); door.SetFloat("_Parallax", .001f);
        concrete = Mat("EntranceConcrete", new Color(.58f, .58f, .52f), 6);
        brass = Mat("DoorHardware", new Color(.56f, .48f, .27f), 0, .35f, .35f);
        brass.SetFloat("_OcclusionStrength", .25f);
        turquoise = Mat("TurquoisePlinth", new Color(.27f, .43f, .38f), 6);
        darkBase = Mat("DarkPlinth", new Color(.28f, .31f, .27f), 6);
        glass = Enumerable.Range(0, 4).Select(i => Mat("Glass" + i,
            new Color(.18f + i * .018f, .26f + i * .017f, .28f + i * .015f), 9, .75f, .18f)).ToArray();
        fanGlass = Mat("ArchitecturalFanGlass", Color.white);
        StreetSurfaceLibrary.ApplyGlass(fanGlass, 1, false);
    }

    private static Material Mat(string name, Color color, float kind = 0, float smoothness = .1f, float metallic = 0)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Street/Surface")); AssetDatabase.CreateAsset(m, path); }
        m.shader = Shader.Find("Street/Surface");
        m.SetColor("_Color", color); m.SetFloat("_Kind", kind); m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_VertexTint", 0);
        m.SetFloat("_Metallic", metallic); m.SetFloat("_Cull", 2); m.enableInstancing = true;
        StreetSurfaceLibrary.ApplyNamed(m, name);
        EditorUtility.SetDirty(m); return m;
    }

    internal static bool TryCreate(StreetBuilder.Building building)
    {
        if (!profiles.TryGetValue(building.id, out var profile) || profile.style == "existing") return false;
        Vector3[] outline = building.points.Select(p => p.V).ToArray();
        Vector3 origin = new Vector3((outline.Min(p => p.x) + outline.Max(p => p.x)) * .5f, 0,
            (outline.Min(p => p.z) + outline.Max(p => p.z)) * .5f);
        Section[] sections = profile.style == "brick-arches" ? ApartmentSections(outline, origin, profile) : SmallSection(outline, origin, profile);
        var root = new GameObject("Дунина-Горкавича, " + profile.address + " · OSM " + building.id);
        var renderers = new Renderer[3];
        Material plinth = profile.@base == "dark" ? darkBase : turquoise;
        for (int lod = 0; lod < 3; lod++)
        {
            var mesh = new EnvironmentMesh();
            foreach (var section in sections)
            {
                var frame = new Frame(mesh, section.center, section.rotation);
                if (profile.style == "brick-arches") Apartment(frame, section, profile, plinth, lod);
                else SmallBuilding(frame, section, profile, lod);
            }
            var part = mesh.Create("Building_" + building.id + "_LOD" + lod, "LOD " + lod, Root, true);
            part.transform.SetParent(root.transform, false);
            renderers[lod] = part.GetComponent<Renderer>();
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.OccluderStatic);
        }
        var group = root.AddComponent<LODGroup>();
        group.SetLODs(new[] { new LOD(.16f, new[] { renderers[0] }), new LOD(.045f, new[] { renderers[1] }), new LOD(.006f, new[] { renderers[2] }) });
        group.RecalculateBounds();
        var colliders = new List<Collider>();
        foreach (var section in sections)
        {
            var body = new GameObject("Корпус · " + section.floors + " этажей");
            body.transform.SetParent(root.transform, false); body.transform.localPosition = section.center; body.transform.localRotation = section.rotation;
            colliders.Add(EnvironmentMesh.Collider(body, Vector3.up * section.Height * .5f, new Vector3(section.width, section.Height, section.depth)));
            if (profile.style.StartsWith("villa", StringComparison.Ordinal))
                foreach (var edge in new[] { (new Vector3(0, 0, section.depth * .5f + 4), 0f, section.width + 8),
                    (new Vector3(0, 0, -section.depth * .5f - 4), 180f, section.width + 8),
                    (new Vector3(section.width * .5f + 4, 0, 0), 90f, section.depth + 8),
                    (new Vector3(-section.width * .5f - 4, 0, 0), -90f, section.depth + 8) })
                {
                    var fence = new GameObject("Коллизия ограды"); fence.transform.SetParent(body.transform, false);
                    fence.transform.localPosition = edge.Item1; fence.transform.localRotation = Quaternion.Euler(0, edge.Item2, 0);
                    for (float x = -edge.Item3 * .5f; x < edge.Item3 * .5f; x += 2.8f)
                    {
                        float length = Mathf.Min(2.8f, edge.Item3 * .5f - x);
                        if (edge.Item2 == 0 && Mathf.Abs(x + length * .5f) < 2) continue;
                        EnvironmentMesh.Collider(fence, new Vector3(x + length * .5f, .85f, 0), new Vector3(length, 1.7f, .15f));
                    }
                }
            if (profile.style != "brick-arches") continue;
            foreach (int sign in new[] { -1, 1 })
            {
                EnvironmentMesh.Collider(body, new Vector3(0, section.Height * .5f, sign * (section.depth * .5f + .7f)), new Vector3(9.6f, section.Height, 1.4f));
                for (int step = 0; step < 4; step++)
                    EnvironmentMesh.Collider(body, new Vector3(0, (step + 1) * .08f, sign * (section.depth * .5f + 2.55f - step * .29f)),
                        new Vector3(2.4f, (step + 1) * .16f, .30f));
                var end = new GameObject("Торцевой навес · стойки");
                end.transform.SetParent(body.transform, false);
                end.transform.localPosition = new Vector3(sign * section.width * .5f, 0, 0);
                end.transform.localRotation = Quaternion.Euler(0, sign > 0 ? 90 : -90, 0);
                foreach (int side in new[] { -1, 1 })
                    EnvironmentMesh.Collider(end, new Vector3(1.8f + side * 1.33f, 1.065f, 1.54f), new Vector3(.065f, 1.99f, .065f));
            }
        }
        int accessIndex = 0;
        foreach (var section in sections)
        {
            void Access(float z, int sign, string kind)
            {
                var marker = new GameObject("Access · " + kind + " " + (++accessIndex));
                marker.transform.SetParent(root.transform, false);
                marker.transform.localPosition = section.center + section.rotation * new Vector3(0, 0, z);
                marker.transform.localRotation = section.rotation * Quaternion.Euler(0, sign > 0 ? 0 : 180, 0);
            }
            if (profile.style == "brick-arches")
            {
                foreach (int sign in new[] { -1, 1 }) Access(sign * (section.depth * .5f + 2.69f), sign, "подход к крыльцу");
                foreach (int sign in new[] { -1, 1 })
                {
                    Quaternion outward = section.rotation * Quaternion.Euler(0, sign > 0 ? 90 : -90, 0);
                    Vector3 point = section.center + section.rotation * new Vector3(sign * section.width * .5f, 0, 0)
                        + outward * new Vector3(1.8f, 0, 1.80f);
                    Vector3 world = origin + point;
                    // Do not connect a repeated end detail through another mapped section.
                    if (referenceFootprints.Any(p => ContainsXZ(p, world) || ContainsXZ(p, world + outward * Vector3.forward * .7f))) continue;
                    var marker = new GameObject("Access · торцевой вход " + (++accessIndex));
                    marker.transform.SetParent(root.transform, false);
                    marker.transform.localPosition = point; marker.transform.localRotation = outward;
                }
            }
            else if (profile.style.StartsWith("villa", StringComparison.Ordinal))
                Access(section.depth * .5f + 4.35f, 1, "вход у ворот");
            else Access(section.depth * .5f + (profile.style == "office-stepped" ? 1.55f : 1.05f), 1, "вход");
        }
        OpeningViews(root.transform, sections, profile, origin);
        var metadata = root.AddComponent<StreetBuilding>();
        metadata.osmId = building.id; metadata.address = profile.address; metadata.referenceId = profile.referenceId;
        metadata.accuracyNote = profile.note; metadata.sectionStoreys = profile.storeys;
        metadata.localBounds = renderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
        metadata.bodyColliders = colliders.ToArray();
        var prefab = EnvironmentMesh.SavePrefab(root, "Building_" + building.id, Root);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.localPosition = origin;
        return true;
    }

    private static void OpeningViews(Transform root, Section[] sections, Profile profile, Vector3 origin)
    {
        // A few explicit cameras per saved prefab allow runtime QA to inspect the actual openings.
        // They do not affect colliders, facade layout, LOD bounds or Access markers.
        Frame selected = null; Section chosen = sections[0]; float best = float.MaxValue;
        foreach (var section in sections)
            foreach (int sign in new[] { -1, 1 })
            {
                if (profile.style != "brick-arches" && sign < 0) continue;
                var facade = new Frame(null, section.center + section.rotation * new Vector3(0, 0, sign * section.depth * .5f),
                    section.rotation * Quaternion.Euler(0, sign > 0 ? 0 : 180, 0));
                Vector3 camera = origin + facade.P(new Vector3(0, 1.6f, 6));
                float score = RoadDistanceSquared(camera) + (referenceFootprints.Any(p => ContainsXZ(p, camera)) ? 1000000 : 0);
                if (score >= best) continue;
                selected = facade; chosen = section; best = score;
            }
        if (selected == null) return;
        void View(string kind, Vector3 target, float distance, bool eyeLevel = false, Vector3? localCamera = null)
        {
            Vector3 look = selected.P(target), camera = localCamera.HasValue ? selected.P(localCamera.Value) : look + selected.Forward * distance;
            if (eyeLevel) camera.y = 1.6f;
            var marker = new GameObject("Opening QA · " + kind).transform;
            marker.SetParent(root, false); marker.localPosition = camera;
            marker.localRotation = Quaternion.LookRotation(look - camera);
        }
        if (profile.style == "brick-arches")
        {
            // Narrow rectangular light and the outer arched light share the selected street-facing side.
            View("window", new Vector3(6.7f, 2.9f, 0), 4.4f);
            float spring = 2.9f + (chosen.floors - 1) * 3.1f + .08f;
            View("arch", new Vector3(chosen.width * .5f - 2.2f, spring + .15f, 0), 5.8f);
            View("central fan", new Vector3(0, chosen.Height - 2.45f + .2f, 1.35f), 6.0f);
            View("door", new Vector3(0, 1.9f, 1.35f), 4.8f, true);
            View("portal return", new Vector3(2.75f, 1.0f, 1.12f), 0, true, new Vector3(1.8f, 1.6f, 4.2f));
            Frame endView = null; float endScore = float.MaxValue;
            foreach (var section in sections)
                foreach (int sign in new[] { -1, 1 })
                {
                    var end = new Frame(null, section.center + section.rotation * new Vector3(sign * section.width * .5f, 0, 0),
                        section.rotation * Quaternion.Euler(0, sign > 0 ? 90 : -90, 0));
                    Vector3 camera = origin + end.P(new Vector3(4.2f, 1.65f, 4.4f));
                    float score = RoadDistanceSquared(camera) + (referenceFootprints.Any(p => ContainsXZ(p, camera)) ? 1000000 : 0);
                    if (score < endScore) { endScore = score; endView = end; }
                }
            var sideEntrance = new GameObject("Opening QA · side entrance").transform;
            sideEntrance.SetParent(root, false);
            sideEntrance.localPosition = endView.P(new Vector3(4.2f, 1.65f, 4.4f));
            sideEntrance.localRotation = Quaternion.LookRotation(endView.P(new Vector3(1.8f, 1.6f, .7f)) - sideEntrance.localPosition);
        }
        else
        {
            int columns = Math.Max(2, Mathf.RoundToInt((chosen.width - 2) / 3.2f));
            float x = chosen.width / columns * .5f - chosen.width * .5f;
            bool villa = profile.style.StartsWith("villa", StringComparison.Ordinal);
            // Inspect from inside the veranda's outer rail (z=1.45), with a lateral
            // offset providing enough viewing distance for the complete window/door.
            if (villa) View("window", new Vector3(x, 1.8f, 0), 0, false, new Vector3(x + 1.5f, 1.8f, 1.25f));
            else View("window", new Vector3(x, 1.8f, 0), 4.4f);
            if (profile.style == "office-stepped")
            {
                float height = .65f + chosen.floors * 3;
                View("arch", new Vector3(0, height - 1.15f, .64f), 5.2f);
                View("door", new Vector3(0, 1.7f, .64f), 4.8f, true);
            }
            else if (villa) View("door", new Vector3(0, 1.2f, 0), 0, false, new Vector3(1.9f, 1.6f, 1.25f));
            else View("door", new Vector3(0, 1.3f, 0), 4.8f, true);
        }
    }

    private static float RoadDistanceSquared(Vector3 p)
    {
        float best = float.MaxValue;
        for (int i = 1; i < mainRoad.Length; i++)
        {
            Vector3 a = mainRoad[i - 1], d = mainRoad[i] - a;
            Vector3 nearest = a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / d.sqrMagnitude);
            best = Mathf.Min(best, (nearest - p).sqrMagnitude);
        }
        return best;
    }

    private static bool ContainsXZ(Vector3[] polygon, Vector3 p)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            if ((polygon[i].z > p.z) != (polygon[j].z > p.z) &&
                p.x < (polygon[j].x - polygon[i].x) * (p.z - polygon[i].z) / (polygon[j].z - polygon[i].z) + polygon[i].x)
                inside = !inside;
        return inside;
    }

    internal static void Validate()
    {
        var buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>();
        var expected = profiles.Values.Where(p => p.style != "existing").Select(p => p.osmId).OrderBy(id => id);
        if (!buildings.Select(b => b.osmId).OrderBy(id => id).SequenceEqual(expected))
            throw new InvalidOperationException("Some reference buildings are missing or duplicated.");
        foreach (var building in buildings)
        {
            if (building.bodyColliders.Length == 0 || building.bodyColliders.Any(c => !c)
                || building.localBounds.size.sqrMagnitude < 1 || building.GetComponent<LODGroup>().GetLODs().Length != 3)
                throw new InvalidOperationException("Incomplete reference building: " + building.osmId);
            var triangles = building.GetComponent<LODGroup>().GetLODs().Select(l => TriangleCount(l.renderers[0])).ToArray();
            if (triangles[0] <= triangles[1] || triangles[1] <= triangles[2])
                throw new InvalidOperationException("Building LODs must reduce geometry: " + building.osmId);
        }
        Debug.Log("REFERENCE_BUILDINGS count=" + buildings.Length + " sections=" + buildings.Sum(b => b.sectionStoreys.Length));
        for (int lod = 0; lod < 3; lod++)
            Debug.Log("REFERENCE_GEOMETRY LOD" + lod + " triangles=" + buildings.Sum(b => TriangleCount(b.GetComponent<LODGroup>().GetLODs()[lod].renderers[0])));
    }

    private static long TriangleCount(Renderer renderer)
    {
        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        return Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i)) / 3;
    }

    private static Section[] ApartmentSections(Vector3[] p, Vector3 origin, Profile profile)
    {
        float min = p.Min(v => v.z), span = p.Max(v => v.z) - min;
        int count = profile.storeys.Length;
        if (span / count < 22 || span / count > 29) throw new InvalidOperationException("Section/footprint mismatch: " + profile.osmId);
        var sections = new Section[count];
        for (int i = 0; i < count; i++)
        {
            float z = min + span * (i + .5f) / count;
            var crossings = new List<float>();
            float cut = z - span / count * .38f;
            for (int j = 0; j < p.Length; j++)
            {
                Vector3 a = p[j], b = p[(j + 1) % p.Length];
                if ((a.z > cut) != (b.z > cut)) crossings.Add(Mathf.Lerp(a.x, b.x, (cut - a.z) / (b.z - a.z)));
            }
            if (crossings.Count != 2) throw new InvalidOperationException("Ambiguous section footprint: " + profile.osmId);
            float left = crossings.Min(), right = crossings.Max();
            sections[i] = new Section { center = new Vector3((left + right) * .5f, 0, z) - origin,
                width = span / count, depth = right - left, floors = profile.storeys[i], rotation = Quaternion.Euler(0, 90, 0) };
        }
        return sections;
    }

    private static Section[] SmallSection(Vector3[] p, Vector3 origin, Profile profile)
    {
        Vector3 axis = Enumerable.Range(0, p.Length).Select(i => p[(i + 1) % p.Length] - p[i]).OrderByDescending(v => v.sqrMagnitude).First().normalized;
        Vector3 normal = Vector3.Cross(axis, Vector3.up).normalized;
        Vector3 nearest = mainRoad[0]; float best = float.MaxValue;
        for (int i = 1; i < mainRoad.Length; i++)
        {
            Vector3 a = mainRoad[i - 1], d = mainRoad[i] - a;
            Vector3 q = a + d * Mathf.Clamp01(Vector3.Dot(origin - a, d) / d.sqrMagnitude);
            if ((q - origin).sqrMagnitude < best) { best = (q - origin).sqrMagnitude; nearest = q; }
        }
        if (Vector3.Dot(normal, nearest - origin) < 0) normal = -normal;
        Quaternion rotation = Quaternion.LookRotation(normal);
        Vector3[] local = p.Select(v => Quaternion.Inverse(rotation) * (v - origin)).ToArray();
        return new[] { new Section { center = Vector3.zero, rotation = rotation, floors = profile.storeys[0], height = .65f + profile.storeys[0] * 3.0f,
            width = local.Max(v => v.x) - local.Min(v => v.x), depth = local.Max(v => v.z) - local.Min(v => v.z) } };
    }

    private static void Apartment(Frame body, Section s, Profile profile, Material plinth, int lod)
    {
        // The black inner shell closes window recesses; it is not an invented apartment interior.
        body.Box(dark, Vector3.up * (s.Height * .5f), new Vector3(s.width - .35f, s.Height - .3f, s.depth - .35f));
        foreach (int sign in new[] { -1, 1 })
        {
            Facade(body.At(new Vector3(0, 0, sign * s.depth * .5f), sign > 0 ? 0 : 180), s, profile, plinth, lod);
            End(body.At(new Vector3(sign * s.width * .5f, 0, 0), sign > 0 ? 90 : -90), s, plinth, lod);
        }
        Roof(body, s, lod);
    }

    private static void Facade(Frame f, Section s, Profile profile, Material plinth, int lod)
    {
        float h = s.Height, half = s.width * .5f;
        Material centerMaterial = profile.redCenter ? red : cream;
        var holes = new List<Rect>();
        for (int sign = -1; sign <= 1; sign += 2)
        {
            float wide = sign * (half - 2.2f), narrow = sign * 6.7f;
            for (int floor = 0; floor < s.floors; floor++)
            {
                float y = 2.9f + floor * 3.1f;
                // The photographs contain plain pairs, upper lights and a top light in one leaf.
                // Keep these observed families instead of applying the same transom to every light.
                Window(f, holes, narrow, y, 1.28f, 1.8f, 0, lod, 2, floor,
                    floor % 3 != 1, red, floor % 3 == 0 ? 1 : -1);
                if (floor == s.floors - 1)
                    QuarterWindow(f, holes, new Rect(wide - 1.15f, y + .08f - 1.12f, 2.3f, 2.27f), 0, red, lod, sign);
                else Window(f, holes, wide, y, 2.3f, 2.05f, 0, lod, 3, floor + 1, true, red);
                if (floor < s.floors - 1)
                {
                    f.Box(cream, new Vector3(wide, y + 1.52f, .022f), new Vector3(2.52f, .94f, .044f));
                    f.Box(cream, new Vector3(narrow, y + 1.52f, .022f), new Vector3(1.5f, .94f, .044f));
                }
            }
            Window(f, holes, wide, .76f, .78f, .68f, .018f, lod, 1, 0, false, plinth);
            Wall(f, red, plinth, sign < 0 ? -half : 4.8f, sign < 0 ? -4.8f : half, h, 0, holes);
            holes.Clear();
            if (lod < 2) Balcony(f, wide, 2.9f + (s.floors - 2) * 3.1f - 1.03f, lod);
            f.Box(trim, new Vector3(sign * (half + 4.8f) * .5f, h, .03f), new Vector3(half - 4.65f, .18f, .28f));
            if (lod < 2)
            {
                Downspout(f, sign * 5.15f, h + .10f, .15f, .052f, lod);
                f.Rod(roofEdge, new Vector3(sign * 4.8f, h + .08f, .12f), new Vector3(sign * half, h + .08f, .12f), .055f);
            }
        }
        const float z = 1.35f;
        Doorway(f, holes, z, centerMaterial, plinth, lod);
        float spring = h - 2.45f;
        for (int floor = 1; floor < s.floors - 1; floor++)
            foreach (int sign in new[] { -1, 1 }) Window(f, holes, sign * 1.25f, 2.9f + floor * 3.1f, 1.42f, 1.82f, z, lod, 2, floor,
                floor % 2 == 0 || sign > 0, centerMaterial, floor % 2 == 0 ? -1 : 1);
        SplitArchedWindow(f, holes, 0, spring, 2.05f, 1.30f, z, centerMaterial, lod);
        foreach (int sign in new[] { -1, 1 })
        {
            // Three planar faces preserve the faceted bay instead of a flat painted stripe.
            float x = sign * 3.9f;
            holes.Add(Rect.MinMaxRect(x - .54f, 4.55f, x + .54f, h - 1.6f));
            Bay(f, x, z, s.floors, lod);
            f.Box(centerMaterial, new Vector3(sign * 4.72f, h * .5f, z * .5f), new Vector3(.16f, h, z));
        }
        Wall(f, centerMaterial, plinth, -4.8f, 4.8f, h, z, holes);
        f.Box(trim, new Vector3(0, h + .08f, z + .03f), new Vector3(9.8f, .16f, .24f));
        Gable(f, centerMaterial, 0, 4.9f, h, 1.42f, z + .08f, lod);
        if (lod == 0)
        {
            f.Box(trim, new Vector3(2.45f, 4.8f, z + .25f), new Vector3(.73f, .48f, .42f));
            for (int i = 0; i < 7; i++) f.Box(roofEdge, new Vector3(2.21f + i * .075f, 4.8f, z + .47f), new Vector3(.018f, .31f, .02f));
        }
    }

    private static void End(Frame f, Section s, Material plinth, int lod)
    {
        float half = s.depth * .5f, h = s.Height;
        var holes = new List<Rect>();
        foreach (float x in new[] { -half * .69f, -2.1f, 2.1f, half * .69f })
        {
            for (int floor = 0; floor < s.floors; floor++)
            {
                // The end photograph separates the first light from the pitched basement
                // hood by masonry. Correct only that light; keep its vertical proportions
                // and the other window rows instead of slicing a roof through its glass.
                bool aboveEntrance = floor == 0 && Mathf.Abs(x - 1.8f) < 1.2f;
                float windowHeight = aboveEntrance ? 1.50f : 1.8f;
                float windowY = 2.9f + floor * 3.1f + (aboveEntrance ? .60f : 0);
                Window(f, holes, x, windowY, 1.38f, windowHeight, 0, lod, 2, floor, floor % 2 == 1, red);
            }
            if (Mathf.Abs(x) > 3) Window(f, holes, x, .76f, .78f, .68f, .02f, lod, 1, 0, false, plinth);
        }
        // The existing low end entrance stays below the first-floor sill. Previously its
        // 2.04 m leaf crossed the window starting at 2.00 m and was pasted onto a solid wall.
        EntranceDoor(f, holes, 1.8f, .02f, 1.35f, 1.85f, 0, lod, false);
        Wall(f, red, plinth, -half, half, h, 0, holes, true);
        f.Box(trim, new Vector3(0, h, .03f), new Vector3(s.depth + .3f, .18f, .3f));
        foreach (int sign in new[] { -1, 1 }) if (lod < 2)
            Downspout(f, sign * 4.6f, h + .1f, .13f, .05f, lod);
        EndCanopy(f, lod);
    }

    private static void Downspout(Frame f, float x, float top, float wallOffset, float radius, int lod)
    {
        // Complete the already placed drains, rather than adding arbitrary pipes.
        // The supplied courtyard view shows wall brackets and a low outward outlet.
        Vector3 start = new Vector3(x, .30f, wallOffset);
        f.Rod(roofEdge, new Vector3(x, top, wallOffset), start, radius, lod == 0 ? 10 : 6);
        Vector3 control = new Vector3(x, .16f, wallOffset);
        Vector3 bendEnd = new Vector3(x, .16f, wallOffset + .14f);
        Vector3 previous = start; int segments = lod == 0 ? 6 : 3;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            Vector3 current = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control + t * t * bendEnd;
            f.Rod(roofEdge, previous, current, radius, lod == 0 ? 10 : 6);
            previous = current;
        }
        Vector3 mouth = new Vector3(x, .12f, wallOffset + .40f);
        Vector3 axis = (mouth - bendEnd).normalized;
        Vector3 u = Vector3.right, v = Vector3.Cross(axis, u).normalized;
        int sides = lod == 0 ? 12 : 8;
        var interior = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.PI * 2 * i / sides, b = Mathf.PI * 2 * (i + 1) / sides;
            Vector3 p = u * Mathf.Cos(a) + v * Mathf.Sin(a), q = u * Mathf.Cos(b) + v * Mathf.Sin(b);
            f.Face(roofEdge, (p + q).normalized, bendEnd + p * radius, mouth + p * radius,
                mouth + q * radius, bendEnd + q * radius);
            // An annular rolled lip leaves the mouth open; an ordinary capped cylinder
            // would leave a solid green disk at the visible end of the downpipe.
            f.Face(roofEdge, axis, mouth + p * (radius + .003f), mouth + q * (radius + .003f),
                mouth + q * (radius - .009f), mouth + p * (radius - .009f));
            f.Face(dark, -(p + q).normalized, mouth + p * (radius - .009f), mouth + q * (radius - .009f),
                mouth + q * (radius - .009f) - axis * .055f, mouth + p * (radius - .009f) - axis * .055f);
            interior[i] = mouth + p * (radius - .009f) - axis * .055f;
        }
        f.Face(dark, axis, interior);
        for (float y = .95f; y < top - .25f; y += 3.1f)
        {
            f.Box(metal, new Vector3(x, y, .015f), new Vector3(.08f, .10f, .025f));
            f.Rod(metal, new Vector3(x, y, .025f), new Vector3(x, y, wallOffset - radius), .016f, 4);
            f.Rod(roofEdge, new Vector3(x, y - .012f, wallOffset), new Vector3(x, y + .012f, wallOffset), radius + .009f, lod == 0 ? 10 : 6);
        }
    }

    private static void EndCanopy(Frame f, int lod)
    {
        // Observed in the supplied end/courtyard photographs: green pitched sheet metal,
        // a closed front gable, supported corners and diagonal braces. Dimensions remain
        // an estimate, rather than an assertion of a measured survey.
        const float center = 1.8f, half = 1.45f, back = .02f, front = 1.70f;
        const float eave = 2.10f, ridge = 2.60f, thickness = .04f;
        void Panel(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (normal.y < 0) normal = -normal;
            Vector3 down = Vector3.down * thickness;
            f.Face(roof, normal, a, b, c, d);
            f.Face(roofEdge, -normal, a + down, b + down, c + down, d + down);
            var corners = new[] { a, b, c, d };
            Vector3 middle = (a + b + c + d) * .25f;
            for (int edge = 0; edge < corners.Length; edge++)
            {
                Vector3 p = corners[edge], q = corners[(edge + 1) % corners.Length];
                Vector3 side = Vector3.Cross(Vector3.up, q - p).normalized;
                if (Vector3.Dot(side, (p + q) * .5f - middle) < 0) side = -side;
                f.Face(roofEdge, side, p, q, q + down, p + down);
            }
        }
        Panel(new Vector3(center - half, eave, back), new Vector3(center, ridge, back),
            new Vector3(center, ridge, front), new Vector3(center - half, eave, front));
        Panel(new Vector3(center, ridge, back), new Vector3(center + half, eave, back),
            new Vector3(center + half, eave, front), new Vector3(center, ridge, front));
        f.Face(roof, Vector3.forward, new Vector3(center - half, eave - thickness, front),
            new Vector3(center + half, eave - thickness, front), new Vector3(center, ridge - thickness, front));
        f.Face(roof, Vector3.back, new Vector3(center - half, eave - thickness, back),
            new Vector3(center + half, eave - thickness, back), new Vector3(center, ridge - thickness, back));
        f.Box(roofEdge, new Vector3(center, ridge + .006f, (back + front) * .5f), new Vector3(.09f, .025f, front - back + .07f));
        foreach (int sign in new[] { -1, 1 })
        {
            float post = center + sign * (half - .12f);
            f.Box(metal, new Vector3(post, 1.065f, front - .16f), new Vector3(.065f, 1.99f, .065f));
            f.Box(concrete, new Vector3(post, .075f, front - .16f), new Vector3(.17f, .15f, .17f));
            f.Box(roofEdge, new Vector3(post, eave - .065f, (back + front - .16f) * .5f), new Vector3(.065f, .065f, front - back - .16f));
            if (lod < 2)
            {
                f.Rod(metal, new Vector3(post, 1.52f, front - .16f), new Vector3(post - sign * .38f, eave - .06f, front - .16f), .022f, 4);
                f.Rod(metal, new Vector3(post, 1.52f, front - .16f), new Vector3(post, eave - .06f, front - .66f), .022f, 4);
            }
        }
        f.Box(roofEdge, new Vector3(center, eave - .065f, front - .16f), new Vector3(half * 2 - .18f, .065f, .065f));
        // Flashing follows both pitches at the wall; fine standing seams are near LOD only.
        foreach (int sign in new[] { -1, 1 })
            f.Rod(roofEdge, new Vector3(center + sign * half, eave + .014f, back), new Vector3(center, ridge + .014f, back), .022f, 4);
        if (lod == 0)
            for (float x = center - half + .18f; x < center + half; x += .24f)
            {
                float y = eave + (ridge - eave) * (1 - Mathf.Abs(x - center) / half) + .009f;
                f.Rod(roofEdge, new Vector3(x, y, back), new Vector3(x, y, front), .007f, 4);
            }
    }

    private static void Wall(Frame f, Material material, Material plinth, float left, float right, float h, float z, List<Rect> holes, bool end = false, float plinthHeight = 1.5f)
    {
        var xs = new List<float> { left, right };
        var ys = new List<float> { 0, plinthHeight, h };
        foreach (var r in holes) { xs.Add(Mathf.Clamp(r.xMin, left, right)); xs.Add(Mathf.Clamp(r.xMax, left, right)); ys.Add(Mathf.Clamp(r.yMin, 0, h)); ys.Add(Mathf.Clamp(r.yMax, 0, h)); }
        if (end) foreach (float x in new[] { left * .69f, right * .69f }) { xs.Add(x - 1.15f); xs.Add(x + 1.15f); }
        var xCuts = xs.Distinct().OrderBy(x => x).ToArray(); var yCuts = ys.Distinct().OrderBy(y => y).ToArray();
        for (int x = 1; x < xCuts.Length; x++) for (int y = 1; y < yCuts.Length; y++)
        {
            float a = xCuts[x - 1], b = xCuts[x], c = yCuts[y - 1], d = yCuts[y];
            Vector2 middle = new Vector2((a + b) * .5f, (c + d) * .5f);
            if (b - a < .001f || d - c < .001f || holes.Any(r => r.Contains(middle))) continue;
            Material m = middle.y < plinthHeight ? plinth : end && middle.y < h - 3 && Mathf.Abs(Mathf.Abs(middle.x) - right * .69f) < 1.16f ? cream : material;
            f.Face(m, Vector3.forward, new Vector3(a, c, z), new Vector3(b, c, z), new Vector3(b, d, z), new Vector3(a, d, z));
        }
    }

    private static void Window(Frame f, List<Rect> holes, float x, float y, float w, float h, float z, int lod,
        int panes = 2, int variant = 0, bool transom = true, Material masonry = null, int transomPane = -1)
    {
        // Keep real holes even in LOD2: coplanar window decals flicker in the far depth buffer.
        holes.Add(new Rect(x - w * .5f, y - h * .5f, w, h));
        const float glassDepth = -.149f;
        Flat(f, glass[Math.Abs(variant) % glass.Length], x, y, w, h, z + glassDepth);
        if (lod < 2) Reveal(f, masonry ? masonry : concrete, new Rect(x - w * .5f, y - h * .5f, w, h), z, z - .085f);
        foreach (int sign in new[] { -1, 1 })
        {
            if (lod == 0)
            {
                f.Box(trim, new Vector3(x + sign * (w * .5f - .0275f), y, z - .115f), new Vector3(.055f, h, .06f));
                f.Box(trim, new Vector3(x, y + sign * (h * .5f - .0275f), z - .115f), new Vector3(w, .055f, .06f));
            }
            else
            {
                Flat(f, trim, x + sign * (w * .5f - .0275f), y, .055f, h, z - .085f);
                Flat(f, trim, x, y + sign * (h * .5f - .0275f), w, .055f, z - .085f);
            }
        }
        // Preserve the main division at distance: a two/three-light window must not become
        // one large dark sheet when LOD changes. Fine sash rebates remain in LOD0 only.
        for (int pane = 1; pane < panes; pane++)
        {
            float u = x - w * .5f + w * pane / panes;
            if (lod == 0) f.Box(trim, new Vector3(u, y, z - .11f), new Vector3(.065f, h - .10f, .06f));
            else Flat(f, trim, u, y, .065f, h - .10f, z - .08f);
        }
        if (transom && h > 1.2f)
        {
            float lightWidth = transomPane < 0 ? w - .10f : w / panes - .055f;
            float lightX = transomPane < 0 ? x : x - w * .5f + w / panes * (transomPane + .5f);
            if (lod == 0) f.Box(trim, new Vector3(lightX, y + h * .26f, z - .11f), new Vector3(lightWidth, .045f, .06f));
            else Flat(f, trim, lightX, y + h * .26f, lightWidth, .045f, z - .08f);
        }
        else if (panes == 1 && h < 1)
            Flat(f, trim, x, y, w - .10f, .035f, z - .08f);
        if (lod == 0)
        {
            // Fine sash rebates are 12 mm, not a second broad frame around every pane.
            float paneWidth = w / panes;
            for (int pane = 0; pane < panes; pane++)
            {
                float u = x - w * .5f + paneWidth * (pane + .5f);
                foreach (int side in new[] { -1, 1 })
                {
                    f.Box(trim, new Vector3(u + side * (paneWidth * .5f - .077f), y, z - .135f), new Vector3(.012f, h - .13f, .018f));
                    f.Box(trim, new Vector3(u, y + side * (h * .5f - .077f), z - .135f), new Vector3(paneWidth - .13f, .012f, .018f));
                }
            }
            f.Box(trim, new Vector3(x, y - h * .5f - .018f, z - .005f), new Vector3(w + .08f, .035f, .23f));
        }
    }

    private static void Reveal(Frame f, Material material, Rect opening, float front, float back)
    {
        float a = opening.xMin, b = opening.xMax, c = opening.yMin, d = opening.yMax;
        f.Face(material, Vector3.right, new Vector3(a, c, front), new Vector3(a, c, back), new Vector3(a, d, back), new Vector3(a, d, front));
        f.Face(material, Vector3.left, new Vector3(b, c, front), new Vector3(b, d, front), new Vector3(b, d, back), new Vector3(b, c, back));
        f.Face(material, Vector3.down, new Vector3(a, d, front), new Vector3(a, d, back), new Vector3(b, d, back), new Vector3(b, d, front));
        f.Face(material, Vector3.up, new Vector3(a, c, front), new Vector3(b, c, front), new Vector3(b, c, back), new Vector3(a, c, back));
    }

    private static void Flat(Frame f, Material material, float x, float y, float width, float height, float z) =>
        f.Face(material, Vector3.forward, new Vector3(x - width * .5f, y - height * .5f, z), new Vector3(x + width * .5f, y - height * .5f, z),
            new Vector3(x + width * .5f, y + height * .5f, z), new Vector3(x - width * .5f, y + height * .5f, z));

    private static Vector3 ArcPoint(float x, float y, float rx, float ry, float angle, float z, int mirror = 1) =>
        new Vector3(x + mirror * rx * Mathf.Cos(angle), y + ry * Mathf.Sin(angle), z);

    private static void ArcProfile(Frame f, float x, float y, float rx, float ry, float from, float to,
        float z, float width, int lod, int mirror = 1, Material masonry = null)
    {
        int count = lod == 0 ? 32 : lod == 1 ? 20 : 12;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Lerp(from, to, (float)i / count), b = Mathf.Lerp(from, to, (float)(i + 1) / count);
            Vector3 p = ArcPoint(x, y, rx, ry, a, z - .085f, mirror), q = ArcPoint(x, y, rx, ry, b, z - .085f, mirror);
            Vector3 ip = ArcPoint(x, y, rx - width, ry - width, a, z - .085f, mirror);
            Vector3 iq = ArcPoint(x, y, rx - width, ry - width, b, z - .085f, mirror);
            f.Face(trim, Vector3.forward, p, q, iq, ip);
            if (lod < 2)
            {
                Vector3 inward = new Vector3(-mirror * Mathf.Cos((a + b) * .5f), -Mathf.Sin((a + b) * .5f), 0);
                f.Face(trim, inward, ip, iq, iq - Vector3.forward * .06f, ip - Vector3.forward * .06f);
                if (masonry) f.Face(masonry, inward, p + Vector3.forward * .085f, q + Vector3.forward * .085f, q, p);
            }
            if (!masonry) continue;
            Vector3 op = ArcPoint(x, y, rx + .15f, ry + .15f, a, z + .002f, mirror);
            Vector3 oq = ArcPoint(x, y, rx + .15f, ry + .15f, b, z + .002f, mirror);
            f.Face(masonry, Vector3.forward, p + Vector3.forward * .087f, q + Vector3.forward * .087f, oq, op);
            if (lod == 0 && i > 0)
                FlatBar(f, concrete, ArcPoint(x, y, rx + .008f, ry + .008f, a, z + .004f, mirror),
                    ArcPoint(x, y, rx + .145f, ry + .145f, a, z + .004f, mirror), .007f, 0);
        }
    }

    private static void QuarterWindow(Frame f, List<Rect> holes, Rect bounds, float z, Material wall, int lod, int outerSide)
    {
        // The red wings have a quarter-circle fan, mirrored at the two outer corners.
        // Its former semicircle plus rectangle occupied this exact same bounding box.
        holes.Add(bounds);
        float cx = bounds.center.x + outerSide * bounds.width * .5f, cy = bounds.yMin;
        int mirror = -outerSide, count = lod == 0 ? 32 : lod == 1 ? 20 : 12;
        Vector3 center = new Vector3(cx, cy, z - .149f);
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.PI * .5f * i / count, b = Mathf.PI * .5f * (i + 1) / count;
            Vector3 p = ArcPoint(cx, cy, bounds.width, bounds.height, a, z, mirror);
            Vector3 q = ArcPoint(cx, cy, bounds.width, bounds.height, b, z, mirror);
            f.WindowFace(fanGlass, bounds, new Vector3(bounds.center.x, bounds.center.y, z - .149f),
                center, p - Vector3.forward * .149f, q - Vector3.forward * .149f);
            f.Face(wall, Vector3.forward, p, q, new Vector3(q.x, bounds.yMax, z), new Vector3(p.x, bounds.yMax, z));
        }
        ArcProfile(f, cx, cy, bounds.width, bounds.height, 0, Mathf.PI * .5f, z, .055f, lod, mirror, wall);
        foreach (float fraction in new[] { .34f, .68f })
            ArcProfile(f, cx, cy, bounds.width * fraction + .018f, bounds.height * fraction + .018f,
                0, Mathf.PI * .5f, z, .036f, lod, mirror);
        foreach (float angle in new[] { 0f, Mathf.PI * .5f })
            FlatBar(f, trim, new Vector3(cx, cy, z - .085f),
                ArcPoint(cx, cy, bounds.width, bounds.height, angle, z - .085f, mirror), .055f, lod == 0 ? .06f : 0);
        for (int ray = 1; ray < 5; ray++)
            FlatBar(f, trim, new Vector3(cx, cy, z - .08f),
                ArcPoint(cx, cy, bounds.width - .055f, bounds.height - .055f, ray * Mathf.PI / 10, z - .08f, mirror), .035f, lod == 0 ? .055f : 0);
        if (lod < 2)
        {
            float inner = cx + mirror * bounds.width;
            f.Face(wall, Vector3.up, new Vector3(cx, cy, z), new Vector3(inner, cy, z), new Vector3(inner, cy, z - .085f), new Vector3(cx, cy, z - .085f));
            f.Face(wall, Vector3.right * mirror, new Vector3(cx, cy, z), new Vector3(cx, bounds.yMax, z),
                new Vector3(cx, bounds.yMax, z - .085f), new Vector3(cx, cy, z - .085f));
        }
    }

    private static void SplitArchedWindow(Frame f, List<Rect> holes, float x, float spring, float radius,
        float below, float z, Material wall, int lod)
    {
        const float pierHalf = .54f;
        var bounds = new Rect(x - radius, spring - below, radius * 2, radius + below);
        holes.Add(bounds);
        float lightWidth = radius - pierHalf, lightHeight = below - .14f;
        foreach (int side in new[] { -1, 1 })
        {
            float lightX = x + side * (radius + pierHalf) * .5f;
            Window(f, holes, lightX, spring - .14f - lightHeight * .5f, lightWidth, lightHeight,
                z, lod, 2, side > 0 ? 1 : 0, true, wall, side > 0 ? 1 : -1);
            Flat(f, red, lightX, spring - .07f, lightWidth, .14f, z + .001f);
        }
        // Solid masonry continues between the two lower windows, with a curved cap.
        Flat(f, wall, x, spring - below * .5f, pierHalf * 2, below, z);
        int count = lod == 0 ? 40 : lod == 1 ? 24 : 16;
        Vector3 pierCenter = new Vector3(x, spring, z);
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.PI * i / count, b = Mathf.PI * (i + 1) / count;
            Vector3 p = ArcPoint(x, spring, radius, radius, a, z), q = ArcPoint(x, spring, radius, radius, b, z);
            Vector3 ip = ArcPoint(x, spring, pierHalf, pierHalf, a, z), iq = ArcPoint(x, spring, pierHalf, pierHalf, b, z);
            f.WindowFace(fanGlass, bounds, new Vector3(x, spring - below * .5f, z - .149f),
                ip - Vector3.forward * .149f, p - Vector3.forward * .149f, q - Vector3.forward * .149f, iq - Vector3.forward * .149f);
            f.Face(wall, Vector3.forward, pierCenter, ip, iq);
            f.Face(wall, Vector3.forward, p, q, new Vector3(q.x, spring + radius, z), new Vector3(p.x, spring + radius, z));
        }
        ArcProfile(f, x, spring, radius, radius, 0, Mathf.PI, z, .055f, lod, 1, wall);
        ArcProfile(f, x, spring, pierHalf + .045f, pierHalf + .045f, 0, Mathf.PI, z, .045f, lod);
        for (int ray = 1; ray < 6; ray++)
        {
            float angle = ray * Mathf.PI / 6;
            FlatBar(f, trim, ArcPoint(x, spring, pierHalf + .045f, pierHalf + .045f, angle, z - .08f),
                ArcPoint(x, spring, radius - .055f, radius - .055f, angle, z - .08f), .038f, lod == 0 ? .055f : 0);
        }
    }

    private static void ArchedWindow(Frame f, List<Rect> holes, float x, float spring, float radius, float below, float z, Material wall, int lod, bool fan)
    {
        Window(f, holes, x, spring - below * .5f, radius * 2, below, z, lod, fan ? 3 : 2, 0, false, wall);
        holes.Add(new Rect(x - radius, spring, radius * 2, radius));
        int segments = lod == 0 ? 20 : lod == 1 ? 12 : 8;
        const float glassDepth = -.149f;
        Vector3 center = new Vector3(x, spring, z + glassDepth);
        for (int i = 0; i < segments; i++)
        {
            float a = Mathf.PI * i / segments, b = Mathf.PI * (i + 1) / segments;
            Vector3 p = new Vector3(x + Mathf.Cos(a) * radius, spring + Mathf.Sin(a) * radius, z);
            Vector3 q = new Vector3(x + Mathf.Cos(b) * radius, spring + Mathf.Sin(b) * radius, z);
            f.WindowFace(glass[1], new Rect(x - radius, spring - below, radius * 2, radius + below),
                new Vector3(x, spring - below * .5f, z + glassDepth), center, p + Vector3.forward * glassDepth, q + Vector3.forward * glassDepth);
            f.Face(wall, Vector3.forward, p, q, new Vector3(q.x, spring + radius, z), new Vector3(p.x, spring + radius, z));
        }
        ArcProfile(f, x, spring, radius, radius, 0, Mathf.PI, z, .055f, lod, 1, wall);
        if (lod < 2)
            for (int i = 1; i < (fan ? 6 : 2); i++)
            {
                float angle = Mathf.PI * i / (fan ? 6 : 2);
                FlatBar(f, trim, new Vector3(x, spring, z - .08f),
                    new Vector3(x + Mathf.Cos(angle) * (radius - .055f), spring + Mathf.Sin(angle) * (radius - .055f), z - .08f), .038f, lod == 0 ? .055f : 0);
            }
    }

    private static void FlatBar(Frame f, Material material, Vector3 a, Vector3 b, float width, float depth)
    {
        Vector3 direction = (b - a).normalized, side = new Vector3(-direction.y, direction.x, 0) * width * .5f;
        f.Face(material, Vector3.forward, a - side, b - side, b + side, a + side);
        if (depth <= 0) return;
        Vector3 back = Vector3.back * depth;
        f.Face(material, side.normalized, a + side, b + side, b + side + back, a + side + back);
        f.Face(material, -side.normalized, a - side, a - side + back, b - side + back, b - side);
    }

    private static void Doorway(Frame f, List<Rect> holes, float z, Material wall, Material plinth, int lod)
    {
        holes.Add(new Rect(-2.75f, 0, 5.5f, 4.2f));
        f.Box(red, new Vector3(0, 2.1f, z - .55f), new Vector3(5.5f, 4.2f, .15f));
        const float backDepth = .475f;
        foreach (int sign in new[] { -1, 1 })
        {
            // Close the real depth of the plinth jamb. The missing inward face exposed
            // the single front polygon as a turquoise fin beside the entrance.
            float x = sign * 2.75f;
            f.Face(plinth, Vector3.left * sign, new Vector3(x, 0, z), new Vector3(x, 1.35f, z),
                new Vector3(x, 1.35f, z - backDepth), new Vector3(x, 0, z - backDepth));
        }
        for (int i = 0; i < 16; i++)
        {
            float a = Mathf.PI * i / 16, b = Mathf.PI * (i + 1) / 16;
            Vector3 p = new Vector3(2.75f * Mathf.Cos(a), 1.35f + 2.75f * Mathf.Sin(a), z);
            Vector3 q = new Vector3(2.75f * Mathf.Cos(b), 1.35f + 2.75f * Mathf.Sin(b), z);
            f.Face(wall, Vector3.forward, p, q, new Vector3(q.x, 4.2f, z), new Vector3(p.x, 4.2f, z));
            Vector3 inward = -(p + q - new Vector3(0, 2.7f, z * 2)).normalized;
            f.Face(wall, inward, p, q, q - Vector3.forward * backDepth, p - Vector3.forward * backDepth);
        }
        foreach (int sign in new[] { -1, 1 }) f.Box(darkBase, new Vector3(sign * 1.2f, 2.27f, z + .12f), new Vector3(.32f, 4.54f, .48f));
        f.Box(darkBase, new Vector3(0, 4.4f, z + .12f), new Vector3(2.74f, .28f, .48f));
        // Keep the tall entrance portal, but split a normal 2.10 m door leaf from the
        // fixed light above. The former 2.90 m solid leaf overlapped this same glass.
        EntranceDoor(f, new List<Rect>(), 0, .64f, 1.78f, 2.10f, z, lod, true);
        PorchFanlight(f, 0, 2.86f, .83f, .58f, z, lod);
        if (lod < 2)
        {
            f.Box(metal, new Vector3(1.01f, 1.95f, z + .39f), new Vector3(.14f, .24f, .05f));
            f.mesh.Ellipsoid(trim, f.P(new Vector3(0, 3.84f, z + .08f)), Vector3.one * .14f, 8, 4);
        }
        for (int i = 0; i < 4; i++) f.Box(concrete, new Vector3(0, (i + 1) * .08f, 2.55f - i * .29f), new Vector3(2.4f, (i + 1) * .16f, .30f));
        f.Box(concrete, new Vector3(0, .32f, 1.5f), new Vector3(2.4f, .64f, .9f));
    }

    private static void PorchFanlight(Frame f, float x, float spring, float rx, float ry, float z, int lod)
    {
        // The courtyard photograph shows an arched fixed light behind the ordinary door,
        // inside the existing high rectangular portal, rather than an extra-tall door leaf.
        int count = lod == 0 ? 24 : lod == 1 ? 16 : 10;
        var bounds = new Rect(x - rx, spring, rx * 2, ry);
        Vector3 center = new Vector3(x, spring, z - .149f);
        for (int i = 0; i < count; i++)
            f.WindowFace(fanGlass, bounds, new Vector3(x, spring + ry * .5f, z - .149f), center,
                ArcPoint(x, spring, rx, ry, Mathf.PI * i / count, z - .149f),
                ArcPoint(x, spring, rx, ry, Mathf.PI * (i + 1) / count, z - .149f));
        ArcProfile(f, x, spring, rx, ry, 0, Mathf.PI, z, .045f, lod);
        ArcProfile(f, x, spring, rx * .61f, ry * .61f, 0, Mathf.PI, z, .025f, lod);
        FlatBar(f, trim, new Vector3(x - rx, spring, z - .085f), new Vector3(x + rx, spring, z - .085f), .045f, lod == 0 ? .055f : 0);
    }

    private static void EntranceDoor(Frame f, List<Rect> holes, float x, float bottom, float width,
        float leafHeight, float z, int lod, bool doubleLeaf, float fixedLight = 0)
    {
        const float jamb = .06f;
        float height = leafHeight + jamb * 2 + (fixedLight > 0 ? fixedLight + jamb : 0);
        var opening = new Rect(x - width * .5f, bottom, width, height);
        holes.Add(opening);
        if (lod < 2) Reveal(f, concrete, opening, z, z - .18f);
        float leafBottom = bottom + jamb, leafTop = leafBottom + leafHeight;
        f.Box(door, new Vector3(x, (leafBottom + leafTop) * .5f, z - .065f), new Vector3(width - jamb * 2, leafHeight, .07f));
        foreach (int side in new[] { -1, 1 })
        {
            if (lod == 0) f.Box(metal, new Vector3(x + side * (width * .5f - jamb * .5f), bottom + height * .5f, z - .035f), new Vector3(jamb, height, .17f));
            else Flat(f, metal, x + side * (width * .5f - jamb * .5f), bottom + height * .5f, jamb, height, z + .035f);
        }
        foreach (float level in new[] { bottom + jamb * .5f, bottom + height - jamb * .5f })
            if (lod == 0) f.Box(metal, new Vector3(x, level, z - .035f), new Vector3(width, jamb, .17f));
            else Flat(f, metal, x, level, width, jamb, z + .035f);
        if (fixedLight > 0)
        {
            float lightBottom = leafTop + jamb;
            Flat(f, glass[0], x, lightBottom + fixedLight * .5f, width - jamb * 2, fixedLight, z - .075f);
            if (lod == 0) f.Box(metal, new Vector3(x, leafTop + jamb * .5f, z - .035f), new Vector3(width - jamb * 2, jamb, .17f));
            else Flat(f, metal, x, leafTop + jamb * .5f, width - jamb * 2, jamb, z + .035f);
        }
        float seam = x + width * .22f;
        if (doubleLeaf) Flat(f, metal, seam, (leafBottom + leafTop) * .5f, .035f, leafHeight, z - .025f);
        if (lod < 2)
        {
            float handle = doubleLeaf ? seam - .13f : x + width * .30f;
            float handleY = bottom + Mathf.Min(1.02f, leafHeight * .56f);
            f.Rod(brass, new Vector3(handle, handleY - .13f, z + .035f), new Vector3(handle, handleY + .13f, z + .035f), .016f);
            f.Box(metal, new Vector3(handle, handleY, z - .008f), new Vector3(.065f, .31f, .025f));
            // Door panel seams remain shallow; no opaque plate is placed across a window.
            foreach (float level in new[] { leafBottom + .20f, leafTop - .22f })
                Flat(f, metal, x, level, width - .23f, .018f, z - .027f);
        }
        f.Box(concrete, new Vector3(x, bottom + .012f, z + .015f), new Vector3(width + .08f, .025f, .24f));
    }

    private static void Bay(Frame f, float x, float z, int floors, int lod)
    {
        Vector3[] edge = { new Vector3(x - .58f, 0, z), new Vector3(x - .36f, 0, z + .48f), new Vector3(x + .36f, 0, z + .48f), new Vector3(x + .58f, 0, z) };
        f.Box(cream, new Vector3(x, 2.3f, z + .12f), new Vector3(1.15f, 4.6f, .24f));
        for (int floor = 1; floor < floors; floor++)
        {
            float y = 2.9f + floor * 3.1f;
            for (int j = 0; j < 3; j++)
            {
                Vector3 a = edge[j], b = edge[j + 1], normal = new Vector3(-(b - a).z, 0, (b - a).x).normalized;
                if (normal.z < 0) normal = -normal;
                float yaw = Mathf.Atan2(normal.x, normal.z) * Mathf.Rad2Deg;
                Frame panel = f.At((a + b) * .5f, yaw);
                Window(panel, new List<Rect>(), 0, y, (b - a).magnitude, 1.84f, 0,
                    lod, 1, floor, floor % 2 == 0, cream);
                foreach (int sign in new[] { -1, 1 }) f.Face(cream, normal,
                    a + Vector3.up * (y + sign * 1.235f - .315f), b + Vector3.up * (y + sign * 1.235f - .315f),
                    b + Vector3.up * (y + sign * 1.235f + .315f), a + Vector3.up * (y + sign * 1.235f + .315f));
            }
        }
    }

    private static void Balcony(Frame f, float x, float y, int lod)
    {
        f.Box(concrete, new Vector3(x, y - .06f, .34f), new Vector3(2.5f, .12f, .72f));
        foreach (float level in new[] { y + .13f, y + .91f }) f.Rod(metal, new Vector3(x - 1.2f, level, .68f), new Vector3(x + 1.2f, level, .68f), .028f);
        for (int i = 0; i <= (lod == 0 ? 8 : 3); i++)
        {
            float u = x - 1.18f + 2.36f * i / (lod == 0 ? 8 : 3);
            f.Rod(metal, new Vector3(u, y + .1f, .68f), new Vector3(u, y + .94f, .68f), .018f, 5);
        }
        foreach (int sign in new[] { -1, 1 }) f.Rod(metal, new Vector3(x + sign * 1.2f, y + .91f, .03f), new Vector3(x + sign * 1.2f, y + .91f, .68f), .025f);
    }

    private static void Gable(Frame f, Material material, float x, float halfWidth, float y, float rise, float z, int lod)
    {
        var a = new Vector3(x - halfWidth, y, z); var b = new Vector3(x + halfWidth, y, z); var c = new Vector3(x, y + rise, z);
        f.Face(material, Vector3.forward, a, b, c);
        if (lod < 2) { f.Rod(trim, a, c, .07f); f.Rod(trim, c, b, .07f); }
        if (lod == 0)
        {
            Vector3 center = new Vector3(x, y + rise * .41f, z + .025f);
            f.mesh.Ellipsoid(dark, f.P(center), Vector3.one * .18f, 12, 6);
        }
    }

    private static void Roof(Frame f, Section s, int lod)
    {
        float half = s.width * .5f + .28f, depth = s.depth * .5f + .3f, h = s.Height;
        foreach (int sign in new[] { -1, 1 })
        {
            float a = sign < 0 ? -half : 4.9f, b = sign < 0 ? -4.9f : half;
            foreach (int side in new[] { -1, 1 })
            {
                f.Roof(new Vector3(a, h + .1f, side * depth), new Vector3(b, h + .1f, side * depth), new Vector3(b, h + .9f, 0), new Vector3(a, h + .9f, 0));
                f.Roof(new Vector3(0, h + 1.57f, side * (depth + 1.35f)), new Vector3(sign * 5.03f, h + .16f, side * (depth + 1.35f)),
                    new Vector3(sign * 5.03f, h + .9f, 0), new Vector3(0, h + 2.3f, 0));
            }
            f.Face(red, Vector3.right * sign, new Vector3(sign * half, h, -depth), new Vector3(sign * half, h, depth), new Vector3(sign * half, h + .9f, 0));
            f.Box(red, new Vector3(sign * 7.4f, h + 1.35f, sign * 1.4f), new Vector3(.9f, 1.25f, 1.2f));
            f.Box(roofEdge, new Vector3(sign * 7.4f, h + 2.03f, sign * 1.4f), new Vector3(1.1f, .12f, 1.4f));
        }
    }

    private static void SmallBuilding(Frame f, Section s, Profile profile, int lod)
    {
        bool villa = profile.style.StartsWith("villa", StringComparison.Ordinal);
        Material wall = profile.style == "villa-cream" ? cream : red;
        float h = .65f + s.floors * 3.0f, w = s.width, d = s.depth;
        f.Box(dark, Vector3.up * h * .5f, new Vector3(w - .3f, h - .2f, d - .3f));
        foreach (int sign in new[] { -1, 1 })
        {
            SimpleElevation(f.At(new Vector3(0, 0, sign * d * .5f), sign > 0 ? 0 : 180), w, h, s.floors, wall, profile, sign > 0, lod);
            SimpleElevation(f.At(new Vector3(sign * w * .5f, 0, 0), sign > 0 ? 90 : -90), d, h, s.floors, wall, profile, false, lod);
        }
        HipRoof(f, w + .65f, d + .65f, h + .15f, villa ? 2.0f : .85f, lod, greyRoof);
        for (int i = -1; i <= 1; i++)
        {
            f.Box(red, new Vector3(i * w * .26f, h + 1.5f, 0), new Vector3(.7f, 1.3f, .9f));
            f.Box(roofEdge, new Vector3(i * w * .26f, h + 2.2f, 0), new Vector3(.85f, .12f, 1.05f));
        }
        if (profile.style == "office-stepped")
        {
            var front = f.At(new Vector3(0, 0, d * .5f + .28f));
            var openings = new List<Rect>();
            EntranceDoor(front, openings, 0, .06f, 2.0f, 2.20f, .36f, lod, true, .56f);
            ArchedWindow(front, openings, 0, h - 1.3f, 1.2f, 1.05f, .36f, red, lod, false);
            Wall(front, red, concrete, -2.7f, 2.7f, h + 1, .36f, openings, false, .4f);
            for (int i = 0; i < 3; i++) front.Box(red, new Vector3(0, h + .55f + i * .48f, 0), new Vector3(4.2f - i * 1.15f, .65f, .65f));
        }
        if (!villa) return;
        HipRoof(f, w + 3.6f, d + 3.6f, 2.9f, .85f, lod, greyRoof);
        if (lod < 2)
        {
            foreach (var edge in new[] { (new Vector3(0, 0, d * .5f + 1.45f), 0f, w),
                (new Vector3(0, 0, -d * .5f - 1.45f), 180f, w),
                (new Vector3(w * .5f + 1.45f, 0, 0), 90f, d), (new Vector3(-w * .5f - 1.45f, 0, 0), -90f, d) })
            {
                Frame porch = f.At(edge.Item1, edge.Item2);
                for (float x = -edge.Item3 * .5f - 1.4f; x <= edge.Item3 * .5f + 1.4f; x += 2.4f)
                {
                    porch.Box(trim, new Vector3(x, 1.45f, 0), new Vector3(.14f, 2.9f, .14f));
                    porch.Box(wall, new Vector3(x + 1.05f, .55f, 0), new Vector3(2.1f, 1.1f, .15f));
                }
            }
            Fence(f, w + 8, d + 8, lod);
        }
    }

    private static void SimpleElevation(Frame f, float width, float height, int floors, Material material, Profile profile, bool front, int lod)
    {
        var holes = new List<Rect>(); int columns = Math.Max(2, Mathf.RoundToInt((width - 2) / 3.2f));
        bool entrance = front && profile.style != "office-stepped";
        var doorOpening = new Rect(-.6f, .04f, 1.2f, 2.26f);
        for (int floor = 0; floor < floors; floor++) for (int i = 0; i < columns; i++)
        {
            float x = (i + .5f) * width / columns - width * .5f;
            if (front && Mathf.Abs(x) < 2.7f && profile.style == "office-stepped") continue;
            var windowOpening = new Rect(x - .8f, 1.8f + floor * 3 - .825f, 1.6f, 1.65f);
            if (entrance && floor == 0 && windowOpening.Overlaps(doorOpening)) continue;
            Window(f, holes, x, 1.8f + floor * 3.0f, 1.6f, 1.65f, 0, lod, 2, i + floor, true, material);
            if (lod < 2) foreach (int sign in new[] { -1, 1 }) f.Box(material == red ? cream : red, new Vector3(x + sign * .93f, 1.8f + floor * 3, .04f), new Vector3(.18f, 1.96f, .12f));
        }
        if (entrance) EntranceDoor(f, holes, 0, .04f, 1.2f, 2.14f, 0, lod, false);
        Wall(f, material, concrete, -width * .5f, width * .5f, height, 0, holes, false, .4f);
        f.Box(trim, new Vector3(0, height, .1f), new Vector3(width + .2f, .17f, .3f));
    }

    private static void HipRoof(Frame f, float width, float depth, float y, float rise, int lod, Material material)
    {
        float x = width * .5f, z = depth * .5f, ridge = Mathf.Max(.1f, x - z * .65f);
        Vector3 a = new Vector3(-x, y, -z), b = new Vector3(x, y, -z), c = new Vector3(x, y, z), d = new Vector3(-x, y, z);
        Vector3 left = new Vector3(-ridge, y + rise, 0), right = new Vector3(ridge, y + rise, 0);
        f.Roof(material, a, b, right, left); f.Roof(material, d, c, right, left); f.Roof(material, a, d, left); f.Roof(material, b, c, right);
        if (lod == 0) foreach (int sign in new[] { -1, 1 })
            for (float u = -ridge; u < ridge; u += .6f) f.Rod(roofEdge, new Vector3(u, y + .02f, sign * z), new Vector3(u, y + rise + .02f, 0), .012f, 4);
    }

    private static void Fence(Frame f, float width, float depth, int lod)
    {
        foreach (var edge in new[] { (new Vector3(0, 0, depth * .5f), 0f, width), (new Vector3(0, 0, -depth * .5f), 180f, width),
            (new Vector3(width * .5f, 0, 0), 90f, depth), (new Vector3(-width * .5f, 0, 0), -90f, depth) })
        {
            var front = f.At(edge.Item1, edge.Item2);
            for (float x = -edge.Item3 * .5f; x < edge.Item3 * .5f; x += 2.8f)
            {
                float length = Mathf.Min(2.8f, edge.Item3 * .5f - x);
                if (edge.Item2 == 0 && Mathf.Abs(x + length * .5f) < 2) continue;
                front.Box(trim, new Vector3(x + length * .5f, .85f, 0), new Vector3(length - .2f, 1.7f, .07f));
                front.Box(red, new Vector3(x, .95f, 0), new Vector3(.3f, 1.9f, .3f));
                if (lod == 0) for (float rib = .1f; rib < length - .1f; rib += .16f) front.Box(trim, new Vector3(x + rib, .85f, .045f), new Vector3(.045f, 1.7f, .04f));
            }
        }
    }
}
