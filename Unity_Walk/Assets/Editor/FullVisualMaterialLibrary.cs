using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class FullVisualMaterialLibrary
{
    private const string Folder = "Assets/Art/FullVisual/Materials";
    private enum Surface { Other, Concrete, Stucco, Timber, Bark, Brick, Asphalt, Paver, Ground, Painted, Roof }

    [Serializable] private sealed class Binding
    {
        public string name, family, albedo, normal, mask;
        public Vector4 metresPerTile;
        public bool sourceUV;
    }

    [Serializable] private sealed class Manifest
    {
        public string description = "Unified material bindings for the main saved Street scene.";
        public string authoredTextures = "Textures/PROVENANCE.json";
        public string retainedScans = "Assets/ThirdParty/PolyHaven/LICENSES.md";
        public Binding[] bindings;
    }

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved main Street scene before installing the visual library.");

        FullVisualTextureBuilder.Prepare();
        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var replacements = new Dictionary<Material, Material>();
        var bindings = new List<Binding>();
        int changed = 0, authored = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (renderer.gameObject.scene != scene)
                continue;
            var materials = renderer.sharedMaterials;
            bool assigned = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var current = materials[i];
                if (!current || !current.shader || current.shader.name != "Street/TexturedSurface")
                    continue;
                if (!replacements.TryGetValue(current, out var material))
                {
                    var source = SourceMaterial(current);
                    Surface surface = Classify(source);
                    material = current;
                    if (surface != Surface.Other &&
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id))
                    {
                        string path = $"{Folder}/{guid}_{id}.mat";
                        material = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (material)
                            EditorUtility.CopySerialized(source, material);
                        else
                        {
                            material = new Material(source);
                            AssetDatabase.CreateAsset(material, path);
                        }
                        material.name = source.name;
                        string family = Finish(material, surface);
                        if (family != "retained PBR maps") authored++;
                        EditorUtility.SetDirty(material);
                        bindings.Add(new Binding
                        {
                            name = material.name, family = surface + " / " + family,
                            albedo = AssetDatabase.GetAssetPath(material.GetTexture("_MainTex")),
                            normal = AssetDatabase.GetAssetPath(material.GetTexture("_BumpMap")),
                            mask = AssetDatabase.GetAssetPath(material.GetTexture("_MaskMap")),
                            metresPerTile = material.GetVector("_TileMeters"),
                            sourceUV = material.GetFloat("_UseUV") > .5f
                        });
                    }
                    replacements.Add(current, material);
                }
                if (current == material)
                    continue;
                materials[i] = material;
                assigned = true;
            }
            if (!assigned)
                continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
            changed++;
        }
        string manifestPath = "Assets/Art/FullVisual/BINDINGS.json";
        File.WriteAllText(manifestPath, JsonUtility.ToJson(new Manifest { bindings = bindings.ToArray() }, true));
        AssetDatabase.ImportAsset(manifestPath);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"FULL_VISUAL_LIBRARY materials={bindings.Count} authoredTextureBindings={authored} renderers={changed}");
    }

    private static Material SourceMaterial(Material current)
    {
        string path = AssetDatabase.GetAssetPath(current);
        if (!path.StartsWith(Folder + "/", StringComparison.Ordinal))
            return current;
        string[] parts = Path.GetFileNameWithoutExtension(path).Split('_');
        if (parts.Length != 2 || !long.TryParse(parts[1], out long expected))
            throw new InvalidOperationException("Unrecognized full visual material " + path);
        var source = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(parts[0]))
            .OfType<Material>().FirstOrDefault(material =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out _, out long id) && id == expected);
        if (!source)
            throw new InvalidOperationException("Missing original material for " + path);
        return source;
    }

    private static Surface Classify(Material material)
    {
        string name = material.name;
        string albedo = AssetDatabase.GetAssetPath(material.GetTexture("_MainTex"));
        // Named concrete remains concrete even when an earlier pass classified it as paving.
        if (name.Contains("Concrete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Kerb", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Coping", StringComparison.Ordinal) || name is "Curb" or "ResidentialAccessEdge")
            return Surface.Concrete;
        if (name.Contains("Plinth", StringComparison.Ordinal) || name == "Foundation" || albedo.Contains("/grey_plaster/"))
            return Surface.Stucco;
        if (name.Contains("Bark", StringComparison.Ordinal) || name is "ModelTrunk" or "ModelBranches")
            return Surface.Bark;
        if (name.Contains("Timber", StringComparison.Ordinal) || name == "Wood" || albedo.Contains("/fine_grained_wood/"))
            return Surface.Timber;
        switch (Mathf.RoundToInt(material.GetFloat("_SurfaceClass")))
        {
            case 1: return Surface.Ground;
            case 2: return Surface.Brick;
            case 3: return Surface.Roof;
            case 4: return Surface.Asphalt;
            case 6: return Surface.Paver;
        }
        if (albedo.Contains("/concrete_wall_006/") || albedo.Contains("/concrete_pavement/"))
            return Surface.Concrete;
        return albedo.Contains("/Paint/") || albedo.Contains("/green_metal_rust/") ? Surface.Painted : Surface.Other;
    }

    private static string Finish(Material material, Surface surface)
    {
        switch (surface)
        {
            case Surface.Concrete:
                Maps(material, "CurbConcrete", new Vector4(.425f, .408f, .376f, 0));
                material.SetFloat("_SurfaceClass", 0);
                material.SetFloat("_Parallax", .0006f);
                SurfaceFinish(material, .20f, .64f, .21f, .67f, .17f);
                return "CurbConcrete";
            case Surface.Stucco:
                Maps(material, "SoftStucco", new Vector4(.420f, .410f, .387f, 0));
                material.SetFloat("_Parallax", .00045f);
                SurfaceFinish(material, .17f, .59f, .19f, .69f, .16f);
                return "SoftStucco";
            case Surface.Timber:
                ScanMaps(material, "fine_grained_wood");
                material.SetFloat("_Parallax", .0008f);
                SurfaceFinish(material, .25f, .66f, .32f, .52f, .21f);
                break;
            case Surface.Bark:
                ScanMaps(material, material.name == "ModelBranches" ? "tree_small_02_branch" :
                    material.name == "ModelTrunk" ? "tree_small_02_trunk" : "bark_brown_02");
                material.SetFloat("_Parallax", .0014f);
                SurfaceFinish(material, .42f, .70f, .24f, .69f, .34f);
                break;
            case Surface.Brick:
                material.SetFloat("_Saturation", Mathf.Max(.90f, material.GetFloat("_Saturation")));
                material.SetFloat("_Parallax", .00035f);
                SurfaceFinish(material, .065f, .41f, .25f, .66f, .16f);
                break;
            case Surface.Asphalt:
                material.SetFloat("_Parallax", .00045f);
                SurfaceFinish(material, .11f, .46f, .24f, .68f, .20f);
                break;
            case Surface.Paver:
                material.SetFloat("_Parallax", .00045f);
                SurfaceFinish(material, .12f, .50f, .26f, .65f, .23f);
                break;
            case Surface.Ground:
                material.SetFloat("_Parallax", .00065f);
                SurfaceFinish(material, .15f, .59f, .26f, .68f, .21f);
                break;
            case Surface.Painted:
                material.SetFloat("_Parallax", .00035f);
                SurfaceFinish(material, .095f, .52f, .30f, .49f, .18f);
                break;
            case Surface.Roof:
                material.SetFloat("_Parallax", .0022f);
                SurfaceFinish(material, .28f, .55f, .37f, .46f, .25f);
                break;
        }
        return "retained PBR maps";
    }

    private static void Maps(Material material, string family, Vector4 mean)
    {
        foreach (var map in new[] { ("_MainTex", "Albedo"), ("_BumpMap", "Normal"), ("_MaskMap", "Mask") })
        {
            string path = $"{FullVisualTextureBuilder.Root}/{family}/{map.Item2}.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture)
                throw new InvalidOperationException("Missing authored PBR map " + path);
            material.SetTexture(map.Item1, texture);
        }
        material.SetVector("_AlbedoMean", mean);
    }

    private static void ScanMaps(Material material, string family)
    {
        foreach (var map in new[] { ("_MainTex", "Albedo", "jpg"), ("_BumpMap", "Normal", "jpg"), ("_MaskMap", "Mask", "png") })
        {
            string path = $"Assets/ThirdParty/PolyHaven/Graphics2K/{family}/{map.Item2}.{map.Item3}";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (!texture)
                throw new InvalidOperationException("Missing retained PBR scan " + path);
            material.SetTexture(map.Item1, texture);
        }
        material.SetVector("_AlbedoMean", new Vector4(.3f, .3f, .3f, 0));
    }

    private static void SurfaceFinish(Material material, float bump, float contrast, float roughnessScale,
        float roughnessBias, float occlusion)
    {
        material.SetFloat("_BumpStrength", bump);
        material.SetFloat("_AlbedoContrast", contrast);
        material.SetFloat("_RoughnessScale", roughnessScale);
        material.SetFloat("_RoughnessBias", roughnessBias);
        material.SetFloat("_OcclusionStrength", occlusion);
    }
}
