using System.Collections.Generic;
using UnityEngine;

// Original birch/maple/aspen silhouettes share a small alpha atlas. A near leaf
// has two folded panels (four triangles), the same budget as the painted pass.
internal static class AutumnLeafShape
{
    internal static void Append(List<Vector3> vertices, List<Vector2> uv,
        List<Color32> colors, List<int> triangles, Vector3 center, Vector3 axis,
        Vector3 width, Vector3 normal, Color tint, int variant, bool near)
    {
        int first = vertices.Count;
        Vector2[] outline = near
            ? new[] { new Vector2(-.5f,-.51f), new Vector2(-.5f,.51f),
                new Vector2(0,-.51f), new Vector2(0,.51f),
                new Vector2(.5f,-.51f), new Vector2(.5f,.51f) }
            : new[] { new Vector2(-.5f,-.51f), new Vector2(-.5f,.51f),
                new Vector2(.5f,.51f), new Vector2(.5f,-.51f) };
        foreach (var point in outline)
        {
            float curl = near ? (Mathf.Abs(point.x) > .1f ? .003f : .010f) +
                Mathf.Max(0, point.y - .25f) * .012f : 0;
            vertices.Add(center + width * point.x + axis * point.y + normal * curl);
            uv.Add(new Vector2((variant % 3 + .5f + point.x) / 3f, .5f + point.y));
            colors.Add(tint);
        }
        int[] indices = near ? new[] { 0,1,3, 0,3,2, 2,3,5, 2,5,4 } : new[] { 0,1,2, 0,2,3 };
        foreach (int index in indices) triangles.Add(first + index);
    }
}
