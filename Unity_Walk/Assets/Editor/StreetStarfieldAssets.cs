using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

public static class StreetStarfieldAssets
{
    public const string Folder = "Assets/StreetHDRP/DayNight";
    public const float DefaultNightEmission = 1f;

    private const int FaceSize = 1024;
    private const int FieldStarCount = 18000;
    private const int GalacticStarCount = 28000;
    private static readonly Color Zenith = new(.082f, .176f, .440f);
    private static readonly Color Horizon = new(.115f, .225f, .470f);
    private static readonly Color MilkyWayBlue = new(.29f, .40f, .66f);
    private static readonly Color MilkyWayCore = new(.43f, .39f, .51f);
    private static readonly Vector3 GalacticNorth = new Vector3(.48f, .81f, .32f).normalized;
    private static readonly Vector3 GalacticCentre =
        Vector3.ProjectOnPlane(new Vector3(.1f, .31f, -1f), GalacticNorth).normalized;
    private static readonly Vector3 GalacticTangent = Vector3.Cross(GalacticNorth, GalacticCentre);

    private readonly struct Star
    {
        internal readonly Vector3 Direction;
        internal readonly Color Radiance;
        internal readonly float Width;

        internal Star(Vector3 direction, Color radiance, float width)
        {
            Direction = direction;
            Radiance = radiance;
            Width = width;
        }
    }

