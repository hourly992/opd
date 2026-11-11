using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Small CC0 resource selection; no package dependency or runtime download.
internal static class StreetSurfaceLibrary
{
    private const string Sources = "Assets/ThirdParty/PolyHaven/";
    [Serializable] private sealed class FacadeDocument { public FacadeSource[] materials; }
    [Serializable] private sealed class FacadeSource
    {
        public string id;
        public float[] linear_albedo_mean;
        public int dominant_horizontal_courses;
    }
    private static System.Collections.Generic.Dictionary<string, FacadeSource> facadeSources;

    internal static void Initialize()
    {
        if (!File.Exists(Sources + "SOURCES.json"))
            throw new InvalidOperationException("Run fetch_environment_assets.py before generating the sunny street.");
        if (!File.Exists(Sources + "Graphics2K/SOURCES.json") || !File.Exists("Assets/Art/Textures/PROVENANCE.json"))
            throw new InvalidOperationException("Run fetch_graphics_assets.py and prepare_art_textures.py before generation.");
        if (!File.Exists(Sources + "RoadSurfaces2K/SOURCES.json"))
            throw new InvalidOperationException("Run prepare_road_materials.py before generation.");
        string facadeManifest = Sources + "Facades4K/SOURCES.json";
        if (!File.Exists(facadeManifest)) throw new InvalidOperationException("Run prepare_facade_materials.py before generation.");
        facadeSources = JsonUtility.FromJson<FacadeDocument>(File.ReadAllText(facadeManifest)).materials.ToDictionary(m => m.id);
        foreach (string path in Directory.GetFiles(Sources, "*", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles("Assets/Art/Textures", "*", SearchOption.AllDirectories)))
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".png" && extension != ".hdr") continue;
            string assetPath = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (!importer) continue;
            string name = Path.GetFileNameWithoutExtension(path);
            bool normal = name.EndsWith("Normal", StringComparison.Ordinal), scalar = name == "Mask" || name == "Occlusion" || name == "Roughness" || name == "Opacity";
            bool foliage = name == "Foliage" || name == "Leaf" || name == "Sprig" || name == "Leaves", hdr = extension == ".hdr";
            float cutoff = name == "Sprig" ? .28f : .45f;
            bool srgb = !normal && !scalar && !hdr;
            bool facade = path.Contains("Facades4K"), roadSurface = path.Contains("RoadSurfaces2K");
            int size = facade ? 4096 : hdr || path.Contains("Graphics2K") || roadSurface || name == "Interiors" ? 2048 : 1024;
            int anisotropy = facade || roadSurface ? 16 : 8;
            var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType == type && importer.textureShape == TextureImporterShape.Texture2D && importer.sRGBTexture == srgb && importer.maxTextureSize == size
                && importer.mipmapEnabled == !hdr && importer.anisoLevel == anisotropy && importer.alphaIsTransparency == foliage
                && importer.mipMapsPreserveCoverage == foliage && importer.filterMode == FilterMode.Trilinear
                && (!foliage || Mathf.Approximately(importer.alphaTestReferenceValue, cutoff))
                && (!hdr || importer.wrapModeU == TextureWrapMode.Repeat && importer.wrapModeV == TextureWrapMode.Clamp)) continue;
            importer.textureType = type;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = size;
            // atan2 at a panorama seam otherwise selects the sun-coloured final mip, drawing a white vertical line.
            importer.mipmapEnabled = !hdr;
            importer.anisoLevel = anisotropy;
            importer.filterMode = FilterMode.Trilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.alphaIsTransparency = foliage;
            importer.mipMapsPreserveCoverage = foliage;
            importer.alphaTestReferenceValue = cutoff;
            importer.wrapMode = foliage || hdr || name == "Interiors" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            if (hdr) { importer.wrapModeU = TextureWrapMode.Repeat; importer.wrapModeV = TextureWrapMode.Clamp; }
            importer.SaveAndReimport();
        }
    }

    internal static Texture2D Texture(string asset, string name, string extension = "jpg")
    {
        string path = Sources + asset + "/" + name + "." + extension;
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (!texture) throw new InvalidOperationException("Missing CC0 texture: " + path);
        return texture;
    }

    internal static void Apply(Material material, string asset, float meters, Color tint, float normal = .7f, float verticalMeters = 0, float saturation = 1)
    {
        if (asset == "red_brick_03") { asset = "red_bricks_04"; meters = 1.6f; verticalMeters = 1.6f; }
        ApplyMaps(material, Sources + "Graphics2K/" + asset, meters, tint, normal, verticalMeters, saturation);
        material.SetFloat("_SurfaceClass", asset.Contains("grass") || asset == "gravel_floor_02" ? 1 : asset.Contains("brick") ? 2 : 0);
        material.SetFloat("_Parallax", asset.Contains("brick") ? .012f : asset.Contains("grass") ? .008f : .004f);
    }

    private static void ApplyMaps(Material material, string folder, float meters, Color tint, float normal = .6f,
        float verticalMeters = 0, float saturation = 1)
    {
        material.shader = Shader.Find("Street/TexturedSurface");
        Texture2D Load(string name)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/" + name + (name == "Mask" || folder.Contains("/Art/") && name == "Normal" ? ".png" : ".jpg"));
            if (!texture) throw new InvalidOperationException("Missing graphics map: " + folder + "/" + name);
            return texture;
        }
        material.SetTexture("_MainTex", Load("Albedo"));
        material.SetTexture("_BumpMap", Load("Normal"));
        material.SetTexture("_MaskMap", Load("Mask"));
        material.SetColor("_Color", tint);
        material.SetVector("_TileMeters", new Vector4(meters, verticalMeters > 0 ? verticalMeters : meters, 0, 0));
        material.SetFloat("_Saturation", saturation);
        material.SetFloat("_BumpStrength", normal);
        material.SetFloat("_UseUV", 0); material.SetFloat("_UVRotation", 0); material.SetFloat("_Colorize", 0);
        material.SetFloat("_Metallic", 0); material.SetFloat("_RoughnessScale", 1); material.SetFloat("_RoughnessBias", 0);
        material.SetFloat("_AlbedoContrast", 1); material.SetVector("_AlbedoMean", new Vector4(.3f, .3f, .3f, 0));
        material.SetFloat("_OcclusionStrength", .65f);
        material.SetFloat("_Weathering", .3f); material.SetFloat("_Parallax", .002f); material.SetFloat("_SurfaceClass", 0);
        material.SetFloat("_Cull", 2); material.SetColor("_EmissionColor", Color.black);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    internal static void ApplyNamed(Material material, string key)
    {
        Color color = material.GetColor("_Color");
        float smoothness = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : .15f;
        float metallic = material.HasProperty("_Metallic") ? material.GetFloat("_Metallic") : 0;
        Color emission = material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor") : Color.black;
        switch (key)
        {
            case "MainRoadAsphalt":
                // Photographed cracked road: a separate map family from paths and parking aprons.
                Apply(material, "asphalt_02", 3, new Color(.40f, .42f, .43f), .35f);
                AsphaltFinish(material, .82f, .16f, .36f); break;
            case "Asphalt":
                ApplyMaps(material, Sources + "RoadSurfaces2K/asphalt_01", 2.08f,
                    new Color(.68f, .69f, .69f), .32f);
                AsphaltFinish(material, .90f, .08f, .43f); break;
            case "FootpathAsphalt":
                ApplyMaps(material, Sources + "RoadSurfaces2K/clean_asphalt", 2.10f,
                    new Color(.69f, .70f, .71f), .22f);
                AsphaltFinish(material, .87f, .13f, .29f); break;
            case "Grass":
                Apply(material, "grass_ground", 2, new Color(.60f, .86f, .48f), .8f, 2, .9f);
                material.SetFloat("_Weathering", .08f); break;
            case "Pavement": case "WarmPaving":
                Apply(material, "concrete_wall_006", 1.4f, Color.white, .35f);
                material.SetFloat("_SurfaceClass", 6); material.SetFloat("_Parallax", .001f); break;
            case "Path": Apply(material, "concrete_pavement", 1.8f, new Color(.85f, .86f, .83f)); break;
            case "Concrete": case "EntranceConcrete": case "RoadKerb": case "Kerbstone": case "Curb":
                Apply(material, "concrete_wall_006", 1.25f, new Color(1.14f, 1.15f, 1.13f), .55f); break;
            case "Brick0": case "RedBrick": ApplyFacade(material, false); break;
            case "Brick1": case "CreamBrick": ApplyFacade(material, true); break;
            case "Brick2":
                ApplyFacade(material, true);
                material.SetColor("_Color", material.GetColor("_Color") * new Color(.96f, .97f, .98f)); break;
            case "Roof": case "GreenStandingSeam": case "GreyStandingSeam":
                Own(material, "Roof", .55f, color, .55f); material.SetFloat("_Metallic", .45f);
                material.SetFloat("_RoughnessScale", 1.1f);
                material.SetFloat("_SurfaceClass", 3); material.SetFloat("_Parallax", .009f); break;
            case "Wood": case "OiledTimber":
                Apply(material, "fine_grained_wood", 1.2f, new Color(1.22f, 1.13f, 1.03f), .65f);
                material.SetFloat("_RoughnessScale", .82f); material.SetFloat("_UVRotation", 1); break;
            case "Mulch": Apply(material, "gravel_floor_02", 1.5f, new Color(.37f, .33f, .23f), .8f); break;
            case "Foundation": case "TurquoisePlinth": case "DarkPlinth":
                ApplyPlinth(material, color); break;
            case "ContainerGreen": case "EntranceSteel": case "GraphiteMetal": case "Metal": case "BalconyIron": case "ManholeMetal":
                Apply(material, "green_metal_rust", 1.3f, color, .4f); material.SetFloat("_Colorize", 1);
                material.SetFloat("_Metallic", Mathf.Min(.55f, metallic)); material.SetFloat("_RoughnessScale", .8f); break;
            case "Glass": case "Glass0": case "Glass1": case "Glass2": case "Glass3":
                ApplyGlass(material, key.Length > 5 ? key[5] - '0' : 0); break;
            case "VehicleGlass": ApplyGlass(material, 0, false); break;
            case "Tire": case "Rubber": Own(material, "Rubber", .32f, color); break;
            case "VehicleNavy": case "VehicleSilver": case "VehicleBurgundy":
                Own(material, "Paint", .45f, color, .12f);
                material.SetFloat("_Metallic", .52f); material.SetFloat("_RoughnessScale", .42f);
                material.SetFloat("_Weathering", .14f); break;
            case "RoadPaint":
                Own(material, "Paint", .3f, color, .38f);
                material.SetFloat("_SurfaceClass", 5); material.SetFloat("_Weathering", .08f); break;
            default:
                // Even painted trim, signs, car bodies and small hardware now have real normal/roughness detail.
                Own(material, "Paint", .6f, color, .35f);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_RoughnessBias", .5f - smoothness);
                material.SetColor("_EmissionColor", emission);
                break;
        }
    }

    private static void Own(Material material, string asset, float meters, Color tint, float normal = .6f) =>
        ApplyMaps(material, "Assets/Art/Textures/" + asset, meters, tint, normal);

    private static void AsphaltFinish(Material material, float roughnessScale, float roughnessBias, float occlusion)
    {
        // Grain stays photographic. The two added scans have distinct normals and roughness,
        // rather than recolouring the same cracked texture three times.
        material.SetFloat("_SurfaceClass", 4);
        material.SetFloat("_RoughnessScale", roughnessScale);
        material.SetFloat("_RoughnessBias", roughnessBias);
        material.SetFloat("_OcclusionStrength", occlusion);
        material.SetFloat("_Parallax", .001f);
        material.SetFloat("_Weathering", .10f);
        material.SetFloat("_Metallic", 0);
        EditorUtility.SetDirty(material);
    }

    internal static void ApplyFacade(Material material, bool cream)
    {
        string asset = cream ? "brick_wall_003" : "red_brick";
        var source = facadeSources[asset];
        // Both photographs span six full bricks; row counts differ. Match a 260 x 75 mm course,
        // rather than stretching the two square images to the same metres-per-tile value.
        float width = 6 * .26f, height = source.dominant_horizontal_courses * .075f;
        // The reference has flat, close-jointed pale facing brick, not white stone blocks.
        // Correct the scan's brown cast without the previous >1.4 boost in every channel;
        // retain photographic joints but suppress its coarse speckles and inflated edge normals.
        ApplyMaps(material, Sources + "Facades4K/" + asset, width,
            cream ? new Color(1.15f, 1.35f, 1.48f) : new Color(.86f, .78f, .79f), cream ? .07f : .12f, height, .90f);
        material.SetFloat("_SurfaceClass", 2);
        material.SetFloat("_Parallax", .0007f);
        material.SetFloat("_Weathering", .10f);
        material.SetFloat("_OcclusionStrength", cream ? .14f : .20f);
        material.SetFloat("_AlbedoContrast", cream ? .60f : .74f);
        material.SetVector("_AlbedoMean", new Vector4(source.linear_albedo_mean[0], source.linear_albedo_mean[1], source.linear_albedo_mean[2], 0));
        material.SetFloat("_RoughnessScale", .38f);
        material.SetFloat("_RoughnessBias", .60f);
        EditorUtility.SetDirty(material);
    }

    private static void ApplyPlinth(Material material, Color color)
    {
        // Painted/rendered plinths have a fine matte grain, without centimetre-scale plaster relief.
        Apply(material, "grey_plaster", 1.5f, color, .22f);
        material.SetFloat("_Colorize", 1);
        material.SetFloat("_Parallax", .0008f);
        material.SetFloat("_RoughnessScale", .42f); material.SetFloat("_RoughnessBias", .56f);
        material.SetFloat("_OcclusionStrength", .30f); material.SetFloat("_Weathering", .18f);
        EditorUtility.SetDirty(material);
    }

    internal static void ApplyGlass(Material material, int variant, bool interior = true)
    {
        material.shader = Shader.Find("Street/Glass");
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Windows/Interiors.jpg"));
        material.SetTexture("_DirtTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/GlassDirt/Albedo.jpg"));
        material.SetTexture("_SkyTex", Texture("kloofendal_48d_partly_cloudy_puresky", "Sky", "hdr"));
        material.SetFloat("_SkyRotation", 110);
        material.SetFloat("_SkyExposure", .82f);
        material.SetColor("_SkyTint", new Color(.5f, .5f, .5f, 1));
        material.SetColor("_Color", new Color(.78f, .85f, .88f));
        material.SetFloat("_Variant", variant); material.SetFloat("_Interior", interior ? .58f : 0);
        material.SetFloat("_Cull", 2); material.enableInstancing = true; EditorUtility.SetDirty(material);
    }

    internal static void ApplyHouse(Material material, string name)
    {
        Color color = material.GetColor("_Color");
        if (name.Contains("Стекло")) ApplyGlass(material, name[name.Length - 1] % 4);
        else if (name.Contains("Кирпич")) ApplyFacade(material, !(name.Contains("красно") || name.Contains("терракота")));
        else if (name.Contains("Кровля")) ApplyNamed(material, "GreenStandingSeam");
        else if (name.Contains("цоколя")) ApplyPlinth(material, color * new Color(.82f, .82f, .82f));
        else if (name.Contains("Бетон")) ApplyNamed(material, "Concrete");
        else if (name.Contains("штор") || name.Contains("Шторы")) Own(material, "Linen", .4f, color);
        else if (name.Contains("дверь")) ApplyNamed(material, "EntranceSteel");
    }

    internal static Material ModelBark(bool branch)
    {
        var material = EnvironmentMesh.Material(branch ? "ModelBranches" : "ModelTrunk", Color.white);
        Apply(material, branch ? "tree_small_02_branch" : "tree_small_02_trunk", 1, Color.white, .7f);
        material.SetFloat("_UseUV", 1); return material;
    }

    internal static Material ModelLeaves()
    {
        var material = Foliage("ModelLeaves", "tree_small_02", Color.white);
        material.SetTexture("_MainTex", Texture("Graphics2K/tree_small_02", "Leaves", "png"));
        material.SetTexture("_BumpMap", Texture("Graphics2K/tree_small_02", "LeavesNormal", "png"));
        material.SetFloat("_BumpStrength", 0);
        return material;
    }

    internal static Material GrassBlades()
    {
        var material = Foliage("LawnBlades", "tree_small_02", new Color(.25f, .36f, .15f));
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Grass/Blade.png"));
        material.SetFloat("_BumpStrength", 0);
        material.SetFloat("_Grass", 1);
        material.SetFloat("_WindStrength", .075f);
        material.SetFloat("_Translucency", .22f);
        return material;
    }

    internal static Material Foliage(string name, string atlas, Color tint)
    {
        string path = EnvironmentMesh.Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("Street/Foliage"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = Shader.Find("Street/Foliage");
        string patch = atlas == "fir_tree_01" ? "Sprig" : "Leaf";
        material.SetTexture("_MainTex", Texture(atlas, patch, "png"));
        material.SetTexture("_BumpMap", Texture(atlas, patch + "Normal", "png"));
        material.SetColor("_Color", tint);
        material.SetFloat("_Cutoff", atlas == "fir_tree_01" ? .28f : .45f);
        material.SetFloat("_BumpStrength", .35f);
        material.SetFloat("_Translucency", .32f);
        material.SetFloat("_WindStrength", atlas == "fir_tree_01" ? .035f : .065f);
        material.SetFloat("_Grass", 0);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    internal static Material Bark()
    {
        var material = EnvironmentMesh.Material("SummerBark", Color.white, 0, .1f);
        Apply(material, "bark_brown_02", 1.5f, new Color(.83f, .83f, .77f));
        return material;
    }
}
