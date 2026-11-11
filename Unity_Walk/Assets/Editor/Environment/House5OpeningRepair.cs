using System;
using System.Collections.Generic;
using UnityEngine;

// Repairs orientation in the existing photo-based house, without moving a window, door or wall.
// House coordinates below come from house_full/build_full_house.py, not an approximate mesh centre.
internal static class House5OpeningRepair
{
    internal struct Report
    {
        public int closedComponents, rejectedComponents, degenerateTriangles;
        public int topologyFlips, wallFlips, revealFlips, flippedTriangles, splitVertices, protectedPlaneConflicts;
    }
    private struct EdgeUse { internal int triangle; internal bool direction; }
    private struct Triangle2 { internal Vector2 a, b, c; }
    private sealed class Wall
    {
        internal int axis;
        internal float front, sign, thickness, minU, maxU, height;
        internal readonly List<Triangle2> mask = new List<Triangle2>();
        internal float Coordinate(Vector3 p) => axis == 0 ? p.x : p.z;
        internal Vector2 Project(Vector3 p) => new Vector2(axis == 0 ? p.z : p.x, p.y);
        internal float Back => front - sign * thickness;
        private bool Contains(Vector3 p)
        {
            Vector2 uv = Project(p);
            return uv.x >= minU - .001f && uv.x <= maxU + .001f && uv.y >= -.001f && uv.y <= height + .001f;
        }
        internal bool Bounds(Vector3 a, Vector3 b, Vector3 c) => Contains(a) && Contains(b) && Contains(c);
        internal bool On(float value, Vector3 a, Vector3 b, Vector3 c) => Mathf.Abs(Coordinate(a) - value) < .0003f
            && Mathf.Abs(Coordinate(b) - value) < .0003f && Mathf.Abs(Coordinate(c) - value) < .0003f;
        internal bool Solid(Vector3 p)
        {
            Vector2 uv = Project(p);
            foreach (var t in mask)
            {
                float a = Cross(t.b - t.a, uv - t.a), b = Cross(t.c - t.b, uv - t.b), c = Cross(t.a - t.c, uv - t.c);
                if (a >= -1e-7f && b >= -1e-7f && c >= -1e-7f || a <= 1e-7f && b <= 1e-7f && c <= 1e-7f) return true;
            }
            return false;
        }
    }

