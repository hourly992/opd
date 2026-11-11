using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// The route remains OSM-based. Cross sections and surfaces are separated by observed street segment.
internal static class StreetRoadBuilder
{
    [Serializable] private sealed class Profile
    {
        public int panoramaYear;
        public Section[] sections;
        public StreetBuilder.Point[] crossings, manholes;
    }
    [Serializable] private sealed class Section
    {
        public float from, to, halfWidth, leftWidth, leftSetback, rightWidth, rightSetback, leftApron, rightApron;
        public string leftSurface, rightSurface;
    }
    private struct Gap { internal float distance, halfWidth; internal int sides; internal bool crossing; }
    private static Profile profile;
    private static Vector3[] route;
    private static float[] chainage;
    private static readonly List<Gap> gaps = new List<Gap>();
    private static Material asphalt, serviceAsphalt, footpathAsphalt, pavement, kerb, marking, blue, verge;
    private static float Length => chainage[chainage.Length - 1];
    internal static IEnumerable<Vector3> Manholes => profile.manholes.Select(p => p.V);
    internal static float RoadHeight(Vector3 p)
    {
        Nearest(p, out float d, out float across);
        return Mathf.Lerp(.082f, .035f, Mathf.Clamp01(Mathf.Abs(across) / At(d).halfWidth));
    }

    internal static void Initialize(StreetBuilder.Data data)
    {
        EnvironmentMesh.EnsureFolders();
        profile = JsonUtility.FromJson<Profile>(File.ReadAllText("Assets/StreetSource/road_profiles.json"));
        if (profile.sections == null || profile.sections.Length == 0) throw new InvalidOperationException("Missing road cross sections");
        route = data.roads.First(r => r.main).points.Select(p => p.V).ToArray();
        chainage = new float[route.Length];
        for (int i = 1; i < route.Length; i++) chainage[i] = chainage[i - 1] + Vector3.Distance(route[i], route[i - 1]);
        gaps.Clear();
        // Only actual graph connections and mapped pedestrian crossings lower or interrupt a kerb.
        foreach (var road in data.roads.Where(r => !r.main && r.kind != "footway" && r.kind != "path" && r.kind != "pedestrian"))
            for (int i = 0; i < road.points.Length; i++)
            {
                Vector3 point = road.points[i].V;
                Nearest(point, out float d, out float across);
                if (Mathf.Abs(across) >= 1.2f || Vector3.Distance(point, Position(d)) > 1.2f) continue;
                foreach (int step in new[] { -1, 1 })
                    for (int j = i + step; j >= 0 && j < road.points.Length; j += step)
                    {
                        Nearest(road.points[j].V, out _, out float lateral);
                        if (Mathf.Abs(lateral) < 1.2f) continue;
                        Frame(d, out _, out var right);
                        Vector3 direction = (road.points[j].V - point).normalized;
                        float sine = Mathf.Max(.35f, Mathf.Abs(Vector3.Dot(direction, right.normalized)));
                        AddGap(d, road.width * .5f / sine + .75f, lateral < 0 ? 1 : 2, false);
                        break;
                    }
            }
        foreach (var crossing in profile.crossings)
        {
            Nearest(crossing.V, out float d, out _);
            AddGap(d, 1.8f, 3, true);
        }
        asphalt = EnvironmentMesh.Material("ReferenceAsphalt", Color.white);
        StreetSurfaceLibrary.ApplyNamed(asphalt, "MainRoadAsphalt");
        serviceAsphalt = EnvironmentMesh.Material("ReferenceServiceAsphalt", Color.white);
        StreetSurfaceLibrary.ApplyNamed(serviceAsphalt, "Asphalt");
        footpathAsphalt = EnvironmentMesh.Material("ReferenceFootpathAsphalt", Color.white);
        StreetSurfaceLibrary.ApplyNamed(footpathAsphalt, "FootpathAsphalt");
        pavement = EnvironmentMesh.Material("ReferenceRedPaving", Color.white);
        StreetSurfaceLibrary.ApplyNamed(pavement, "Pavement");
        kerb = EnvironmentMesh.Material("ReferenceKerbstone", Color.white);
        StreetSurfaceLibrary.ApplyNamed(kerb, "RoadKerb");
        marking = EnvironmentMesh.Material("RoadPaint", new Color(.69f, .68f, .61f));
        blue = EnvironmentMesh.Material("VergeBlueEdge", new Color(.065f, .22f, .34f));
        verge = EnvironmentMesh.Material("RoadVergeSurface", Color.white);
        StreetSurfaceLibrary.ApplyNamed(verge, "Grass");
    }

