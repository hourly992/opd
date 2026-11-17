using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public static class GarageSurfaceCut
{
    private const float Epsilon = 0.00001f;

    // Corners are supplied as world (x,z), in either winding order. The
    // half-plane clipping retains every triangle fragment outside the court.
    public static Mesh Clip(Mesh source, Matrix4x4 localToWorld, Vector2[] footprint, out int changedTriangles)
    {
        changedTriangles = 0;
        Vector2[] polygon = CanonicalFootprint(footprint);
        Rect bounds = Bounds(polygon);
        if (!source || !source.isReadable)
            throw new ArgumentException("The source surface mesh must be readable.", nameof(source));

        var originalVertices = source.vertices;
        var vertices = new List<Vector3>(originalVertices);
        var normals = new List<Vector3>(source.normals);
        var tangents = new List<Vector4>(source.tangents);
        var colors = new List<Color>(source.colors);
        var uv = new List<Vector4>[8];
        for (int channel = 0; channel < uv.Length; channel++)
        {
            uv[channel] = new List<Vector4>();
            source.GetUVs(channel, uv[channel]);
        }
        var world = Array.ConvertAll(originalVertices, p => localToWorld.MultiplyPoint3x4(p));
        var submeshIndices = new List<int>[source.subMeshCount];
        var triangle = new List<Vector3>(8);
        var inside = new List<Vector3>(8);
        var outside = new List<Vector3>(8);
        var check = new List<Vector3>(8);
        var next = new List<Vector3>(8);

        for (int submesh = 0; submesh < source.subMeshCount; submesh++)
        {
            if (source.GetTopology(submesh) != MeshTopology.Triangles)
                throw new InvalidOperationException($"Non-triangular surface submesh in {source.name}.");
            int[] indices = source.GetIndices(submesh);
            var retained = new List<int>(indices.Length);
            submeshIndices[submesh] = retained;
            for (int index = 0; index < indices.Length; index += 3)
            {
                int a = indices[index], b = indices[index + 1], c = indices[index + 2];
                Vector3 wa = world[a], wb = world[b], wc = world[c];
                if (Mathf.Max(wa.x, Mathf.Max(wb.x, wc.x)) < bounds.xMin ||
                    Mathf.Min(wa.x, Mathf.Min(wb.x, wc.x)) > bounds.xMax ||
                    Mathf.Max(wa.z, Mathf.Max(wb.z, wc.z)) < bounds.yMin ||
                    Mathf.Min(wa.z, Mathf.Min(wb.z, wc.z)) > bounds.yMax)
                {
                    AddOriginal();
                    continue;
                }

                Vector2 pa = Horizontal(wa), pb = Horizontal(wb), pc = Horizontal(wc);
                Vector3 Position(Vector3 q) => wa * q.x + wb * q.y + wc * q.z;
                triangle.Clear();
                triangle.Add(Vector3.right);
                triangle.Add(Vector3.up);
                triangle.Add(Vector3.forward);
                check.Clear();
                check.AddRange(triangle);

                // First verify a nonzero intersection. This keeps even diagonal
                // corner touches from needlessly splitting untouched triangles.
                for (int edge = 0; edge < polygon.Length && check.Count > 0; edge++)
                {
                    Vector2 start = polygon[edge], stop = polygon[(edge + 1) % polygon.Length];
                    Vector3 distances = Distances(start, stop, pa, pb, pc);
                    Split(check, distances, next, outside);
                    (check, next) = (next, check);
                }
                bool strictlyInside = false;
                if (check.Count >= 3)
                {
                    Vector3 center = Vector3.zero;
                    foreach (Vector3 q in check) center += q;
                    center /= check.Count;
                    strictlyInside = true;
                    for (int edge = 0; edge < polygon.Length; edge++)
                    {
                        Vector3 distances = Distances(polygon[edge], polygon[(edge + 1) % polygon.Length], pa, pb, pc);
                        if (Vector3.Dot(distances, center) <= Epsilon)
                        {
                            strictlyInside = false;
                            break;
                        }
                    }
                }
                if (!strictlyInside || AreaSquared(check, wa, wb, wc) <= 1e-14f)
                {
                    AddOriginal();
                    continue;
                }

                changedTriangles++;
                for (int edge = 0; edge < polygon.Length && triangle.Count > 0; edge++)
                {
                    Vector3 distances = Distances(polygon[edge], polygon[(edge + 1) % polygon.Length], pa, pb, pc);
                    Split(triangle, distances, inside, outside);
                    if (outside.Count >= 3)
                    {
                        Vector3 first = Position(outside[0]);
                        for (int j = 1; j + 1 < outside.Count; j++)
                        {
                            var cross = Vector3.Cross(Position(outside[j]) - first, Position(outside[j + 1]) - first);
                            if (cross.sqrMagnitude <= 1e-14f) continue;
                            retained.Add(AddVertex(outside[0], a, b, c));
                            retained.Add(AddVertex(outside[j], a, b, c));
                            retained.Add(AddVertex(outside[j + 1], a, b, c));
                        }
                    }
                    (triangle, inside) = (inside, triangle);
                }

                void AddOriginal()
                {
                    retained.Add(a);
                    retained.Add(b);
                    retained.Add(c);
                }
            }
        }

        if (changedTriangles == 0)
            return null;

        var result = new Mesh
        {
            name = source.name + " · garage boundary",
            indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : source.indexFormat,
            subMeshCount = source.subMeshCount
        };
        result.SetVertices(vertices);
        if (normals.Count > 0) result.SetNormals(normals);
        if (tangents.Count > 0) result.SetTangents(tangents);
        if (colors.Count > 0)
        {
            if (source.GetVertexAttributeFormat(VertexAttribute.Color) == VertexAttributeFormat.UNorm8)
                result.SetColors(colors.ConvertAll(c => (Color32)c));
            else result.SetColors(colors);
        }
        for (int channel = 0; channel < uv.Length; channel++)
        {
            if (uv[channel].Count == 0) continue;
            int dimension = source.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel);
            switch (dimension)
            {
                case 2: result.SetUVs(channel, uv[channel].ConvertAll(v => new Vector2(v.x, v.y))); break;
                case 3: result.SetUVs(channel, uv[channel].ConvertAll(v => new Vector3(v.x, v.y, v.z))); break;
                default: result.SetUVs(channel, uv[channel]); break;
            }
        }
        for (int submesh = 0; submesh < submeshIndices.Length; submesh++)
            result.SetTriangles(submeshIndices[submesh], submesh, false);
        result.RecalculateBounds();
        return result;

        int AddVertex(Vector3 q, int a, int b, int c)
        {
            if (q == Vector3.right) return a;
            if (q == Vector3.up) return b;
            if (q == Vector3.forward) return c;
            int index = vertices.Count;
            vertices.Add(vertices[a] * q.x + vertices[b] * q.y + vertices[c] * q.z);
            if (normals.Count > 0)
                normals.Add((normals[a] * q.x + normals[b] * q.y + normals[c] * q.z).normalized);
            if (tangents.Count > 0)
            {
                Vector4 t = tangents[a] * q.x + tangents[b] * q.y + tangents[c] * q.z;
                Vector3 direction = ((Vector3)t).normalized;
                tangents.Add(new Vector4(direction.x, direction.y, direction.z, t.w < 0 ? -1f : 1f));
            }
            if (colors.Count > 0)
                colors.Add(colors[a] * q.x + colors[b] * q.y + colors[c] * q.z);
            foreach (List<Vector4> channel in uv)
                if (channel.Count > 0)
                    channel.Add(channel[a] * q.x + channel[b] * q.y + channel[c] * q.z);
            return index;
        }
    }

    public static Vector2[] CanonicalFootprint(Vector2[] footprint)
    {
        if (footprint == null || footprint.Length < 3)
            throw new ArgumentException("Supply a convex boundary in world XZ.", nameof(footprint));
        var polygon = (Vector2[])footprint.Clone();
        float signedArea = 0f;
        for (int i = 0; i < polygon.Length; i++)
            signedArea += Cross(polygon[i] - polygon[0], polygon[(i + 1) % polygon.Length] - polygon[0]);
        if (Mathf.Abs(signedArea) < Epsilon)
            throw new ArgumentException("The garage footprint has no area.", nameof(footprint));
        if (signedArea < 0f) Array.Reverse(polygon);
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 edge = polygon[(i + 1) % polygon.Length] - polygon[i];
            if (edge.sqrMagnitude < Epsilon * Epsilon)
                throw new ArgumentException("The garage footprint has a repeated corner.", nameof(footprint));
            for (int j = 0; j < polygon.Length; j++)
                if (Cross(edge, polygon[j] - polygon[i]) < -Epsilon)
                    throw new ArgumentException("The garage footprint must be convex.", nameof(footprint));
        }
        return polygon;
    }

    public static Rect Bounds(Vector2[] polygon)
    {
        Vector2 min = polygon[0], max = polygon[0];
        foreach (Vector2 point in polygon)
        {
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Split(List<Vector3> input, Vector3 distances, List<Vector3> inside, List<Vector3> outside)
    {
        inside.Clear();
        outside.Clear();
        if (input.Count == 0) return;
        Vector3 previous = input[input.Count - 1];
        float previousDistance = SnappedDistance(previous, distances);
        foreach (Vector3 current in input)
        {
            float distance = SnappedDistance(current, distances);
            if ((distance < 0f && previousDistance > 0f) || (distance > 0f && previousDistance < 0f))
            {
                Vector3 crossing = Vector3.LerpUnclamped(previous, current, previousDistance / (previousDistance - distance));
                inside.Add(crossing);
                outside.Add(crossing);
            }
            if (distance >= 0f) inside.Add(current);
            if (distance <= 0f) outside.Add(current);
            previous = current;
            previousDistance = distance;
        }
    }

    private static float SnappedDistance(Vector3 q, Vector3 distances)
    {
        float distance = Vector3.Dot(distances, q);
        return Mathf.Abs(distance) <= Epsilon ? 0f : distance;
    }

    private static float AreaSquared(List<Vector3> polygon, Vector3 a, Vector3 b, Vector3 c)
    {
        if (polygon.Count < 3) return 0f;
        Vector3 cross = Vector3.zero;
        Vector3 first = Position(polygon[0]);
        for (int i = 1; i + 1 < polygon.Count; i++)
            cross += Vector3.Cross(Position(polygon[i]) - first, Position(polygon[i + 1]) - first);
        return cross.sqrMagnitude;

        Vector3 Position(Vector3 q) => a * q.x + b * q.y + c * q.z;
    }

    private static Vector3 Distances(Vector2 start, Vector2 stop, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 edge = (stop - start).normalized;
        return new Vector3(Cross(edge, a - start), Cross(edge, b - start), Cross(edge, c - start));
    }

    private static Vector2 Horizontal(Vector3 point) => new Vector2(point.x, point.z);
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
}
