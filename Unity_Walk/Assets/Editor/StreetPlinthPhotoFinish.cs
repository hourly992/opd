using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class StreetPlinthPhotoFinish
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string MaterialFolder = "Assets/PhotoReconstruction/Stage07Plinth";
    private const string PlinthName = "TurquoisePlinth";
    private static readonly int PanelProperty = Shader.PropertyToID("_PlinthPanels");

    // Photos resolve the cladding family and relative tint. Panel dimensions are visual estimates.
    private static readonly Vector4 PanelDimensions = new Vector4(.64f, .34f, .006f, 1f);
    private static readonly Dictionary<string, Color> Tints = new Dictionary<string, Color>
    {
        { "6", new Color(.22f, .34f, .325f) },
        { "8", new Color(.23f, .345f, .335f) },
        { "10", new Color(.23f, .36f, .35f) },
        { "11", new Color(.20f, .32f, .30f) },
        { "13", new Color(.21f, .35f, .34f) },
        { "15", new Color(.215f, .33f, .355f) }
    };

    internal static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            throw new InvalidOperationException("Open the Street scene before applying the plinth finish.");

        var buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var renderersByAddress = Tints.Keys.ToDictionary(address => address, address => buildings
            .Where(building => building.gameObject.scene == scene && building.address == address)
            .SelectMany(building => building.GetComponentsInChildren<MeshRenderer>(true))
            .Where(renderer => renderer.sharedMaterials.Any(IsTurquoisePlinth))
            .Distinct()
            .ToArray());

        foreach (var entry in renderersByAddress)
        {
            if (entry.Value.Length == 0)
                throw new InvalidOperationException("No turquoise plinth renderers found for house " + entry.Key + ".");
            if (entry.Value.SelectMany(renderer => renderer.sharedMaterials).Where(IsTurquoisePlinth)
                .Any(material => !material.HasProperty(PanelProperty)))
                throw new InvalidOperationException("The turquoise plinth shader is missing _PlinthPanels.");
        }

        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
        foreach (var entry in renderersByAddress)
        {
            string address = entry.Key;
            var source = entry.Value[0].sharedMaterials.First(IsTurquoisePlinth);
            string assetPath = $"{MaterialFolder}/TurquoisePlinth_{address}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (!material)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, assetPath);
            }

            material.name = PlinthName;
            material.SetVector(PanelProperty, PanelDimensions);
            material.SetColor("_Color", Tints[address]);
            material.SetFloat("_Weathering", .22f);
            material.SetFloat("_RoughnessBias", .60f);
            EditorUtility.SetDirty(material);

            foreach (var renderer in entry.Value)
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (IsTurquoisePlinth(materials[i]))
                        materials[i] = material;
                renderer.sharedMaterials = materials;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }

            Debug.Log($"PHOTO_PLINTH house={address} renderers={entry.Value.Length} panels={PanelDimensions.x:F2}x{PanelDimensions.y:F2}");
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static bool IsTurquoisePlinth(Material material)
    {
        return material && material.name == PlinthName && material.shader &&
               material.shader.name == "Street/TexturedSurface";
    }
}
