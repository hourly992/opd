using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static partial class StreetFacadeUtilities
{
    private sealed class Run
    {
        internal Wall Wall;
        internal float From, To, Y, Depth;
        internal float Length => To - From;
        internal float Height => Y - Wall.Start.y;
    }

    private sealed class Drain
    {
        internal Wall Wall;
        internal float X, Depth, Bottom, Top;
        internal Vector3 Foot => Wall.At(X, Bottom, Depth);
    }

    private sealed class Vine
    {
        internal Wall Wall;
        internal float X, Foot, Depth, Height, WidthScale, DepthScale, Turn, Tilt;
        internal int Tint;
    }

    private static Plan PlanDecorations(Site site, ArtSet art)
    {
        var facadeRuns = site.Walls.Where(wall => !wall.Veranda).Select(BestRuns).Where(runs => runs.Length > 0)
            .OrderByDescending(runs => Score(runs)).ToArray();
        int pipeFaces = site.Detailed ? 7 : site.Area >= 42 ? 2 : 1;
        var gas = new List<Run>();
        var gasWalls = new List<Wall>();
        foreach (var runs in facadeRuns)
        {
            var wall = runs[0].Wall;
            if (gasWalls.Any(other => NearSameWall(other, wall) &&
                Mathf.Abs(Vector3.Dot(other.At(.5f * other.Width, 0) - wall.At(.5f * wall.Width, 0), wall.Across))
                < Mathf.Max(other.Width, wall.Width) * .47f)) continue;
            gas.AddRange(runs);
            gasWalls.Add(wall);
            if (gasWalls.Count >= pipeFaces) break;
        }

        var orderedWalls = gasWalls.Concat(site.Walls.OrderByDescending(wall => wall.Width)).Distinct().ToArray();
        var existing = site.Detailed ? FindExistingDrains(site) : new List<Drain>();
        var drains = new List<Drain>();
        if (existing.Count == 0)
            foreach (var wall in orderedWalls)
            {
                var drain = FindDrain(wall);
                if (drain == null || drains.Any(other => (other.Foot - drain.Foot).sqrMagnitude < 4)) continue;
                drains.Add(drain);
                if (drains.Count == (site.Area >= 32 ? 2 : 1)) break;
            }

        var vines = new List<Vine>();
        foreach (var wall in orderedWalls)
        {
            var vine = FindVine(wall, gas, existing.Concat(drains), art.ModelBounds, vines.Count);
            if (vine == null || vines.Any(other => (other.Wall.At(other.X, other.Foot, other.Depth) -
                wall.At(vine.X, vine.Foot, vine.Depth)).sqrMagnitude < 4)) continue;
            vines.Add(vine);
            if (vines.Count == (site.Detailed ? (site.Area > 650 ? 3 : 2) : 1)) break;
        }
        return new Plan
        {
            Site = site, Gas = gas.ToArray(), Existing = existing.ToArray(), Drains = drains.ToArray(), Vines = vines.ToArray()
        };
    }

    private static float Score(Run[] runs) => runs.Sum(run => run.Length) * .65f + runs.Max(run => run.Length) * .35f;

    private static Run[] BestRuns(Wall wall)
    {
        float[] heights = !wall.Site.Detailed && wall.Site.Source.levels <= 1
            ? new[] { 1.03f, .92f, 1.13f, .78f, .58f, 3.06f, 3.24f }
            : new[] { 4.29f, 4.16f, 4.44f, 3.12f, 3.36f, 3.71f, 2.93f, 1.52f, 1.10f, .88f, 4.58f };
        if (!wall.Site.Detailed && wall.Site.Source.levels > 1)
            heights = new[] { 3.10f, 3.38f, 3.62f, 2.92f, 1.10f, .94f, .77f };
        var best = Array.Empty<Run>();
        float bestScore = 0;
        for (int i = 0; i < heights.Length; i++)
        {
            float y = wall.Y(heights[i]);
            if (heights[i] > wall.Height - .42f) continue;
            var runs = ScanRuns(wall, y);
            if (runs.Length == 0) continue;
            float quality = Score(runs) * (1f - .026f * i);
            if (quality <= bestScore) continue;
            best = runs;
            bestScore = quality;
            if (quality > wall.Width * .88f) break;
        }
        return best;
    }

    private static Run[] ScanRuns(Wall wall, float y)
    {
        const float margin = .30f;
        int steps = Mathf.Max(2, Mathf.CeilToInt((wall.Width - 2 * margin) / .32f));
        var runs = new List<Run>();
        float start = -1, end = 0, baseDepth = 0;
        void Flush()
        {
            float length = end - start;
            if (start >= 0 && length >= Mathf.Min(2.15f, wall.Width * .52f))
                runs.Add(new Run { Wall = wall, Y = y, From = start, To = end, Depth = baseDepth });
            start = -1;
        }
        for (int i = 0; i <= steps; i++)
        {
            float x = Mathf.Lerp(margin, wall.Width - margin, i / (float)steps);
            if (!WallDepth(wall, x, y, true, out float depth) ||
                !WallDepth(wall, x, y - .073f, true, out float below) ||
                !WallDepth(wall, x, y + .073f, true, out float above) ||
                Mathf.Abs(depth - below) > .12f || Mathf.Abs(depth - above) > .12f ||
                y - Ground(wall.At(x, y, depth + GasOffset), wall.Start.y) < .38f)
            {
                Flush();
                continue;
            }
            if (start >= 0 && Mathf.Abs(depth - baseDepth) > .085f) Flush();
            if (start < 0)
            {
                start = x;
                baseDepth = depth;
            }
            end = x;
        }
        Flush();
        return runs.ToArray();
    }

    private static List<Drain> FindExistingDrains(Site site)
    {
        var result = new List<Drain>();
        foreach (var wall in site.Walls)
        {
            float step = .068f;
            for (float x = .24f; x < wall.Width - .24f; x += step)
            {
                const float front = .65f;
                float low = wall.Y(Mathf.Min(2.45f, wall.Height * .32f)), high = wall.Y(wall.Height * .69f);
                if (!HitFacade(wall, x, low, front, out var a, out var aSurface) || aSurface != Surface.Gutter ||
                    !HitFacade(wall, x, high, front, out var b, out var bSurface) || bSurface != Surface.Gutter ||
                    Mathf.Abs(a.distance - b.distance) > .13f || a.distance > front - .035f) continue;
                var drain = new Drain
                {
                    Wall = wall, X = x, Bottom = wall.Start.y, Depth = front - a.distance,
                    Top = wall.Y(wall.Height)
                };
                if (result.All(other => (other.Foot - drain.Foot).sqrMagnitude > .32f * .32f))
                    result.Add(drain);
            }
        }
        return result;
    }

    private static Drain FindDrain(Wall wall)
    {
        var positions = wall.Veranda
            ? Enumerable.Range(5, Mathf.Max(0, Mathf.FloorToInt(wall.Width / .075f) - 10)).Select(i => i * .075f)
            : new[] { .09f, .91f, .21f, .79f, .30f, .70f, .46f, .54f }.Select(fraction => wall.Width * fraction);
        foreach (float position in positions)
        {
            float x = Mathf.Clamp(position, .32f, wall.Width - .32f);
            float foot = Mathf.Max(wall.Start.y, Ground(wall.At(x, 0, .37f), wall.Start.y));
            float top = wall.Y(wall.Height - .08f);
            if (!WallDepth(wall, x, foot + .43f, false, out var depth)) continue;
            bool whole = true;
            for (float y = foot + .74f; y < top - .05f; y += .66f)
            {
                if (WallDepth(wall, x, y, false, out var next) && Mathf.Abs(depth - next) < .13f) continue;
                whole = false;
                break;
            }
            if (!whole) continue;
            var above = wall.At(x, top + 1.8f, depth + .085f);
            if (wall.Geometry.Sample(new Ray(above, Vector3.down), 2.45f, out var lip, out _) && lip.normal.y > .30f)
                top = Mathf.Clamp(lip.point.y - .02f, top - .33f, top + .35f);
            return new Drain { Wall = wall, X = x, Depth = depth, Bottom = foot, Top = top };
        }
        return null;
    }

    private static Vine FindVine(Wall wall, IEnumerable<Run> gas, IEnumerable<Drain> drains, Bounds mesh, int order)
    {
        float variant = Mathf.Repeat(wall.Site.Source.id % 331 * .372f + order * .337f, 1f);
        float height = wall.Site.Detailed ? Mathf.Lerp(1.65f, 2.78f, variant) : Mathf.Lerp(.96f, 1.76f, variant);
        if (wall.Veranda) height = .96f;
        var gasOnWall = gas.Where(run => run.Wall == wall).ToArray();
        var gutters = drains.ToArray();
        float shift = Mathf.Lerp(.17f, .37f, variant);
        float prefer = wall.Width * (variant < .5f ? shift : 1 - shift);
        var candidates = Enumerable.Range(1, Mathf.Max(3, Mathf.CeilToInt(wall.Width / .39f)))
            .Select(i => i * .39f).Where(x => x < wall.Width - .27f).OrderBy(x => Mathf.Abs(x - prefer)).ToArray();
        foreach (float factor in new[] { 1f, .82f, .64f })
        {
            float length = height * factor;
            float widthScale = length * Mathf.Lerp(.67f, .90f, variant);
            float halfWidth = mesh.size.x * widthScale * .5f + .09f;
            foreach (float x in candidates)
            {
                if (x < halfWidth + .11f || x > wall.Width - halfWidth - .11f) continue;
                float foot = Mathf.Max(wall.Start.y + .055f, Ground(wall.At(x, 0, .24f), wall.Start.y) + .065f);
                if (gasOnWall.Any(run => x + halfWidth > run.From - .10f && x - halfWidth < run.To + .10f &&
                    run.Y >= foot - .09f && run.Y <= foot + length + .13f)) continue;
                if (gutters.Any(drain => (drain.Foot - wall.At(x, drain.Bottom, 0)).sqrMagnitude <
                    Mathf.Pow(halfWidth + .20f, 2))) continue;
                if (!WallDepth(wall, x, foot + length * .64f, false, out float depth)) continue;
                bool free = true;
                float outer = depth;
                for (int row = 0; row < 5 && free; row++)
                for (int col = -1; col <= 1; col++)
                {
                    float y = foot + length * (.09f + row * .215f);
                    if (!WallDepth(wall, x + col * halfWidth, y, false, out var at) || Mathf.Abs(at - depth) > .106f)
                    {
                        free = false;
                        break;
                    }
                    outer = Mathf.Max(outer, at);
                }
                if (!free || foot + length > wall.Y(wall.Height - .25f)) continue;
                return new Vine
                {
                    Wall = wall, X = x, Foot = foot, Depth = outer + .029f, Height = length,
                    WidthScale = widthScale, DepthScale = Mathf.Lerp(.34f, .48f, variant),
                    Turn = Mathf.Lerp(-6, 6, variant), Tilt = Mathf.Lerp(-3.5f, 3.5f, variant),
                    Tint = Mathf.Min(2, (int)(variant * 3))
                };
            }
        }
        return null;
    }

    private static void DrawGas(EnvironmentMesh mesh, Run run, ArtSet art)
    {
        var wall = run.Wall;
        float radius = wall.Site.Detailed || wall.Site.Source.levels >= 2 ? .050f : .039f;
        float r = Mathf.Min(radius, run.Length * .06f);
        Vector3 a = wall.At(run.From, run.Y, run.Depth + GasOffset);
        Vector3 b = wall.At(run.To, run.Y, run.Depth + GasOffset);
        const float turn = .108f;
        Vector3 begin = a + wall.Across * turn;
        Vector3 end = b - wall.Across * turn;
        mesh.Cylinder(art.yellow, begin, end, r, sides: 12);
        DrawReturn(a, begin);
        DrawReturn(b, end);
        for (float x = run.From + Mathf.Min(.63f, run.Length * .26f); x < run.To - .31f; x += 3.15f)
        {
            if (!WallDepth(wall, x, run.Y, true, out var mountDepth)) continue;
            var center = wall.At(x, run.Y, run.Depth + GasOffset);
            Bracket(mesh, art, wall, center, wall.At(x, run.Y, mountDepth), r, wall.Across, art.darkerYellow);
        }
        if (run.Length > 7.5f)
        {
            var join = Vector3.Lerp(begin, end, .55f);
            mesh.Cylinder(art.darkerYellow, join - wall.Across * .034f, join + wall.Across * .034f,
                r + .006f, sides: 12);
        }

        void DrawReturn(Vector3 corner, Vector3 endOfBend)
        {
            var startOfBend = corner - wall.Out * turn;
            mesh.Cylinder(art.yellow, corner - wall.Out * (GasOffset - .007f), startOfBend, r, sides: 12);
            Elbow(mesh, art.yellow, startOfBend, corner, endOfBend, r);
        }
    }

    private static void DrawDrain(EnvironmentMesh mesh, Drain drain, ArtSet art)
    {
        var wall = drain.Wall;
        float radius = wall.Site.Source.levels <= 1 ? .050f : .056f;
        Vector3 bottom = wall.At(drain.X, drain.Bottom + .29f, drain.Depth + DrainOffset);
        Vector3 top = wall.At(drain.X, drain.Top - .09f, drain.Depth + DrainOffset);
        Vector3 bend = bottom - Vector3.up * .141f;
        Vector3 outlet = bend + wall.Out * .15f;
        Vector3 mouth = outlet + wall.Out * .18f;
        mesh.Cylinder(art.drain, bottom, top, radius, sides: 12);
        var hopper = wall.At(drain.X, drain.Top - .011f, drain.Depth + DrainOffset * .82f);
        mesh.Cylinder(art.drain, top, hopper, radius, radius * 1.46f, 12);
        Elbow(mesh, art.drain, bottom, bend, outlet, radius);
        // The rolled lip has a dark inset so the outlet reads as an opening.
        mesh.Cylinder(art.drain, outlet, mouth, radius, sides: 12);
        mesh.Cylinder(art.drain, mouth - wall.Out * .034f, mouth + wall.Out * .001f, radius + .007f, sides: 12);
        mesh.Face(art.mouth, wall.Out, RadialRing(mouth + wall.Out * .0015f, wall.Out, radius * .80f));
        for (float y = drain.Bottom + .93f; y < drain.Top - .24f; y += 2.82f)
        {
            if (!WallDepth(wall, drain.X, y, false, out var mountDepth)) continue;
            Bracket(mesh, art, wall, wall.At(drain.X, y, drain.Depth + DrainOffset),
                wall.At(drain.X, y, mountDepth), radius, Vector3.up, art.drain);
        }
    }

    private static void Bracket(EnvironmentMesh mesh, ArtSet art, Wall wall, Vector3 center, Vector3 surface,
        float pipeRadius, Vector3 axis, Material collar)
    {
        var plate = surface + wall.Out * .014f;
        mesh.Box(art.bracket, plate, new Vector3(.084f, .085f, .028f), Quaternion.LookRotation(wall.Out));
        mesh.Cylinder(art.bracket, plate, center - wall.Out * (pipeRadius * .82f), .016f, sides: 6);
        mesh.Cylinder(collar, center - axis * .016f, center + axis * .016f, pipeRadius + .007f, sides: 10);
    }

    private static void Elbow(EnvironmentMesh mesh, Material material, Vector3 a, Vector3 corner, Vector3 b, float radius)
    {
        Vector3[] previous = null;
        Vector3 previousPoint = default;
        Vector3 guide = Vector3.Cross(corner - a, b - corner).normalized;
        const int segments = 5;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments, s = 1 - t;
            Vector3 point = s * s * a + 2 * t * s * corner + t * t * b;
            Vector3 axis = (s * (corner - a) + t * (b - corner)).normalized;
            var ring = RadialRing(point, axis, radius, guide);
            if (previous != null)
                for (int j = 0; j < ring.Length; j++)
                {
                    int k = (j + 1) % ring.Length;
                    var normals = new[] { (previous[j] - previousPoint).normalized, (previous[k] - previousPoint).normalized,
                        (ring[k] - point).normalized, (ring[j] - point).normalized };
                    mesh.Surface(material, new[] { previous[j], previous[k], ring[k], ring[j] }, normals);
                }
            previous = ring;
            previousPoint = point;
        }
    }

    private static Vector3[] RadialRing(Vector3 center, Vector3 axis, float radius, Vector3 guide = default)
    {
        // A constant perpendicular guide within each plane keeps the elbow rings from twisting.
        if (guide == Vector3.zero)
            guide = Vector3.Cross(axis, Mathf.Abs(axis.y) < .9f ? Vector3.up : Vector3.forward).normalized;
        var second = Vector3.Cross(axis, guide);
        return Enumerable.Range(0, 12).Select(i =>
        {
            float a = i * Mathf.PI * 2 / 12;
            return center + radius * (guide * Mathf.Cos(a) + second * Mathf.Sin(a));
        }).ToArray();
    }
}