    [MenuItem("Street/Lighting/Bake procedural starfield")]
    public static void Bake()
    {
        GetOrCreate(false);
        GetOrCreate(true);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Street/Lighting/Rebake procedural starfield")]
    public static void Rebake()
    {
        BakeCubemap(false, true);
        BakeCubemap(true, true);
        AssetDatabase.SaveAssets();
    }

    public static Cubemap GetOrCreate(bool milkyWay = true) => BakeCubemap(milkyWay, false);

    private static Cubemap BakeCubemap(bool milkyWay, bool forceRebake)
    {
        string name = milkyWay ? "Night sky with Milky Way" : "Night sky with stars";
        string path = $"{Folder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        if (existing && !forceRebake)
            return existing;

        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/StreetHDRP", "DayNight");

        var format = SystemInfo.IsFormatSupported(GraphicsFormat.E5B9G9R9_UFloatPack32,
            GraphicsFormatUsage.Sample) ? TextureFormat.RGB9e5Float : TextureFormat.RGBAHalf;
        if (existing && (existing.width != FaceSize || existing.mipmapCount < 2 || !existing.isReadable))
            throw new InvalidOperationException($"Cannot rebake incompatible cubemap {path} in place.");
        var cube = existing ? existing : new Cubemap(FaceSize, format, true)
        {
            name = name,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        try
        {
            Star[] catalog = CreateCatalog(milkyWay);
            var pixels = new Color[FaceSize * FaceSize];
            for (int face = 0; face < 6; face++)
            {
                FillBackground(face, pixels, milkyWay);
                foreach (var star in catalog)
                    AddStar(face, pixels, star);
                cube.SetPixels(pixels, (CubemapFace)face);
            }
            // Keep the CPU pixel data available when Unity serializes the authored asset.
            cube.Apply(updateMipmaps: true);
            if (existing)
                EditorUtility.SetDirty(cube);
            else
                AssetDatabase.CreateAsset(cube, path);
            Debug.Log($"STREET_STARFIELD faceSize={FaceSize} format={cube.format} proceduralStars={catalog.Length} " +
                      $"milkyWay={milkyWay} rebake={forceRebake} asset={path}");
            return cube;
        }
        catch
        {
            if (!existing)
                UnityEngine.Object.DestroyImmediate(cube);
            throw;
        }
    }

    private static Star[] CreateCatalog(bool milkyWay)
    {
        var random = new System.Random(638523);
        var catalog = new Star[FieldStarCount + (milkyWay ? GalacticStarCount : 0)];
        for (int i = 0; i < FieldStarCount; i++)
        {
            float y = Next(random) * 2f - 1f;
            float angle = Next(random) * Mathf.PI * 2f;
            float radius = Mathf.Sqrt(1f - y * y);
            var direction = new Vector3(radius * Mathf.Cos(angle), y, radius * Mathf.Sin(angle));
            float magnitude = Next(random);

            // The distribution leaves most stars dim and gives a few isolated points enough
            // radiance to survive HDRP pre-exposure, subpixel filtering and antialiasing.
            float flux = .04f + 2.8f * Mathf.Pow(magnitude, 5.5f);
            if (Next(random) < .012f)
                flux += Mathf.Lerp(3.4f, 9.5f, Next(random));

            Color tint = StarTint(Next(random));
            float width = Mathf.Lerp(.00068f, .00108f, magnitude * magnitude);
            catalog[i] = new Star(direction, tint * flux, width);
        }

        for (int i = FieldStarCount; i < catalog.Length; i++)
        {
            float angle = Next(random) * Mathf.PI * 2f;
            float latitude = (Next(random) + Next(random) - 1f) * .22f;
            float radius = Mathf.Sqrt(1f - latitude * latitude);
            Vector3 direction = (GalacticCentre * Mathf.Cos(angle) + GalacticTangent * Mathf.Sin(angle)) * radius
                                + GalacticNorth * latitude;
            float brightness = Mathf.Lerp(.40f, 1f, Mathf.Pow(.5f + .5f * Mathf.Cos(angle), 2f));
            float gain = brightness * (.018f + .56f * Mathf.Pow(Next(random), 3.75f));
            catalog[i] = new Star(direction, StarTint(Next(random)) * gain, Mathf.Lerp(.00052f, .00074f, Next(random)));
        }
        return catalog;
    }

    private static Color StarTint(float temperature)
    {
        var warm = new Color(1f, .79f, .56f);
        var white = new Color(1f, .98f, .93f);
        var cool = new Color(.69f, .83f, 1f);
        return temperature < .45f
            ? Color.Lerp(warm, white, temperature / .45f)
            : Color.Lerp(white, cool, (temperature - .45f) / .55f);
    }

    private static void FillBackground(int face, Color[] pixels, bool milkyWay)
    {
        for (int y = 0; y < FaceSize; y++)
        for (int x = 0; x < FaceSize; x++)
        {
            Vector3 direction = Direction(face, Coordinate(x), Coordinate(y));
            float horizon = Mathf.Pow(1f - Mathf.Abs(direction.y), 2.4f);
            Color color = Color.Lerp(Zenith, Horizon, horizon);
            if (milkyWay)
                color += Galaxy(direction);
            color.a = 1f;
            pixels[y * FaceSize + x] = color;
        }
    }

    private static Color Galaxy(Vector3 sky)
    {
        float latitude = Vector3.Dot(sky, GalacticNorth);
        if (Mathf.Abs(latitude) > .48f)
            return Color.clear;

        // Sample the band in direction space, with separate density and foreground dust clouds.
        // This keeps its structures continuous at cubemap face boundaries.
        float coarse = DensityNoise(sky * 5.4f + new Vector3(10.5f, 24.3f, 13.1f));
        float detail = DensityNoise(sky * 17f + new Vector3(35.1f, 19.0f, 3.5f));
        float grain = DensityNoise(sky * 48f + new Vector3(5.0f, 46.7f, 7.1f));
        float warped = latitude - (coarse - .5f) * .12f - (detail - .5f) * .035f;
        float band = Gaussian(warped, .123f);
        float distantHalo = Gaussian(warped, .219f);
        float bulge = Mathf.Pow(Mathf.Clamp01(.5f + .5f * Vector3.Dot(sky, GalacticCentre)), 3.5f);
        float clustered = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.22f, .82f, .49f * coarse + .36f * detail + .15f * grain));
        float darkLane = Gaussian(warped + .024f - (detail - .5f) * .081f, .030f);
        float extinction = 1f - darkLane * Mathf.Lerp(.23f, .78f, grain);
        float glow = (.14f + .86f * bulge) * (.158f * distantHalo + 2.95f * band * clustered * extinction);
        return Color.Lerp(MilkyWayBlue, MilkyWayCore, bulge * .83f) * glow;
    }

