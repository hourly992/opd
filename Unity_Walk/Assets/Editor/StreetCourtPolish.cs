using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetCourtPolish
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string MaterialFolder = "Assets/PhotoReconstruction/Stage09Court";
    private const string PaverName = "CourtGreyRectangularPavers";

    public static void ApplyAndBuild()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the open scenes before updating the district.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        var rayShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Shaders/StreetRayContact.compute");
        var sun = RenderSettings.sun;
        if (!sun || !rayShader)
            throw new InvalidOperationException("Street sunlight and the DXR shader are required.");

        sun.color = new Color(1, .94f, .84f);
        RenderSettings.ambientEquatorColor = new Color(.71f, .70f, .68f);
        RenderSettings.ambientGroundColor = new Color(.54f, .50f, .46f);
        RenderSettings.fogColor = new Color(.65f, .62f, .56f);
        RenderSettings.fogDensity = .00175f;
        foreach (var effect in UnityEngine.Object.FindObjectsByType<StreetPresentation>())
            if (effect.gameObject.scene == scene)
                effect.rayContactShader = rayShader;

        ApplyGreyPavers();
        StreetCourtDetail.Apply();
        StreetAutumnLitter.Apply();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("The courtyard changes could not be saved.");
        AssetDatabase.SaveAssets();
        StreetBuilder.BuildGeneratedScene();
    }

    private static void ApplyGreyPavers()
    {
        const string path = MaterialFolder + "/CourtyardPaving.mat";
        var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
        int updated = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>())
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                var source = materials[slot];
                if (!source || (source != copy && source.name != PaverName))
                    continue;
                if (!copy)
                {
                    Directory.CreateDirectory(MaterialFolder);
                    AssetDatabase.Refresh();
                    copy = new Material(source);
                    AssetDatabase.CreateAsset(copy, path);
                }
                copy.name = PaverName;
                materials[slot] = copy;
                changed = true;
                updated++;
            }
            if (changed)
            {
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }
        if (updated == 0)
            throw new InvalidOperationException("No courtyard paving was found.");
        copy.SetColor("_Color", new Color(.62f, .65f, .67f));
        EditorUtility.SetDirty(copy);
    }
}
