using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class PaintedDistrictSurfaces
{
    private const string Art = "Assets/Art/PaintedDistrict";
    private const string Materials = Art + "/Materials";
    private const string Textures = Art + "/Textures";

    private enum Finish
    {
        None, Road, Drive, Footpath, Concrete, RoadKerb, WalkEdge, Soil, Lawn, Paver,
        RedBrick, CreamBrick, Stucco, Paint, Bark, Timber, Roof, Woodland, Playground
    }

    private static readonly Color[] EntryColours =
    {
        new Color(.35f, .48f, .40f), new Color(.32f, .44f, .48f), new Color(.51f, .35f, .29f),
        new Color(.47f, .47f, .33f), new Color(.37f, .40f, .49f), new Color(.52f, .40f, .31f)
    };

    private static readonly Color[] PlinthColours =
    {
        new Color(.53f, .61f, .49f), new Color(.49f, .60f, .60f), new Color(.56f, .51f, .46f),
        new Color(.61f, .58f, .46f), new Color(.52f, .54f, .57f), new Color(.63f, .54f, .46f)
    };

    private sealed class Maps
    {
        public Texture2D albedo, normal, mask;
        public Vector4 mean;
    }

    private struct Sample
    {
        public Color colour;
        public float height, roughness, occlusion;
    }

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before applying painted surfaces.");

        Directory.CreateDirectory(Materials);
        Directory.CreateDirectory(Textures);
        AssetDatabase.Refresh();
        Physics.SyncTransforms();

        var maps = new Dictionary<Finish, Maps>();
        var variants = new Dictionary<(Material, Finish, int), Material>();
        var network = UnityEngine.Object.FindObjectsByType<StreetRoadNetwork>(FindObjectsInactive.Exclude)
            .FirstOrDefault(value => value.gameObject.scene == scene);
        var road = network && network.surfaces.Length > 0 ? network.surfaces[0] : null;
        int renderersChanged = 0, kerbTriangles = 0, edgeTriangles = 0, addressedRenderers = 0;

        Material Variant(Material input, Finish finish, int address)
        {
            var key = (input, finish, address);
            if (variants.TryGetValue(key, out var material))
                return material;

            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(input, out string guid, out long id))
                throw new InvalidOperationException("Cannot save a painted variant of " + input.name);

            string path = $"{Materials}/{guid}_{id}_{finish}_{address}.mat";
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material)
                EditorUtility.CopySerialized(input, material);
            else
            {
                material = new Material(input);
                AssetDatabase.CreateAsset(material, path);
            }
            material.name = input.name;
            Configure(material, finish, address, maps);
            EditorUtility.SetDirty(material);
            variants.Add(key, material);
            return material;
        }

        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (renderer.gameObject.scene != scene)
                continue;
            var address = Address(renderer);
            var materials = renderer.sharedMaterials;
            bool changed = false;
            bool addressed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var input = materials[i];
                if (!input || !input.shader ||
                    AssetDatabase.GetAssetPath(input).StartsWith(Materials + "/", StringComparison.Ordinal))
                    continue;
                var finish = Classify(input);
                if (finish == Finish.None)
                    continue;

                if (road && (finish is Finish.RoadKerb or Finish.WalkEdge) && IsMainKerb(renderer))
                {
                    if (TrySplitMainKerb(renderer, road, i, out int roads, out int walks))
                    {
                        Array.Resize(ref materials, materials.Length + 1);
                        materials[^1] = Variant(input, Finish.WalkEdge, 0);
                    }
                    if (roads > 0 || walks > 0)
                        finish = roads == 0 ? Finish.WalkEdge : Finish.RoadKerb;
                    kerbTriangles += roads;
                    edgeTriangles += walks;
                }

                int palette = finish is Finish.Paint or Finish.Stucco or Finish.RedBrick or Finish.CreamBrick
                    or Finish.Roof or Finish.Timber ? address : 0;
                materials[i] = Variant(input, finish, palette);
                addressed |= palette != 0;
                changed = true;
            }
            if (!changed)
                continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
            renderersChanged++;
            if (addressed)
                addressedRenderers++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"PAINTED_DISTRICT_SURFACES families={maps.Count} materials={variants.Count} " +
            $"renderers={renderersChanged} addressedRenderers={addressedRenderers} roadKerbTriangles={kerbTriangles} " +
            $"walkEdgeTriangles={edgeTriangles}");
    }

    private static int Address(MeshRenderer renderer)
    {
        var building = renderer.GetComponentInParent<StreetBuilding>(true);
        if (!building || string.IsNullOrEmpty(building.address))
            return 0;
        string address = building.address;
        int comma = address.LastIndexOf(',');
        if (comma >= 0)
            address = address[(comma + 1)..].Trim();
        if (int.TryParse(address, out int number))
            return Mathf.Abs(number);
        // Keep named addresses deterministic even when they have no decimal house number.
        uint hash = 2166136261u;
        foreach (char character in address)
            hash = unchecked((hash ^ character) * 16777619u);
        return (int)(hash % 997) + 1;
    }

    private static bool IsMainKerb(MeshRenderer renderer)
    {
        var filter = renderer.GetComponent<MeshFilter>();
        return filter && filter.sharedMesh && filter.sharedMesh.name.Contains("ReferenceKerbs", StringComparison.Ordinal);
    }

    private static bool TrySplitMainKerb(MeshRenderer renderer, MeshCollider road, int submesh,
        out int roads, out int walks)
    {
        roads = walks = 0;
        var filter = renderer.GetComponent<MeshFilter>();
        var source = filter.sharedMesh;
        if (submesh >= source.subMeshCount || source.GetTopology(submesh) != MeshTopology.Triangles)
            return false;
        var vertices = source.vertices;
        var white = new List<int>();
        var muted = new List<int>();
        var indices = source.GetTriangles(submesh);
        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3 a = filter.transform.TransformPoint(vertices[indices[i]]);
            Vector3 b = filter.transform.TransformPoint(vertices[indices[i + 1]]);
            Vector3 c = filter.transform.TransformPoint(vertices[indices[i + 2]]);
            var centre = (a + b + c) / 3;
            Vector3 longest = Horizontal(b - a);
            Vector3 bc = Horizontal(c - b);
            Vector3 ca = Horizontal(a - c);
            if (bc.sqrMagnitude > longest.sqrMagnitude) longest = bc;
            if (ca.sqrMagnitude > longest.sqrMagnitude) longest = ca;
            var across = Vector3.Cross(Vector3.up, longest).normalized;
            bool BesideRoad(Vector3 point) =>
                road.Raycast(new Ray(point + Vector3.up * .65f, Vector3.down), out var hit, 1.1f) &&
                Mathf.Abs(hit.point.y - centre.y) < .34f;
            bool trafficEdge = BesideRoad(centre) ||
                BesideRoad(centre + across * .23f) || BesideRoad(centre - across * .23f);
            if (!trafficEdge && longest.sqrMagnitude < .0625f)
            {
                // At the short end caps the longest side runs across the kerb.
                var along = longest.normalized * .23f;
                trafficEdge = BesideRoad(centre + along) || BesideRoad(centre - along);
            }
            var destination = trafficEdge ? white : muted;
            destination.Add(indices[i]);
            destination.Add(indices[i + 1]);
            destination.Add(indices[i + 2]);
        }
        roads = white.Count / 3;
        walks = muted.Count / 3;
        if (roads == 0 || walks == 0)
            return false;

        // Split the render indices into two materials. Vertices and the collision asset stay in place.
        var split = UnityEngine.Object.Instantiate(source);
        split.name = source.name + "PaintedEdges";
        split.subMeshCount++;
        split.SetTriangles(white, submesh, false);
        split.SetTriangles(muted, split.subMeshCount - 1, false);
        const string path = Art + "/ReferenceKerbsPaintedEdges.asset";
        var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (saved)
        {
            EditorUtility.CopySerialized(split, saved);
            UnityEngine.Object.DestroyImmediate(split);
            EditorUtility.SetDirty(saved);
        }
        else
        {
            saved = split;
            AssetDatabase.CreateAsset(saved, path);
        }
        filter.sharedMesh = saved;
        EditorUtility.SetDirty(filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        return true;
    }

    private static Vector3 Horizontal(Vector3 vector) => new Vector3(vector.x, 0, vector.z);

    private static Finish Classify(Material material)
    {
        string name = material.name;
        switch (material.shader.name)
        {
            case "Street/WoodlandGround": return Finish.Woodland;
            case "Street/PlaygroundSoil": return Finish.Playground;
            case "Street/TexturedSurface": break;
            default: return Finish.None;
        }
        if (name is "ResidentialAccessEdge" or "VergeBlueEdge" or "Court7CoatedEdging" or "AsphaltArris")
            return Finish.WalkEdge;
        if (Has(name, "Kerb") || name == "Curb")
            return name is "Kerbstone" or "PhotoWhitewashedKerb" ? Finish.WalkEdge : Finish.RoadKerb;
        if (name == "Foundation" || Has(name, "Plinth"))
            return Finish.Stucco;
        if (Has(name, "Bark") || name is "ModelBranches" or "ModelTrunk")
            return Finish.Bark;

        string source = AssetDatabase.GetAssetPath(material.GetTexture("_MainTex"));
        if (Has(name, "Timber") || name is "Wood" or "No7BenchFadedBlue" or "No7BenchPaleSlat" ||
            source.Contains("/fine_grained_wood/", StringComparison.Ordinal))
            return Finish.Timber;
        if (name is "SoilAggregate" or "DryGravel" or "Mulch" or "WoodlandFloor")
            return Finish.Soil;
        if (Has(name, "Concrete") || Has(name, "Coping") || name is "Path" or "House5_17"
            or "Court7DoorSlate" or "Court7JambStone" or "LibraryParapetDarkEdge" or "MonumentGraniteStudy")
            return Finish.Concrete;
        switch (Mathf.RoundToInt(material.GetFloat("_SurfaceClass")))
        {
            case 1: return Finish.Lawn;
            case 2: return source.Contains("/brick_wall_003/", StringComparison.Ordinal) || Has(name, "Cream")
                ? Finish.CreamBrick : Finish.RedBrick;
            case 3: return Finish.Roof;
            case 4: return name is "ReferenceAsphalt" or "MainRoadAsphalt" ? Finish.Road :
                Has(name, "Footpath") ? Finish.Footpath : Finish.Drive;
            case 6: return Finish.Paver;
        }
        if (source.Contains("/Paint/", StringComparison.Ordinal) || source.Contains("/green_metal_rust/", StringComparison.Ordinal))
            return Finish.Paint;
        return Finish.None;
    }

    private static bool Has(string value, string part) => value.Contains(part, StringComparison.OrdinalIgnoreCase);

    private static void Configure(Material material, Finish finish, int address, Dictionary<Finish, Maps> cache)
    {
        var maps = GetMaps(finish, cache);
        material.SetTexture("_MainTex", maps.albedo);
        material.SetTexture("_BumpMap", maps.normal);
        if (material.HasProperty("_MaskMap"))
            material.SetTexture("_MaskMap", maps.mask);
        if (finish == Finish.Woodland)
        {
            material.SetTexture("_GrassTex", GetMaps(Finish.Lawn, cache).albedo);
            material.SetColor("_GrassTint", new Color(.79f, .84f, .80f));
            return;
        }
        if (finish == Finish.Playground)
        {
            material.SetColor("_Color", new Color(.95f, .88f, .77f));
            return;
        }

        string name = material.name;
        int palette = address == 0 ? 0 : (address - 1) % EntryColours.Length;
        Color original = material.GetColor("_Color");
        Color tint = original;
        float bump = .22f;
        float variation = .08f;
        float colorize = 0;
        switch (finish)
        {
            case Finish.Road:
            case Finish.Drive:
            case Finish.Footpath:
                tint = finish switch
                {
                    Finish.Drive => new Color(.69f, .74f, .82f),
                    Finish.Footpath => new Color(.78f, .82f, .87f),
                    _ => new Color(.93f, .95f, .97f)
                };
                bump = .09f;
                variation = .045f;
                break;
            case Finish.RoadKerb:
                tint = Color.white;
                material.SetVector("_TileMeters", new Vector4(1.32f, .66f, 0, 0));
                material.SetFloat("_SurfaceClass", 0);
                break;
            case Finish.WalkEdge:
                tint = Color.white;
                material.SetFloat("_SurfaceClass", 0);
                break;
            case Finish.Concrete:
                tint = Color.Lerp(original, Color.white, .55f);
                material.SetFloat("_SurfaceClass", 0);
                break;
            case Finish.Lawn:
                bool maintained = name == "CourtMaintainedGrass";
                tint = maintained ? new Color(.82f, .87f, .79f) : new Color(.76f, .81f, .76f);
                material.SetTexture("_DryGroundTex", GetMaps(Finish.Soil, cache).albedo);
                material.SetFloat("_MeadowDrying", maintained ? .24f : name == "RoadVergeSurface" ? .54f : .43f);
                material.SetVector("_MeadowSoilRange", new Vector4(.097f, .20f, .36f, .72f));
                variation = .13f;
                break;
            case Finish.Soil:
                tint = Color.white;
                variation = .12f;
                break;
            case Finish.Paver:
                bool grey = Has(name, "Grey");
                var warm = new Vector4(.29f, .122f, .080f, 0);
                var stone = new Vector4(.227f, .218f, .194f, 0);
                material.SetVector("_PaverRedTone", grey ? stone : warm);
                material.SetVector("_PaverGreyTone", stone);
                material.SetVector("_PaverFarTone", grey ? stone : Vector4.Lerp(warm, stone, .26f));
                material.SetVector("_PaverFinish", new Vector4(.0015f, .75f, .55f, 0));
                tint = Color.white;
                bump = .14f;
                break;
            case Finish.CreamBrick:
            case Finish.RedBrick:
                // Six bricks by 16 or 20 courses per tile, keeping the existing scale of the facade.
                var tile = material.GetVector("_TileMeters");
                if (name == "DarkRedRetainingBrick")
                    tile = new Vector4(1.56f, 1.50f, 0, 0);
                tile.y = finish == Finish.CreamBrick ? 1.20f : 1.50f;
                material.SetVector("_TileMeters", tile);
                tint = address == 0 ? Color.Lerp(original, Color.white, .65f) :
                    Color.Lerp(Color.white, EntryColours[palette], finish == Finish.CreamBrick ? .19f : .14f);
                if (name == "DarkRedRetainingBrick")
                    tint *= new Color(.79f, .76f, .77f, 1);
                bump = .24f;
                variation = .10f;
                break;
            case Finish.Stucco:
                tint = address == 0 ? Color.Lerp(original, Color.white, .18f) : PlinthColours[palette];
                colorize = .60f;
                break;
            case Finish.Paint:
                colorize = .67f;
                bump = .11f;
                if (address != 0 && (Has(name, "EntranceSteel") || Has(name, "DoorPaint") || Has(name, "Canopy")))
                    tint = EntryColours[palette];
                else if (address != 0 && Has(name, "Balcony"))
                    tint = Color.Lerp(original, EntryColours[palette], .48f);
                break;
            case Finish.Bark:
                tint = Color.Lerp(original, Color.white, .45f);
                variation = .12f;
                break;
            case Finish.Timber:
                tint = Color.Lerp(original, Color.white, .32f);
                break;
            case Finish.Roof:
                tint = Color.Lerp(original, Color.white, .34f);
                colorize = .35f;
                break;
        }

        material.SetColor("_Color", tint);
        material.SetFloat("_Colorize", colorize);
        material.SetVector("_AlbedoMean", maps.mean);
        material.SetFloat("_AlbedoContrast", finish is Finish.Road or Finish.Drive or Finish.Footpath ? .63f : .87f);
        material.SetFloat("_Saturation", .97f);
        material.SetFloat("_BumpStrength", bump);
        material.SetFloat("_Parallax", .00025f);
        material.SetFloat("_RoughnessScale", .51f);
        material.SetFloat("_RoughnessBias", .45f);
        material.SetFloat("_OcclusionStrength", .22f);
        material.SetFloat("_PaintedVariation", variation);
        material.SetFloat("_Weathering", .12f);
    }

    private static Maps GetMaps(Finish finish, Dictionary<Finish, Maps> cache)
    {
        if (cache.TryGetValue(finish, out var maps))
            return maps;
        string folder = $"{Textures}/{finish}";
        Directory.CreateDirectory(folder);
        int size = finish is Finish.Road or Finish.Drive or Finish.Footpath or Finish.RedBrick or Finish.CreamBrick ? 1024 : 512;
        string albedo = folder + "/Albedo.png", normal = folder + "/Normal.png", mask = folder + "/Mask.png";
        if (!File.Exists(albedo) || !File.Exists(normal) || !File.Exists(mask))
            Generate(finish, size, albedo, normal, mask);
        maps = new Maps
        {
            albedo = Import(albedo, size, TextureImporterType.Default, true),
            normal = Import(normal, size, TextureImporterType.NormalMap, false),
            mask = Import(mask, size, TextureImporterType.Default, false)
        };
        Color mean = Tone(finish).linear;
        maps.mean = new Vector4(mean.r, mean.g, mean.b, 0);
        cache.Add(finish, maps);
        return maps;
    }

    private static Texture2D Import(string path, int size, TextureImporterType type, bool srgb)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer.textureType != type || importer.sRGBTexture != srgb || !importer.mipmapEnabled ||
            importer.wrapMode != TextureWrapMode.Repeat || importer.filterMode != FilterMode.Trilinear ||
            importer.anisoLevel != 4 || importer.maxTextureSize != size ||
            importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureType = type;
            importer.sRGBTexture = srgb;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.maxTextureSize = size;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void Generate(Finish finish, int size, string albedoPath, string normalPath, string maskPath)
    {
        var albedo = new Color32[size * size];
        var normal = new Color32[albedo.Length];
        var mask = new Color32[albedo.Length];
        var height = new float[albedo.Length];
        Color tone = Tone(finish);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = y * size + x;
            Sample sample = Paint(finish, tone, (x + .5f) / size, (y + .5f) / size);
            albedo[i] = sample.colour;
            height[i] = sample.height;
            mask[i] = new Color(sample.occlusion, sample.roughness, sample.height, 1);
        }
        float relief = finish is Finish.RedBrick or Finish.CreamBrick ? .012f : .007f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = height[y * size + (x + 1) % size] - height[y * size + (x + size - 1) % size];
            float dy = height[((y + 1) % size) * size + x] - height[((y + size - 1) % size) * size + x];
            Vector3 n = new Vector3(-dx * size * relief, -dy * size * relief, 2).normalized;
            normal[y * size + x] = new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f, 1);
        }
        Write(albedoPath, size, albedo);
        Write(normalPath, size, normal);
        Write(maskPath, size, mask);
    }

    private static Color Tone(Finish finish) => finish switch
    {
        Finish.Road => new Color(.36f, .36f, .37f),
        Finish.Drive => new Color(.43f, .40f, .37f),
        Finish.Footpath => new Color(.49f, .46f, .43f),
        Finish.WalkEdge => new Color(.47f, .445f, .415f),
        Finish.RoadKerb => new Color(.84f, .82f, .76f),
        Finish.Concrete => new Color(.72f, .68f, .61f),
        Finish.Lawn => new Color(.54f, .56f, .34f),
        Finish.Soil => new Color(.48f, .37f, .26f),
        Finish.Playground => new Color(.71f, .61f, .46f),
        Finish.Woodland => new Color(.61f, .48f, .30f),
        Finish.CreamBrick => new Color(.86f, .75f, .59f),
        Finish.RedBrick => new Color(.72f, .43f, .30f),
        Finish.Bark => new Color(.62f, .51f, .39f),
        Finish.Timber => new Color(.72f, .58f, .41f),
        Finish.Roof => new Color(.70f, .70f, .65f),
        _ => new Color(.76f, .74f, .68f)
    };

    private static Sample Paint(Finish finish, Color tone, float u, float v)
    {
        int seed = (int)finish * 169;
        float broad = Noise(u, v, 5, 7, seed) - .5f;
        float stroke = Noise(u, v, 8, 29, seed + 43) - .5f;
        float grains = Noise(u, v, 59, 73, seed + 83) - .5f;
        float height = .53f + broad * .11f + stroke * .10f;
        float value = 1 + broad * .22f + stroke * .09f + grains * .018f;
        float rough = .84f + broad * .075f;
        float ao = .97f;
        switch (finish)
        {
            case Finish.CreamBrick:
            case Finish.RedBrick:
            {
                int courses = finish == Finish.CreamBrick ? 16 : 20;
                float row = v * courses;
                int course = Mathf.FloorToInt(row);
                float column = u * 6 + (course % 2) * .5f;
                int brick = Mathf.FloorToInt(column) % 6;
                float x = column - Mathf.Floor(column);
                float y = row - course;
                float distX = Mathf.Min(x, 1 - x) * 3.48f;
                float distY = Mathf.Min(y, 1 - y);
                float edge = Mathf.Min(distX, distY) + stroke * .053f;
                float body = Smooth(.034f, .10f, edge);
                float firing = Hash(brick, course, seed) - .5f;
                Color mortar = finish == Finish.CreamBrick ? new Color(.64f, .59f, .52f) : new Color(.58f, .51f, .43f);
                tone = Color.Lerp(mortar, tone * (1 + firing * .22f), body);
                height = .41f + body * (.15f + stroke * .07f);
                rough = Mathf.Lerp(.96f, .82f, body);
                ao = Mathf.Lerp(.79f, .98f, body);
                value += .04f * (1 - Smooth(.11f, .24f, y)) * body;
                break;
            }
            case Finish.RoadKerb:
            {
                float worn = Smooth(.61f, .79f, Noise(u, v, 5, 5, 72) + stroke * .11f);
                float chip = Smooth(.68f, .84f, Noise(u, v, 24, 19, 90) + broad * .26f);
                float crack = Crack(u, v);
                tone = Color.Lerp(tone, new Color(.59f, .58f, .54f), Mathf.Max(worn * .72f, chip * .73f));
                value *= 1 - crack * .31f;
                height -= chip * .10f + crack * .18f;
                rough += chip * .065f;
                ao -= crack * .11f;
                break;
            }
            case Finish.Road:
            case Finish.Drive:
            case Finish.Footpath:
            case Finish.WalkEdge:
            {
                float dust = Smooth(.52f, .72f, Noise(u, v, 9, 11, seed + 9));
                float aggregate = Smooth(.56f, .78f, Noise(u, v, 44, 53, seed + 7));
                value += dust * .064f + aggregate * .043f;
                height += aggregate * .055f;
                rough += .057f;
                break;
            }
            case Finish.Lawn:
            {
                float dry = Smooth(.36f, .80f, Noise(u, v, 11, 9, seed + 111) + broad * .28f);
                tone = Color.Lerp(tone, new Color(.68f, .59f, .36f), dry * .72f);
                float blades = Smooth(.56f, .73f, Noise(u, v, 44, 20, seed + 6));
                value += blades * .075f;
                height += blades * .09f;
                rough += .11f;
                break;
            }
            case Finish.Bark:
            case Finish.Timber:
            {
                float grain = Noise(u, v, 36, 4, seed + 9);
                float ridge = Smooth(.43f, .67f, grain + stroke * .19f);
                tone = Color.Lerp(tone * .72f, tone * 1.09f, ridge);
                height += ridge * (finish == Finish.Bark ? .23f : .10f);
                ao -= (1 - ridge) * .11f;
                break;
            }
            case Finish.Soil:
            case Finish.Woodland:
            case Finish.Playground:
            {
                float scumble = Smooth(.32f, .75f, Noise(u, v, 14, 17, seed + 91));
                tone = Color.Lerp(tone * .86f, new Color(tone.r * 1.16f, tone.g * 1.14f, tone.b * 1.06f), scumble);
                float gravel = Smooth(.62f, .81f, Noise(u, v, 35, 47, seed + 2));
                value += gravel * .095f;
                height += gravel * .10f;
                rough += .075f;
                break;
            }
            case Finish.Roof:
            {
                float metal = Noise(u, v, 13, 3, seed + 8) - .5f;
                value += metal * .13f;
                height += metal * .06f;
                break;
            }
            default:
            {
                float trowel = Noise(u, v, 21, 8, seed + 24) - .5f;
                float deposit = Smooth(.61f, .81f, Noise(u, v, 42, 37, seed + 1));
                value += trowel * .082f - deposit * .065f;
                height += trowel * .075f - deposit * .05f;
                break;
            }
        }
        return new Sample { colour = tone * value, height = height, roughness = rough, occlusion = ao };
    }

    private static float Crack(float u, float v)
    {
        // Two short, tapered hairlines sit inside the tile and do not cross the repeat seam.
        float line = .27f + (u - .2f) * .32f + .016f * Mathf.Sin(u * 45) + .008f * Mathf.Sin(u * 73);
        float extent = Smooth(.16f, .24f, u) * (1 - Smooth(.52f, .62f, u));
        float main = extent * (1 - Smooth(.0009f, .005f, Mathf.Abs(v - line)));
        float branch = .34f - (u - .37f) * .68f + .008f * Mathf.Sin(u * 57);
        float fork = Smooth(.33f, .40f, u) * (1 - Smooth(.49f, .54f, u)) *
            (1 - Smooth(.0008f, .004f, Mathf.Abs(v - branch)));
        return Mathf.Max(main, fork * .72f);
    }

    private static float Smooth(float start, float end, float value) =>
        Mathf.SmoothStep(0, 1, Mathf.InverseLerp(start, end, value));

    private static float Noise(float u, float v, int width, int height, int seed)
    {
        float x = u * width, y = v * height;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float tx = x - ix, ty = y - iy;
        tx = tx * tx * (3 - 2 * tx);
        ty = ty * ty * (3 - 2 * ty);
        int nx = (ix + 1) % width, ny = (iy + 1) % height;
        return Mathf.Lerp(Mathf.Lerp(Hash(ix, iy, seed), Hash(nx, iy, seed), tx),
            Mathf.Lerp(Hash(ix, ny, seed), Hash(nx, ny, seed), tx), ty);
    }

    private static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
    }

    private static void Write(string path, int size, Color32[] colours)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGB24, false);
        texture.SetPixels32(colours);
        File.WriteAllBytes(path, texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
    }
}