    private static float DensityNoise(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
        float tx = Smooth(p.x - x), ty = Smooth(p.y - y), tz = Smooth(p.z - z);
        float low = Mathf.Lerp(
            Mathf.Lerp(Hash(x, y, z), Hash(x + 1, y, z), tx),
            Mathf.Lerp(Hash(x, y + 1, z), Hash(x + 1, y + 1, z), tx), ty);
        float high = Mathf.Lerp(
            Mathf.Lerp(Hash(x, y, z + 1), Hash(x + 1, y, z + 1), tx),
            Mathf.Lerp(Hash(x, y + 1, z + 1), Hash(x + 1, y + 1, z + 1), tx), ty);
        return Mathf.Lerp(low, high, tz);
    }

    private static float Hash(int x, int y, int z)
    {
        unchecked
        {
            uint value = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u ^ 0x4ad93b79u;
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            return ((value ^ (value >> 16)) & 0x00ffffffu) / 16777215f;
        }
    }

    private static void AddStar(int face, Color[] pixels, Star star)
    {
        var d = star.Direction;
        float depth;
        float u;
        float v;
        switch (face)
        {
            case 0: depth = d.x; u = -d.z; v = d.y; break;
            case 1: depth = -d.x; u = d.z; v = d.y; break;
            case 2: depth = d.y; u = d.x; v = -d.z; break;
            case 3: depth = -d.y; u = d.x; v = d.z; break;
            case 4: depth = d.z; u = d.x; v = d.y; break;
            default: depth = -d.z; u = -d.x; v = d.y; break;
        }

        if (depth < .56f || Mathf.Abs(u) > depth + .006f || Mathf.Abs(v) > depth + .006f)
            return;

        float centreX = (u / depth * .5f + .5f) * FaceSize - .5f;
        float centreY = (v / depth * .5f + .5f) * FaceSize - .5f;
        float radius = star.Width * 1.6f * FaceSize / (depth * depth) + .5f;
        int minX = Mathf.Max(0, Mathf.FloorToInt(centreX - radius));
        int maxX = Mathf.Min(FaceSize - 1, Mathf.CeilToInt(centreX + radius));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centreY - radius));
        int maxY = Mathf.Min(FaceSize - 1, Mathf.CeilToInt(centreY + radius));
        float inverseWidth = .5f / (star.Width * star.Width);
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            float separation = (Direction(face, Coordinate(x), Coordinate(y)) - d).sqrMagnitude;
            float falloff = Mathf.Exp(-separation * inverseWidth);
            var index = y * FaceSize + x;
            Color color = pixels[index] + star.Radiance * falloff;
            color.a = 1f;
            pixels[index] = color;
        }
    }

    private static Vector3 Direction(int face, float u, float v)
    {
        Vector3 direction = face switch
        {
            0 => new Vector3(1f, v, -u),
            1 => new Vector3(-1f, v, u),
            2 => new Vector3(u, 1f, -v),
            3 => new Vector3(u, -1f, v),
            4 => new Vector3(u, v, 1f),
            _ => new Vector3(-u, v, -1f)
        };
        return direction.normalized;
    }

    private static float Coordinate(int pixel) => 2f * (pixel + .5f) / FaceSize - 1f;
    private static float Next(System.Random random) => (float)random.NextDouble();
    private static float Smooth(float x) => x * x * (3f - 2f * x);
    private static float Gaussian(float x, float spread) => Mathf.Exp(-x * x / (spread * spread));
}