    private static void AddGap(float distance, float halfWidth, int sides, bool crossing)
    {
        int existing = gaps.FindIndex(g => Mathf.Abs(g.distance - distance) < .5f && g.crossing == crossing && g.sides == sides);
        if (existing >= 0)
        {
            var gap = gaps[existing]; gap.halfWidth = Mathf.Max(gap.halfWidth, halfWidth); gaps[existing] = gap;
        }
        else gaps.Add(new Gap { distance = distance, halfWidth = halfWidth, sides = sides, crossing = crossing });
    }

    private static Section At(float d) => profile.sections.FirstOrDefault(s => d >= s.from && d < s.to) ?? profile.sections.Last();
    // Observed widths remain unchanged away from their approximate boundaries. Tapers remove the
    // former right-angle jumps in the same sidewalk and 10-metre-wide apron end faces.
    private static Section GeometryAt(float d)
    {
        for (int i = 1; i < profile.sections.Length; i++)
        {
            float boundary = profile.sections[i].from;
            const float taper = 8;
            if (Mathf.Abs(d - boundary) >= taper) continue;
            var a = profile.sections[i - 1]; var b = profile.sections[i];
            float t = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(boundary - taper, boundary + taper, d));
            return new Section { halfWidth = Mathf.Lerp(a.halfWidth, b.halfWidth, t),
                leftWidth = Mathf.Lerp(a.leftWidth, b.leftWidth, t), rightWidth = Mathf.Lerp(a.rightWidth, b.rightWidth, t),
                leftSetback = Mathf.Lerp(a.leftSetback, b.leftSetback, t), rightSetback = Mathf.Lerp(a.rightSetback, b.rightSetback, t),
                leftApron = Mathf.Lerp(a.leftApron, b.leftApron, t), rightApron = Mathf.Lerp(a.rightApron, b.rightApron, t) };
        }
        return At(d);
    }
    private static Vector3 Position(float d)
    {
        d = Mathf.Clamp(d, 0, Length);
        int i = 1; while (i < chainage.Length - 1 && d > chainage[i]) i++;
        return Vector3.Lerp(route[i - 1], route[i], (d - chainage[i - 1]) / Mathf.Max(.001f, chainage[i] - chainage[i - 1]));
    }
    private static void Frame(float d, out Vector3 p, out Vector3 right)
    {
        p = Position(d);
        int segment = 1;
        while (segment < chainage.Length - 1 && d > chainage[segment]) segment++;
        Vector3 Offset(int node)
        {
            Vector3 before = (route[node] - route[Mathf.Max(0, node - 1)]).normalized;
            Vector3 after = (route[Mathf.Min(route.Length - 1, node + 1)] - route[node]).normalized;
            if (node == 0) before = after;
            if (node == route.Length - 1) after = before;
            Vector3 n1 = new Vector3(before.z, 0, -before.x), n2 = new Vector3(after.z, 0, -after.x);
            Vector3 bisector = (n1 + n2).normalized;
            return bisector / Mathf.Max(.55f, Vector3.Dot(bisector, n2));
        }
        float t = Mathf.InverseLerp(chainage[segment - 1], chainage[segment], d);
        right = Vector3.Lerp(Offset(segment - 1), Offset(segment), t);
    }
    private static void Nearest(Vector3 p, out float distance, out float across)
    {
        float best = float.MaxValue; distance = across = 0;
        for (int i = 1; i < route.Length; i++)
        {
            Vector3 delta = route[i] - route[i - 1];
            float t = Mathf.Clamp01(Vector3.Dot(p - route[i - 1], delta) / delta.sqrMagnitude);
            Vector3 nearest = route[i - 1] + delta * t;
            float length = (p - nearest).sqrMagnitude;
            if (length >= best) continue;
            best = length; distance = Mathf.Lerp(chainage[i - 1], chainage[i], t);
            across = Vector3.Dot(p - nearest, new Vector3(delta.z, 0, -delta.x).normalized);
        }
    }
    internal static bool IsPaved(Vector3 p)
    {
        if (route == null) return false;
        Nearest(p, out float d, out float across);
        // Endpoints use a clamped nearest point; reject their longitudinal extensions as well.
        // Otherwise an unrelated footpath beyond the street can be clipped by a phantom road strip.
        if (d < .001f && Vector3.Dot(p - route[0], route[1] - route[0]) < 0) return false;
        if (d > Length - .001f && Vector3.Dot(p - route.Last(), route.Last() - route[route.Length - 2]) > 0) return false;
        if ((p - Position(d)).sqrMagnitude > 45 * 45) return false;
        var s = GeometryAt(d); float a = Mathf.Abs(across);
        if (a < s.halfWidth + .2f) return true;
        float setback = across < 0 ? s.leftSetback : s.rightSetback;
        float width = across < 0 ? s.leftWidth : s.rightWidth;
        float apron = across < 0 ? s.leftApron : s.rightApron;
        if (PavedConnection(d, across < 0 ? -1 : 1) && a < s.halfWidth + setback + width + .2f) return true;
        return a > s.halfWidth + setback - .15f && a < Mathf.Max(s.halfWidth + setback + width, apron) + .2f;
    }
    private static float WalkHeight(float d, int side)
    {
        float height = .18f;
        foreach (var gap in gaps)
            if ((gap.sides & (side < 0 ? 1 : 2)) != 0)
                // Vehicle entries meet the .035 road with a 10-mm low lip. A pedestrian
                // crossing keeps the observed 20-mm kerb; it is not a second driveway.
                height = Mathf.Min(height, Mathf.Lerp(gap.crossing ? .055f : .045f, .18f,
                    Mathf.SmoothStep(0, 1, Mathf.Clamp01((Mathf.Abs(d - gap.distance) - gap.halfWidth) / 1.6f))));
        return height;
    }

    private static bool Driveway(float d, int side) => gaps.Any(g => !g.crossing
        && (g.sides & (side < 0 ? 1 : 2)) != 0 && Mathf.Abs(d - g.distance) < g.halfWidth);

    private static bool PavedConnection(float d, int side) => gaps.Any(g =>
        (g.sides & (side < 0 ? 1 : 2)) != 0 && Mathf.Abs(d - g.distance) <= g.halfWidth);

    private static float OuterHeight(float d, int side, float width) => WalkHeight(d, side)
        + Mathf.Min(.035f, width * .012f) * Mathf.InverseLerp(.055f, .18f, WalkHeight(d, side));

    private static float ApronInnerHeight(float d, int side) => Mathf.Lerp(.045f, .041f,
        Mathf.InverseLerp(.045f, .055f, WalkHeight(d, side)));

    private static float KerbBevel(float top) => Mathf.Min(.012f, top * .2f);

    private static Vector3 WalkProbe(float d, int side)
    {
        d = Mathf.Clamp(d, 0, Length);
        var section = GeometryAt(d); Frame(d, out var p, out var right);
        float setback = side < 0 ? section.leftSetback : section.rightSetback;
        float width = side < 0 ? section.leftWidth : section.rightWidth;
        float inner = Mathf.Max(section.halfWidth + .16f, section.halfWidth + setback + .05f);
        float outer = section.halfWidth + setback + width;
        float height = (WalkHeight(d, side) + OuterHeight(d, side, width)) * .5f;
        return p + right * (side * (inner + outer) * .5f) + Vector3.up * height;
    }

    // Shared station frames keep the kerb, paving and asphalt in the same plan geometry at bends.
    // The recessed joints contain concrete/mortar, rather than looking down through an open
    // 16-mm slot to the world plane. Very short adaptive sections must also retain their kerb.
    private static void KerbSegment(EnvironmentMesh mesh, Material material, float a, float b, int side,
        float offsetA, float offsetB, float width, float topA, float topB)
    {
        if (b - a <= .0001f) return;
        float inset = Mathf.Min(.003f, (b - a) * .12f);
        Vector3[] Ring(Vector3 origin, Vector3 right, float offset, float top)
        {
            float bevel = KerbBevel(top);
            return new[] { origin + right * (side * offset) - Vector3.up * .035f,
                origin + right * (side * offset) + Vector3.up * (top - bevel),
                origin + right * (side * (offset + bevel)) + Vector3.up * top,
                origin + right * (side * (offset + width - bevel)) + Vector3.up * top,
                origin + right * (side * (offset + width)) + Vector3.up * (top - bevel),
                origin + right * (side * (offset + width)) - Vector3.up * .035f };
        }
        void Part(float from, float to, float recess)
        {
            if (to - from <= .00001f) return;
            Frame(from, out var p, out var r); Frame(to, out var q, out var s);
            float ta = Mathf.InverseLerp(a, b, from), tb = Mathf.InverseLerp(a, b, to);
            var start = Ring(p, r, Mathf.Lerp(offsetA, offsetB, ta), Mathf.Lerp(topA, topB, ta) - recess);
            var end = Ring(q, s, Mathf.Lerp(offsetA, offsetB, tb), Mathf.Lerp(topA, topB, tb) - recess);
            Vector3 forward = (q - p).normalized;
            mesh.Face(material, -forward, start); mesh.Face(material, forward, end);
            for (int i = 0; i < start.Length; i++)
            {
                int j = (i + 1) % start.Length;
                Vector3 normal = Vector3.Cross(end[i] - start[i], start[j] - start[i]).normalized * side;
                mesh.Face(material, normal, start[i], end[i], end[j], start[j]);
            }
        }
        Part(a, a + inset, .006f);
        Part(a + inset, b - inset, 0);
        Part(b - inset, b, .006f);
    }

    internal static void Generate(Transform parent)
    {
        var root = new GameObject("Дороги и тротуары · профили по панорамам 2025");
        root.transform.SetParent(parent, false);
        var road = new EnvironmentMesh(); var walking = new EnvironmentMesh(); var edging = new EnvironmentMesh();
        // Include profile boundaries and original route nodes, then subdivide: no 90-degree strip overlaps.
        var distances = new SortedSet<float>(chainage);
        for (float d = 0; d < Length; d += 1) distances.Add(d);
        foreach (var s in profile.sections) { distances.Add(Mathf.Clamp(s.from, 0, Length)); distances.Add(Mathf.Clamp(s.to, 0, Length)); }
        foreach (var gap in gaps)
            foreach (float offset in new[] { -gap.halfWidth - 1.6f, -gap.halfWidth, gap.halfWidth, gap.halfWidth + 1.6f })
                distances.Add(Mathf.Clamp(gap.distance + offset, 0, Length));
        var ds = distances.ToArray();
        for (int i = 1; i < ds.Length; i++)
        {
            float a = ds[i - 1], b = ds[i], middle = (a + b) * .5f;
            var s = At(middle); var sa = GeometryAt(a); var sb = GeometryAt(b);
            Frame(a, out var pa, out var ra); Frame(b, out var pb, out var rb);
            void CloseEdge(EnvironmentMesh mesh, Material material, float offsetA, float offsetB,
                float heightA, float heightB, int normalSide)
            {
                Vector3 topA = pa + ra * offsetA + Vector3.up * heightA;
                Vector3 topB = pb + rb * offsetB + Vector3.up * heightB;
                mesh.Face(material, (ra + rb).normalized * normalSide, topA, topB,
                    pb + rb * offsetB - Vector3.up * .035f, pa + ra * offsetA - Vector3.up * .035f);
            }
            void Ribbon(EnvironmentMesh mesh, Material material, float fromA, float toA, float fromB, float toB,
                float innerA, float outerA, float innerB, float outerB)
            {
                mesh.Face(material, Vector3.up, pa + ra * fromA + Vector3.up * innerA, pa + ra * toA + Vector3.up * outerA,
                    pb + rb * toB + Vector3.up * outerB, pb + rb * fromB + Vector3.up * innerB);
                // The existing scene is editable: its strip ends must have real faces,
                // rather than an exposed top sheet above the grass/world plane.
                if (i == 1)
                    mesh.Face(material, (pa - pb).normalized, pa + ra * fromA + Vector3.up * innerA,
                        pa + ra * toA + Vector3.up * outerA, pa + ra * toA - Vector3.up * .035f,
                        pa + ra * fromA - Vector3.up * .035f);
                if (i == ds.Length - 1)
                    mesh.Face(material, (pb - pa).normalized, pb + rb * fromB + Vector3.up * innerB,
                        pb + rb * toB + Vector3.up * outerB, pb + rb * toB - Vector3.up * .035f,
                        pb + rb * fromB - Vector3.up * .035f);
            }
            // A gentle, continuous road crown drains towards the two actual road edges.
            foreach (int side in new[] { -1, 1 })
            {
                road.Face(asphalt, (Vector3.up + ra * (side * .013f)).normalized,
                    pa + Vector3.up * .082f, pa + ra * (side * sa.halfWidth) + Vector3.up * .035f,
                    pb + rb * (side * sb.halfWidth) + Vector3.up * .035f, pb + Vector3.up * .082f);
                if (i == 1)
                    road.Face(asphalt, (pa - pb).normalized, pa + Vector3.up * .082f,
                        pa + ra * (side * sa.halfWidth) + Vector3.up * .035f,
                        pa + ra * (side * sa.halfWidth) - Vector3.up * .035f, pa - Vector3.up * .035f);
                if (i == ds.Length - 1)
                    road.Face(asphalt, (pb - pa).normalized, pb + Vector3.up * .082f,
                        pb + rb * (side * sb.halfWidth) + Vector3.up * .035f,
                        pb + rb * (side * sb.halfWidth) - Vector3.up * .035f, pb - Vector3.up * .035f);
            }
            foreach (int side in new[] { -1, 1 })
            {
                float setbackA = side < 0 ? sa.leftSetback : sa.rightSetback;
                float setbackB = side < 0 ? sb.leftSetback : sb.rightSetback;
                float widthA = side < 0 ? sa.leftWidth : sa.rightWidth;
                float widthB = side < 0 ? sb.leftWidth : sb.rightWidth;
                float startA = sa.halfWidth + setbackA, endA = startA + widthA;
                float startB = sb.halfWidth + setbackB, endB = startB + widthB;
                string surface = side < 0 ? s.leftSurface : s.rightSurface;
                float ha = WalkHeight(a, side), hb = WalkHeight(b, side);
                float outerA = OuterHeight(a, side, widthA), outerB = OuterHeight(b, side, widthB);
                // Paving meets the TOP of the back bevel, not its lower outside corner.
                // The previous +.16/+.05 values left a 12-mm open edge above each bevel.
                float innerA = Mathf.Max(sa.halfWidth + .16f, startA + .05f) - KerbBevel(ha);
                float innerB = Mathf.Max(sb.halfWidth + .16f, startB + .05f) - KerbBevel(hb);
                float apronA = side < 0 ? sa.leftApron : sa.rightApron;
                float apronB = side < 0 ? sb.leftApron : sb.rightApron;
                bool hasApron = Mathf.Max(apronA - endA, apronB - endB) > .05f;
                // Leave the outer kerb's top visible instead of laying the paving over it.
                float pavingEndA = endA - (hasApron ? .05f - KerbBevel(outerA) : 0);
                float pavingEndB = endB - (hasApron ? .05f - KerbBevel(outerB) : 0);
                Material finish = Driveway(middle, side) ? serviceAsphalt : surface == "red" ? pavement : footpathAsphalt;
                Ribbon(walking, finish, side * innerA, side * pavingEndA,
                    side * innerB, side * pavingEndB, ha, outerA, hb, outerB);
                CloseEdge(walking, finish, side * innerA, side * innerB, ha, hb, -side);
                CloseEdge(walking, finish, side * pavingEndA, side * pavingEndB, outerA, outerB, side);
                if (hasApron)
                {
                    float apronStartA = ApronInnerHeight(a, side), apronStartB = ApronInnerHeight(b, side);
                    float apronEndA = apronStartA - .013f * Mathf.Clamp01(apronA - endA - .05f);
                    float apronEndB = apronStartB - .013f * Mathf.Clamp01(apronB - endB - .05f);
                    float outerOffsetA = Mathf.Max(endA + .051f, apronA), outerOffsetB = Mathf.Max(endB + .051f, apronB);
                    Ribbon(road, serviceAsphalt, side * (endA + .05f), side * Mathf.Max(endA + .051f, apronA),
                        side * (endB + .05f), side * Mathf.Max(endB + .051f, apronB), apronStartA,
                        apronEndA, apronStartB, apronEndB);
                    CloseEdge(road, serviceAsphalt, side * outerOffsetA, side * outerOffsetB, apronEndA, apronEndB, side);
                    KerbSegment(edging, kerb, a, b, side, endA - .05f, endB - .05f, .10f, outerA, outerB);
                }
                else
                {
                    // Backfill joins the paving to the existing ground instead of a floating 18-cm edge.
                    Ribbon(walking, verge, side * endA, side * (endA + .55f), side * endB, side * (endB + .55f),
                        outerA - .012f, .001f, outerB - .012f, .001f);
                    CloseEdge(walking, verge, side * (endA + .55f), side * (endB + .55f), .001f, .001f, side);
                }
                KerbSegment(edging, kerb, a, b, side, sa.halfWidth, sb.halfWidth, .16f, ha, hb);
                // Only the grassy strips explicitly present in the observed profiles are raised/backfilled.
                if (Mathf.Min(setbackA, setbackB) > .21f)
                {
                    if (PavedConnection(middle, side))
                        // A mapped driveway/crossing cuts through its verge as well as its
                        // sidewalk. Keeping the old grass ribbon here left a planted barrier
                        // across the vehicle entry even though both kerbs were lowered.
                        Ribbon(walking, finish, side * (sa.halfWidth + .16f - KerbBevel(ha)),
                            side * (startA - .05f + KerbBevel(ha)),
                            side * (sb.halfWidth + .16f - KerbBevel(hb)), side * (startB - .05f + KerbBevel(hb)), ha, ha, hb, hb);
                    else
                        Ribbon(walking, verge, side * (sa.halfWidth + .16f), side * (startA - .05f),
                            side * (sb.halfWidth + .16f), side * (startB - .05f), ha - .015f, ha - .012f, hb - .015f, hb - .012f);
                    KerbSegment(edging, side < 0 ? blue : kerb, a, b, side, startA - .05f, startB - .05f, .10f, ha, hb);
                }
                else if (Mathf.Max(setbackA, setbackB) > .001f)
                    // A verge narrower than the two kerbs is closed with the same paving, not a slit
                    // down to the world plane during its short geometric taper.
                    Ribbon(walking, finish, side * (sa.halfWidth + .16f - KerbBevel(ha)), side * Mathf.Max(sa.halfWidth + .161f - KerbBevel(ha), innerA),
                        side * (sb.halfWidth + .16f - KerbBevel(hb)), side * Mathf.Max(sb.halfWidth + .161f - KerbBevel(hb), innerB), ha, ha, hb, hb);
            }
        }
        foreach (var crossing in profile.crossings)
        {
            Nearest(crossing.V, out float d, out _);
            Frame(d, out var p, out var right);
            Vector3 forward = new Vector3(-right.z, 0, right.x);
            // Crossing coordinates come from named OSM nodes, rather than regularly spaced decoration.
            for (float across = -3.05f; across < 3.05f; across += .9f)
                walking.Box(marking, p + right * across + Vector3.up * (RoadHeight(p + right * across) + .005f),
                    new Vector3(.44f, .006f, 3.0f), Quaternion.LookRotation(forward));
        }
        var colliders = new List<MeshCollider>();
        foreach (var item in new[] { (road, "ReferenceRoad", "Асфальт, парковочные уширения и корона дороги"),
            (walking, "ReferenceWalkways", "Красная брусчатка, асфальтовые тротуары и понижения"),
            (edging, "ReferenceKerbs", "Составные бордюры и подпорная кромка газона") })
        {
            var part = item.Item1.Create(item.Item2, item.Item3);
            part.transform.SetParent(root.transform, false);
            var collider = part.AddComponent<MeshCollider>(); collider.sharedMesh = part.GetComponent<MeshFilter>().sharedMesh;
            colliders.Add(collider);
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic);
        }
        var network = root.AddComponent<StreetRoadNetwork>();
        network.streetLength = Length; network.profileSections = profile.sections.Length;
        network.panoramaYear = profile.panoramaYear; network.mappedCrossings = profile.crossings.Length;
        network.surfaces = colliders.ToArray();
        var checks = new List<Vector3>();
        for (float d = 20; d < Length - 10; d += 20)
        {
            var s = GeometryAt(d); Frame(d, out var p, out var right);
            checks.Add(p - right * (s.halfWidth + s.leftSetback + s.leftWidth * .5f));
            checks.Add(p + right * (s.halfWidth + s.rightSetback + s.rightWidth * .5f));
        }
        network.walkingChecks = checks.ToArray();
        var transitions = new List<Vector3>();
        foreach (var section in profile.sections.Skip(1))
            foreach (float offset in new[] { -8.05f, -7.95f, -.05f, .05f, 7.95f, 8.05f })
                foreach (int side in new[] { -1, 1 }) transitions.Add(WalkProbe(section.from + offset, side));
        network.transitionProbePoints = transitions.ToArray();
        var junctions = new List<Vector3>();
        foreach (var gap in gaps.Where(g => !g.crossing && g.sides != 3
            && !gaps.Any(other => !other.crossing && Mathf.Abs(other.distance - g.distance) < 2 && other.sides != g.sides)))
            foreach (float offset in new[] { -gap.halfWidth * .5f, gap.halfWidth * .5f })
                foreach (int side in new[] { -1, 1 }) junctions.Add(WalkProbe(gap.distance + offset, side));
        network.junctionProbePoints = junctions.ToArray();
        Physics.SyncTransforms();
        Debug.Log("REFERENCE_ROADS length=" + Length + " profiles=" + profile.sections.Length + " crossings=" + profile.crossings.Length
            + " sidedConnections=" + gaps.Count(g => !g.crossing) + " bevelledKerbs=True taperedProfiles=True");
    }
}