    internal static Report Repair(string materialName, ref Vector3[] vertices, ref Vector3[] normals, ref int[] indices)
    {
        var report = new Report();
        bool masonry = materialName.Contains("Кирпич") || materialName.Contains("Штукатурка цоколя");
        bool closedDetail = materialName.Contains("Рамы") || materialName.Contains("Светлые карнизы")
            || materialName.Contains("Входная дверь") || materialName.Contains("Стекло");
        if (!masonry && !closedDetail) return report;
        if (normals.Length != vertices.Length || indices.Length % 3 != 0) throw new InvalidOperationException("Invalid house opening mesh: " + materialName);
        foreach (int index in indices) if (index < 0 || index >= vertices.Length) throw new InvalidOperationException("Invalid house triangle: " + materialName);
        foreach (var p in vertices) if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) throw new InvalidOperationException("Non-finite house vertex: " + materialName);
        var flipped = new bool[indices.Length / 3];
        var closedTriangles = new bool[flipped.Length];
        OrientClosedComponents(vertices, indices, flipped, closedTriangles, ref report);
        if (masonry) OrientWallsAndReveals(vertices, indices, flipped, closedTriangles, ref report);
        // Exported hard/smooth normal splits are preserved. If two repaired faces formerly shared
        // a render vertex with an unrepaired face, duplicate that vertex instead of averaging opposite normals.
        var usage = new byte[vertices.Length];
        for (int t = 0; t < flipped.Length; t++)
        {
            if (flipped[t]) report.flippedTriangles++;
            for (int k = 0; k < 3; k++) usage[indices[t * 3 + k]] |= (byte)(flipped[t] ? 2 : 1);
        }
        if (report.flippedTriangles > 0)
        {
            var positions = new List<Vector3>(vertices); var corrected = new List<Vector3>(normals);
            var split = new Dictionary<int, int>();
            for (int i = 0; i < usage.Length; i++)
                if (usage[i] == 2) corrected[i] = -normals[i];
                else if (usage[i] == 3)
                {
                    split.Add(i, positions.Count); positions.Add(vertices[i]); corrected.Add(-normals[i]); report.splitVertices++;
                }
            for (int t = 0; t < flipped.Length; t++)
                if (flipped[t]) for (int k = 0; k < 3; k++)
                {
                    int index = t * 3 + k;
                    if (split.TryGetValue(indices[index], out int replacement)) indices[index] = replacement;
                }
            vertices = positions.ToArray(); normals = corrected.ToArray();
        }
        Debug.Log("HOUSE5_OPENING_REPAIR material=" + materialName + " closed=" + report.closedComponents
            + " rejectedOpenOrNonmanifold=" + report.rejectedComponents + " topologyFlips=" + report.topologyFlips
            + " wallFlips=" + report.wallFlips + " revealFlips=" + report.revealFlips
            + " netFlipped=" + report.flippedTriangles + " splitVertices=" + report.splitVertices
            + " protectedPlaneConflicts=" + report.protectedPlaneConflicts + " movedVertices=0");
        return report;
    }

    private static void OrientClosedComponents(Vector3[] vertices, int[] indices, bool[] flipped, bool[] closedTriangles, ref Report report)
    {
        var weld = new Dictionary<(long, long, long), int>(); var welded = new int[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var p = vertices[i]; var key = ((long)Math.Round(p.x * 100000.0), (long)Math.Round(p.y * 100000.0), (long)Math.Round(p.z * 100000.0));
            if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld.Add(key, id); }
            welded[i] = id;
        }
        var edges = new Dictionary<(int, int), List<EdgeUse>>();
        int triangles = indices.Length / 3; var eligible = new bool[triangles];
        for (int t = 0; t < triangles; t++)
        {
            int a = welded[indices[t * 3]], b = welded[indices[t * 3 + 1]], c = welded[indices[t * 3 + 2]];
            if (a == b || b == c || c == a || Vector3.Cross(vertices[indices[t * 3 + 1]] - vertices[indices[t * 3]],
                vertices[indices[t * 3 + 2]] - vertices[indices[t * 3]]).sqrMagnitude < 1e-16f)
            { report.degenerateTriangles++; continue; }
            eligible[t] = true;
            for (int k = 0; k < 3; k++)
            {
                int x = welded[indices[t * 3 + k]], y = welded[indices[t * 3 + (k + 1) % 3]];
                var key = (Math.Min(x, y), Math.Max(x, y));
                if (!edges.TryGetValue(key, out var uses)) { uses = new List<EdgeUse>(2); edges.Add(key, uses); }
                uses.Add(new EdgeUse { triangle = t, direction = x < y });
            }
        }
        var visited = new bool[triangles]; var orientation = new bool[triangles];
        for (int seed = 0; seed < triangles; seed++)
        {
            if (visited[seed] || !eligible[seed]) continue;
            var component = new List<int>(); var pending = new Stack<int>();
            pending.Push(seed); visited[seed] = true; bool closed = true;
            while (pending.Count > 0)
            {
                int t = pending.Pop(); component.Add(t);
                for (int k = 0; k < 3; k++)
                {
                    int x = welded[indices[t * 3 + k]], y = welded[indices[t * 3 + (k + 1) % 3]];
                    var uses = edges[(Math.Min(x, y), Math.Max(x, y))];
                    if (uses.Count != 2) { closed = false; continue; }
                    var other = uses[0].triangle == t ? uses[1] : uses[0];
                    bool required = orientation[t] ^ (other.direction == (x < y));
                    if (visited[other.triangle]) { if (orientation[other.triangle] != required) closed = false; }
                    else { visited[other.triangle] = true; orientation[other.triangle] = required; pending.Push(other.triangle); }
                }
            }
            if (!closed || component.Count < 4) { report.rejectedComponents++; continue; }
            Vector3 anchor = vertices[indices[component[0] * 3]]; double volume = 0;
            foreach (int t in component)
                volume += (orientation[t] ? -1 : 1) * SignedTetra(vertices[indices[t * 3]], vertices[indices[t * 3 + 1]], vertices[indices[t * 3 + 2]], anchor);
            if (Math.Abs(volume) < 1e-10) { report.rejectedComponents++; continue; }
            report.closedComponents++;
            foreach (int t in component)
            {
                closedTriangles[t] = true;
                if (orientation[t] ^ (volume < 0)) { Flip(indices, flipped, t); report.topologyFlips++; }
            }
        }
    }

    private static void OrientWallsAndReveals(Vector3[] vertices, int[] indices, bool[] flipped, bool[] closedTriangles, ref Report report)
    {
        var walls = Walls();
        for (int t = 0; t < indices.Length / 3; t++)
        {
            Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            foreach (var wall in walls)
            {
                float axisNormal = wall.axis == 0 ? normal.x : normal.z;
                if (Mathf.Abs(axisNormal) < .999f || !wall.Bounds(a, b, c)) continue;
                bool front = wall.On(wall.front, a, b, c), back = wall.On(wall.Back, a, b, c);
                if (!front && !back) continue;
                if (front)
                {
                    var pa = wall.Project(a); var pb = wall.Project(b); var pc = wall.Project(c);
                    if (Mathf.Abs(Cross(pb - pa, pc - pa)) > 1e-8f) wall.mask.Add(new Triangle2 { a = pa, b = pb, c = pc });
                }
                float expected = front ? wall.sign : -wall.sign;
                if (axisNormal * expected < 0)
                {
                    // A proven closed volume has priority over the architectural-plane heuristic.
                    if (closedTriangles[t]) report.protectedPlaneConflicts++;
                    else { Flip(indices, flipped, t); report.wallFlips++; }
                }
                break;
            }
        }
        for (int t = 0; t < indices.Length / 3; t++)
        {
            if (closedTriangles[t]) continue;
            Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized, center = (a + b + c) / 3;
            foreach (var wall in walls)
            {
                float axisNormal = wall.axis == 0 ? normal.x : normal.z;
                if (Mathf.Abs(axisNormal) > .001f || wall.mask.Count == 0 || !wall.Bounds(a, b, c)) continue;
                float min = Mathf.Min(wall.Coordinate(a), Mathf.Min(wall.Coordinate(b), wall.Coordinate(c)));
                float max = Mathf.Max(wall.Coordinate(a), Mathf.Max(wall.Coordinate(b), wall.Coordinate(c)));
                if (Mathf.Abs(min - Mathf.Min(wall.front, wall.Back)) > .0003f
                    || Mathf.Abs(max - Mathf.Max(wall.front, wall.Back)) > .0003f) continue;
                // Only a full-depth reveal of this exact wall layer is eligible. Its projected
                // opening boundary must independently distinguish solid masonry from empty space.
                bool plus = wall.Solid(center + normal * .004f), minus = wall.Solid(center - normal * .004f);
                if (plus == minus) continue;
                if (plus) { Flip(indices, flipped, t); report.revealFlips++; }
                break;
            }
        }
    }

    private static List<Wall> Walls()
    {
        var result = new List<Wall>();
        void Add(int axis, float front, float sign, float minU, float maxU, float height, bool bands = true)
        {
            result.Add(new Wall { axis = axis, front = front, sign = sign, thickness = .36f, minU = minU, maxU = maxU, height = height });
            if (bands) result.Add(new Wall { axis = axis, front = front + sign * (axis == 0 ? .0505f : .045f), sign = sign,
                thickness = axis == 0 ? .065f : .06f, minU = minU, maxU = maxU, height = height });
        }
        // Full red elevations and their projecting cream risalits, two 26 x 22.5 m staggered blocks.
        Add(0, -18.075f, -1, 0, 26, 11.15f); Add(0, 4.425f, 1, 0, 26, 11.15f);
        Add(0, -19.425f, -1, 8.2f, 17.8f, 11.15f, false); Add(0, 5.775f, 1, 8.2f, 17.8f, 11.15f, false);
        Add(0, -4.425f, -1, -26, 0, 17.35f); Add(0, 18.075f, 1, -26, 0, 17.35f);
        Add(0, -5.775f, -1, -17.8f, -8.2f, 17.35f, false); Add(0, 19.425f, 1, -17.8f, -8.2f, 17.35f, false);
        Add(2, 26, 1, -18.075f, 4.425f, 11.15f); Add(2, -26, -1, -4.425f, 18.075f, 17.35f);
        Add(2, 0, 1, -4.425f, 18.075f, 17.35f);
        // Existing low return between the staggered volumes, including its projecting entrance.
        Add(2, 0, -1, -18.075f, -4.425f, 11.15f, false);
        Add(2, -.58f, -1, -14.75f, -7.75f, 11.15f, false);
        return result;
    }
    private static void Flip(int[] indices, bool[] flipped, int triangle)
    {
        int i = triangle * 3 + 1, value = indices[i]; indices[i] = indices[i + 1]; indices[i + 1] = value; flipped[triangle] = !flipped[triangle];
    }
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static double SignedTetra(Vector3 a, Vector3 b, Vector3 c, Vector3 origin)
    {
        double ax = a.x - origin.x, ay = a.y - origin.y, az = a.z - origin.z;
        double bx = b.x - origin.x, by = b.y - origin.y, bz = b.z - origin.z;
        double cx = c.x - origin.x, cy = c.y - origin.y, cz = c.z - origin.z;
        return (ax * (by * cz - bz * cy) + ay * (bz * cx - bx * cz) + az * (bx * cy - by * cx)) / 6;
    }
}
