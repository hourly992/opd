using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Material pass for the saved district. Keep the material names, texture families,
// world scale and surface classes used by the route and graphics checks.
internal static class DistrictAutumnMaterials
{
    private const string Folder = "Assets/PhotoReconstruction/Stage13Style/Materials";
    private const string ScenePath = "Assets/Scenes/Street.unity";

    private enum Surface
    {
        Other, MainRoad, ServiceAsphalt, PedestrianAsphalt, RedPaving, GreyPaving,
        Lawn, Verge, Woodland, Brick, Concrete, Plinth
    }

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open the saved Street scene before styling its materials.");

        Directory.CreateDirectory(Folder);
        AssetDatabase.Refresh();
        var replacements = new Dictionary<Material, Material>();
        int changedRenderers = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (renderer.gameObject.scene != scene)
                continue;

            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source || !source.shader || source.shader.name != "Street/TexturedSurface")
                    continue;

                if (!replacements.TryGetValue(source, out var material))
                {
                    var kind = Classify(source);
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    if (kind == Surface.Other || sourcePath.StartsWith(Folder + "/", StringComparison.Ordinal) ||
                        !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long fileId))
                    {
                        replacements.Add(source, source);
                        continue;
                    }

                    string target = $"{Folder}/{guid}_{fileId}.mat";
                    material = AssetDatabase.LoadAssetAtPath<Material>(target);
                    if (material)
                        EditorUtility.CopySerialized(source, material);
                    else
                    {
                        material = new Material(source);
                        AssetDatabase.CreateAsset(material, target);
                    }

                    material.name = source.name;
                    Style(material, kind);
                    EditorUtility.SetDirty(material);
                    replacements.Add(source, material);
                }

                if (material == source)
                    continue;
                materials[i] = material;
                changed = true;
            }

            if (!changed)
                continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            changedRenderers++;
        }

        int variants = 0;
        foreach (var pair in replacements)
            if (pair.Key != pair.Value) variants++;
        Debug.Log($"DISTRICT_AUTUMN_MATERIALS variants={variants} renderers={changedRenderers}");
    }

    private static Surface Classify(Material material)
    {
        string name = material.name;
        switch (Mathf.RoundToInt(material.GetFloat("_SurfaceClass")))
        {
            case 2: return Surface.Brick;
            case 4:
                if (name == "ReferenceAsphalt" || name == "MainRoadAsphalt") return Surface.MainRoad;
                return name.Contains("Footpath", StringComparison.OrdinalIgnoreCase)
                    ? Surface.PedestrianAsphalt : Surface.ServiceAsphalt;
            case 6:
                return name.Contains("Grey", StringComparison.OrdinalIgnoreCase)
                    ? Surface.GreyPaving : Surface.RedPaving;
        }

        if (name == "Grass" || name == "CourtMaintainedGrass") return Surface.Lawn;
        if (name == "RoadVergeSurface") return Surface.Verge;
        if (name == "WoodlandFloor" || name == "Mulch") return Surface.Woodland;
        if (name == "Foundation" || name.Contains("Plinth", StringComparison.OrdinalIgnoreCase))
            return Surface.Plinth;
        if (name.Contains("Concrete", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Kerb", StringComparison.OrdinalIgnoreCase) || name == "Curb" || name == "Path")
            return Surface.Concrete;
        return Surface.Other;
    }

    private static void Style(Material material, Surface kind)
    {
        switch (kind)
        {
            case Surface.MainRoad:
                material.SetColor("_Color", new Color(.43f, .43f, .42f));
                material.SetFloat("_Saturation", .48f);
                Finish(material, .15f, .0008f, .70f, .27f, .58f, .20f);
                break;
            case Surface.ServiceAsphalt:
                material.SetColor("_Color", new Color(.65f, .63f, .59f));
                material.SetFloat("_Saturation", .52f);
                Finish(material, .20f, .0010f, .66f, .31f, .56f, .26f);
                break;
            case Surface.PedestrianAsphalt:
                material.SetColor("_Color", new Color(.69f, .68f, .64f));
                material.SetFloat("_Saturation", .45f);
                Finish(material, .12f, .0005f, .57f, .30f, .61f, .18f);
                break;
            case Surface.RedPaving:
                material.SetColor("_Color", new Color(.87f, .92f, .90f));
                material.SetFloat("_Saturation", .79f);
                Finish(material, .17f, .0006f, .59f, .31f, .54f, .21f);
                break;
            case Surface.GreyPaving:
                material.SetColor("_Color", new Color(.65f, .65f, .61f));
                material.SetFloat("_Saturation", .50f);
                Finish(material, .13f, .0005f, .56f, .29f, .60f, .18f);
                break;
            case Surface.Lawn:
            case Surface.Verge:
                bool maintained = material.name == "CourtMaintainedGrass";
                material.SetColor("_Color", maintained
                    ? new Color(.66f, .67f, .45f)
                    : new Color(.63f, .62f, .40f));
                material.SetFloat("_Saturation", maintained ? .62f : .56f);
                material.SetFloat("_MeadowDrying", maintained ? .29f : kind == Surface.Verge ? .76f : .67f);
                material.SetVector("_MeadowSoilRange", new Vector4(.049f, .10f, .40f, .70f));
                Finish(material, .19f, .0008f, .57f, .32f, .64f, .20f);
                break;
            case Surface.Woodland:
                material.SetColor("_Color", material.GetColor("_Color") * new Color(.86f, .81f, .69f, 1));
                material.SetFloat("_Saturation", .56f);
                Finish(material, .18f, .0009f, .65f, .33f, .63f, .24f);
                break;
            case Surface.Brick:
                material.SetFloat("_Saturation", Mathf.Max(.90f, material.GetFloat("_Saturation")));
                Finish(material, Mathf.Min(.11f, material.GetFloat("_BumpStrength")), .0006f,
                    .60f, .30f, .63f, .18f);
                break;
            case Surface.Concrete:
                material.SetColor("_Color", material.GetColor("_Color") * new Color(.96f, .94f, .89f, 1));
                material.SetFloat("_Saturation", .52f);
                Finish(material, .17f, .0007f, .55f, .32f, .57f, .21f);
                break;
            case Surface.Plinth:
                Finish(material, .12f, .0007f, .59f, .30f, .59f, .19f);
                break;
        }
    }

    private static void Finish(Material material, float normal, float relief, float contrast,
        float roughnessScale, float roughnessBias, float occlusion)
    {
        material.SetFloat("_BumpStrength", normal);
        material.SetFloat("_Parallax", relief);
        material.SetFloat("_AlbedoContrast", contrast);
        material.SetFloat("_RoughnessScale", roughnessScale);
        material.SetFloat("_RoughnessBias", roughnessBias);
        material.SetFloat("_OcclusionStrength", occlusion);
        material.SetFloat("_Metallic", 0);
    }
}
