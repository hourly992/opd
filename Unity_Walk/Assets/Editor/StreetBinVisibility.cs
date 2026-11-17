using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class StreetBinVisibility
{
    private const string PilotPrefabFolder = "Assets/StreetGenerated/Pilot/Prefabs/";
    private const string PilotMeshFolder = "Assets/StreetGenerated/Pilot/Meshes/";
    private const string Containers = "Container1100";
    private const string LitterBins = "LitterBin";

    [Serializable]
    internal sealed class SceneResult
    {
        public string scene;
        public int containers, litterBins, changed, activeBins, activeRenderers, activeColliders;
    }

    [MenuItem("Street/Waste bins/Hide in active scene")]
    private static void HideActiveScene() => SetHidden(SceneManager.GetActiveScene());

    [MenuItem("Street/Waste bins/Restore in active scene")]
    private static void RestoreActiveScene() => SetHidden(SceneManager.GetActiveScene(), false);

    internal static SceneResult SetHidden(Scene scene, bool hidden = true)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new ArgumentException("A loaded scene is required.", nameof(scene));

        var result = new SceneResult { scene = scene.name };
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
        {
            GameObject instance = item.gameObject;
            string variety = IdentifyBin(instance);
            if (variety == null)
                continue;
            if (variety == Containers)
                result.containers++;
            else
                result.litterBins++;

            if (instance.activeSelf == hidden)
            {
                Undo.RecordObject(instance, hidden ? "Hide street waste bin" : "Restore street waste bin");
                instance.SetActive(!hidden);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
                result.changed++;
            }

            if (instance.activeInHierarchy)
                result.activeBins++;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                    result.activeRenderers++;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                if (collider.enabled && collider.gameObject.activeInHierarchy)
                    result.activeColliders++;
        }

        if (result.changed > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        if (hidden && (result.activeBins != 0 || result.activeRenderers != 0 || result.activeColliders != 0))
            throw new InvalidOperationException($"Waste bin objects still active in {scene.path}.");

        Debug.Log($"STREET_WASTE_BINS {JsonUtility.ToJson(result)}");
        return result;
    }

    private static string IdentifyBin(GameObject instance)
    {
        GameObject prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(instance);
        if (prefabRoot)
        {
            string variety = MatchAsset(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabRoot),
                PilotPrefabFolder, ".prefab");
            if (variety == null)
                variety = MatchAsset(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(prefabRoot)),
                    PilotPrefabFolder, ".prefab");
            if (variety != null)
                return prefabRoot == instance ? variety : null;
        }

        if (!instance.TryGetComponent(out MeshFilter filter) || !filter.sharedMesh)
            return null;
        return MatchAsset(AssetDatabase.GetAssetPath(filter.sharedMesh), PilotMeshFolder, ".asset");
    }

    private static string MatchAsset(string path, string folder, string extension)
    {
        if (path == folder + Containers + extension)
            return Containers;
        return path == folder + LitterBins + extension ? LitterBins : null;
    }
}
