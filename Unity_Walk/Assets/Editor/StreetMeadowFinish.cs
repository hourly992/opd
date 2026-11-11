using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetMeadowFinish
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string MaterialFolder = "Assets/PhotoReconstruction/Stage06Meadow";

    public static void ApplyAndBuild()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scenes before applying the meadow finish.");

        const string backup = "../.local-backups/grass_stage06_20261105";
        Directory.CreateDirectory(backup);
        if (!File.Exists(backup + "/Street.unity"))
            File.Copy(ScenePath, backup + "/Street.unity");

        var scene = EditorSceneManager.OpenScene(ScenePath);
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
        var soil = AssetDatabase.LoadAssetAtPath<Texture2D>(
            "Assets/ThirdParty/PolyHaven/Graphics2K/gravel_floor_02/Albedo.jpg");
        if (!soil)
            throw new InvalidOperationException("The existing soil texture is missing.");

        var replacements = new Dictionary<Material, Material>();
        int groundMaterials = 0, foliageMaterials = 0, changedRenderers = 0;
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
                if (!source || !source.shader)
                    continue;

                string texturePath = source.HasProperty("_MainTex")
                    ? AssetDatabase.GetAssetPath(source.GetTexture("_MainTex")) : "";
                bool ground = source.shader.name == "Street/TexturedSurface" &&
                    (texturePath.Contains("/grass_ground/") || texturePath.Contains("/leafy_grass/"));
                bool foliage = source.shader.name == "Street/Foliage" &&
                    (source.GetFloat("_Grass") > .5f || source.name == "MeadowClover");
                if (!ground && !foliage)
                    continue;

                if (!replacements.TryGetValue(source, out var material))
                {
                    string sourcePath = AssetDatabase.GetAssetPath(source);
                    material = sourcePath.StartsWith(MaterialFolder + "/", StringComparison.Ordinal)
                        ? source : CloneMaterial(source, sourcePath);
                    if (ground)
                    {
                        bool maintainedIsland = source.name == "CourtMaintainedGrass";
                        material.SetColor("_Color", maintainedIsland ? new Color(.65f, .69f, .55f) : new Color(.61f, .655f, .52f));
                        material.SetFloat("_Saturation", maintainedIsland ? .68f : .53f);
                        material.SetTexture("_DryGroundTex", soil);
                        material.SetFloat("_MeadowDrying", maintainedIsland ? .33f : .91f);
                        groundMaterials++;
                    }
                    else
                    {
                        bool blades = source.GetFloat("_Grass") > .5f;
                        material.SetColor("_Color", blades ? new Color(.235f, .25f, .16f) : new Color(.50f, .55f, .36f));
                        material.SetFloat("_MeadowKeep", blades ? .50f : .44f);
                        foliageMaterials++;
                    }
                    EditorUtility.SetDirty(material);
                    replacements.Add(source, material);
                }
                materials[i] = material;
                changed = true;
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
                changedRenderers++;
            }
        }

        if (groundMaterials < 2 || foliageMaterials < 1)
            throw new InvalidOperationException("The saved grass materials were not fully identified.");

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("The Street scene could not be saved.");
        AssetDatabase.SaveAssets();
        Debug.Log($"STREET_MEADOW groundMaterials={groundMaterials} foliageMaterials={foliageMaterials} changedRenderers={changedRenderers}");
        StreetBuilder.BuildGeneratedScene();
    }

    private static Material CloneMaterial(Material source, string sourcePath)
    {
        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        string path = $"{MaterialFolder}/{source.name}_{guid}.mat";
        var saved = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (saved)
            return saved;
        var clone = new Material(source) { name = source.name };
        AssetDatabase.CreateAsset(clone, path);
        // Unity may set the asset name from its file name. Keep the material's
        // semantic name for surface classification and for subsequent passes.
        clone.name = source.name;
        return clone;
    }
}
