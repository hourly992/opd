using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Material-only changes to the saved district. Existing textures, UV scale, pavement
// pattern, plant coverage and scene geometry are retained.
internal static class DistrictLeafPalette
{
    private const string Folder = "Assets/PhotoReconstruction/Stage16LeafStyle/Materials";

    private enum Finish
    {
        Other, Brick, Road, Drive, Footpath, Paver, Grass, ForestFloor, Cement, Plinth, Timber,
        Broadleaf, Birch, Hedge, Needles, Blades
    }

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != LeafCourtyardStudy.ScenePath)
            throw new InvalidOperationException("Open the saved Street scene before applying its leaf palette.");

        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var variants = new Dictionary<Material, Material>();
        int changedRenderers = 0, tuned = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (renderer.gameObject.scene != scene)
                continue;
            if (!LeafCourtyardStudy.InStudyArea(renderer.bounds))
                continue;

            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var current = materials[i];
                if (!current || !current.shader)
                    continue;
                if (!variants.TryGetValue(current, out var variant))
                {
                    var source = OriginalMaterial(current);
                    var finish = Classify(source);
                    variant = current;
                    if (finish != Finish.Other && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
                    {
                        string path = $"{Folder}/{guid}_{localId}.mat";
                        variant = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (variant)
                            EditorUtility.CopySerialized(source, variant);
                        else
                        {
                            variant = new Material(source);
                            AssetDatabase.CreateAsset(variant, path);
                        }
                        variant.name = source.name;
                        Style(variant, finish);
                        EditorUtility.SetDirty(variant);
                        tuned++;
                    }
                    variants.Add(current, variant);
                }
                if (variant == current)
                    continue;
                materials[i] = variant;
                changed = true;
            }
            if (!changed)
                continue;

            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            changedRenderers++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"DISTRICT_LEAF_PALETTE variants={tuned} reassignedRenderers={changedRenderers}");
    }

    private static Material OriginalMaterial(Material current)
    {
        var path = AssetDatabase.GetAssetPath(current);
        if (!path.StartsWith(Folder + "/", StringComparison.Ordinal))
            return current;
        var key = Path.GetFileNameWithoutExtension(path).Split('_');
        if (key.Length != 2 || !long.TryParse(key[1], out long localId))
            throw new InvalidOperationException($"Unknown leaf palette source for {path}.");
        var original = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(key[0]))
            .OfType<Material>().FirstOrDefault(material =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material, out _, out long id) && id == localId);
        if (!original)
            throw new InvalidOperationException($"Missing leaf palette source for {path}.");
        return original;
    }

    private static Finish Classify(Material material)
    {
        string name = material.name;
        if (material.shader.name == "Street/Foliage")
        {
            if (name.StartsWith("ModelLeaves", StringComparison.Ordinal)) return Finish.Broadleaf;
            if (name.StartsWith("BirchLeaves", StringComparison.Ordinal)) return Finish.Birch;
            if (name.StartsWith("HedgeLeaves", StringComparison.Ordinal) || name == "MeadowClover") return Finish.Hedge;
            if (name.StartsWith("LawnBlades", StringComparison.Ordinal)) return Finish.Blades;
            if (name == "SpruceNeedles") return Finish.Needles;
            return Finish.Other;
        }
        if (material.shader.name != "Street/TexturedSurface")
            return Finish.Other;

        switch (Mathf.RoundToInt(material.GetFloat("_SurfaceClass")))
        {
            case 1: return name is "WoodlandFloor" or "Mulch" ? Finish.ForestFloor : Finish.Grass;
            case 2: return Finish.Brick;
            case 4:
                if (name is "ReferenceAsphalt" or "MainRoadAsphalt") return Finish.Road;
                return name.Contains("Footpath", StringComparison.OrdinalIgnoreCase) ? Finish.Footpath : Finish.Drive;
            case 6: return Finish.Paver;
        }
        if (name.Contains("Plinth", StringComparison.Ordinal) || name == "Foundation") return Finish.Plinth;
        if (name.Contains("Concrete", StringComparison.Ordinal) || name.Contains("Kerb", StringComparison.Ordinal) ||
            name is "Curb" or "Path" or "DryGravel" or "ResidentialAccessEdge") return Finish.Cement;
        if (name is "OiledTimber" or "BenchTimber" or "Wood") return Finish.Timber;
        return Finish.Other;
    }

    private static void Style(Material material, Finish finish)
    {
        switch (finish)
        {
            case Finish.Brick:
            {
                Color original = material.GetColor("_Color");
                bool warmBrick = original.r > original.g * 1.05f;
                material.SetColor("_Color", original * (warmBrick
                    ? new Color(.98f, 1.035f, 1.11f, 1)
                    : new Color(1.05f, .97f, .90f, 1)));
                material.SetFloat("_Saturation", .91f);
                material.SetFloat("_Parallax", .00035f);
                Matte(material, .065f, .39f, .24f, .68f, .18f, .10f);
                break;
            }
            case Finish.Road:
            case Finish.Drive:
            case Finish.Footpath:
                material.SetColor("_Color", finish switch
                {
                    Finish.Road => new Color(.39f, .405f, .44f),
                    Finish.Footpath => new Color(.63f, .64f, .67f),
                    _ => new Color(.57f, .59f, .625f)
                });
                material.SetFloat("_Saturation", .35f);
                material.SetFloat("_Parallax", .0004f);
                Matte(material, .085f, .46f, .23f, .68f, .18f, .07f);
                break;
            case Finish.Paver:
            {
                var red = new Vector4(.131f, .054f, .037f, 0);
                var grey = new Vector4(.075f, .082f, .092f, 0);
                material.SetVector("_PaverRedTone", red);
                material.SetVector("_PaverGreyTone", grey);
                material.SetVector("_PaverFarTone", Vector4.Lerp(red, grey, material.GetFloat("_PaverGreyFraction")));
                material.SetVector("_PaverFinish", new Vector4(.0017f, .68f, .59f, 0));
                material.SetColor("_Color", new Color(.83f, .86f, .88f));
                material.SetFloat("_Saturation", .76f);
                material.SetFloat("_Parallax", .0004f);
                Matte(material, .10f, .45f, .25f, .64f, .20f, .08f);
                break;
            }
            case Finish.Grass:
                bool maintained = material.name == "CourtMaintainedGrass";
                material.SetColor("_Color", maintained ? new Color(.61f, .63f, .42f) : new Color(.54f, .55f, .375f));
                material.SetFloat("_Saturation", .47f);
                material.SetFloat("_MeadowDrying", maintained ? .37f : material.name == "RoadVergeSurface" ? .77f : .69f);
                material.SetVector("_MeadowSoilRange", new Vector4(.061f, .126f, .38f, .69f));
                Matte(material, .125f, .53f, .29f, .66f, .18f, .14f);
                break;
            case Finish.ForestFloor:
                material.SetColor("_Color", material.GetColor("_Color") * new Color(1.08f, .97f, .89f, 1));
                material.SetFloat("_Saturation", .55f);
                Matte(material, .125f, .48f, .27f, .67f, .20f, .12f);
                break;
            case Finish.Cement:
                material.SetColor("_Color", material.GetColor("_Color") * new Color(.95f, .94f, .91f, 1));
                Matte(material, .095f, .47f, .25f, .65f, .17f, .12f);
                break;
            case Finish.Plinth:
                material.SetColor("_Color", material.GetColor("_Color") * new Color(1.04f, .92f, .91f, 1));
                Matte(material, .09f, .48f, .22f, .62f, .20f, .12f);
                break;
            case Finish.Timber:
                material.SetColor("_Color", material.GetColor("_Color") * new Color(1.08f, .96f, .82f, 1));
                Matte(material, .10f, .58f, .28f, .54f, .16f, .12f);
                break;
            case Finish.Broadleaf:
                Canopy(material, .98f, .54f, new Color(.96f, .59f, .09f), new Color(.80f, .24f, .067f));
                break;
            case Finish.Birch:
                Canopy(material, .97f, .50f, new Color(.98f, .68f, .125f), new Color(.79f, .32f, .080f));
                break;
            case Finish.Hedge:
                material.SetColor("_Color", new Color(.78f, .85f, .56f));
                Canopy(material, .47f, .42f, new Color(.59f, .53f, .25f), new Color(.49f, .34f, .17f));
                break;
            case Finish.Needles:
                material.SetColor("_Color", new Color(.76f, .89f, .79f));
                material.SetFloat("_BumpStrength", .14f);
                material.SetFloat("_Translucency", .16f);
                break;
            case Finish.Blades:
                material.SetColor("_Color", new Color(.43f, .41f, .28f));
                material.SetFloat("_BumpStrength", .09f);
                break;
        }
    }

    private static void Matte(Material material, float bump, float detail, float roughnessScale, float roughnessBias,
        float occlusion, float broadVariation)
    {
        material.SetFloat("_BumpStrength", bump);
        material.SetFloat("_AlbedoContrast", detail);
        material.SetFloat("_RoughnessScale", roughnessScale);
        material.SetFloat("_RoughnessBias", roughnessBias);
        material.SetFloat("_OcclusionStrength", occlusion);
        material.SetFloat("_PaintedVariation", broadVariation);
    }

    private static void Canopy(Material material, float autumn, float clustering, Color gold, Color copper)
    {
        material.SetFloat("_AutumnAmount", autumn);
        material.SetFloat("_AutumnCrownVariation", clustering);
        material.SetColor("_AutumnGold", gold);
        material.SetColor("_AutumnCopper", copper);
        material.SetFloat("_Translucency", .26f);
        material.SetFloat("_BumpStrength", .18f);
    }
}
