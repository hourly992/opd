using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetPhotoComparisonFinish
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string MaterialFolder = "Assets/PhotoReconstruction/Stage07Surfaces";
    private const string Output = "street_qa/photo_comparison";

    public static void ApplyAndBuild()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save open scenes before applying the photo comparison pass.");

        const string backup = "../.local-backups/photo_comparison_20261105";
        Directory.CreateDirectory(backup);
        if (!File.Exists(backup + "/Street.unity"))
            File.Copy(ScenePath, backup + "/Street.unity");

        var scene = EditorSceneManager.OpenScene(ScenePath);
        ApplySurfaces();
        StreetPlinthPhotoFinish.Apply();
        StreetCentrePhotoFinish.Apply();
        StreetLibraryPhotoFinish.Apply();

        Physics.SyncTransforms();
        Directory.CreateDirectory(Output);
        DistrictStage05.VerifyPoses();
        File.Copy("street_qa/district_stage05_editor/building_poses.json", Output + "/building_poses.json", true);
        DistrictSurfaceAuditStage04.Check();
        const string auditPath = "street_qa/district_stage04_geometry_audit.json";
        File.Copy(auditPath, Output + "/geometry_audit.json", true);
        var report = JsonUtility.FromJson<DistrictSurfaceAuditStage04.Report>(File.ReadAllText(auditPath));
        if (!report.passed)
            throw new InvalidOperationException("The district route audit failed. See " + Output);
        PhotoCourtJointsStage05.Audit();
        File.Copy("street_qa/district_stage05_editor/court_joint_audit.json", Output + "/court_joint_audit.json", true);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("The Street scene could not be saved.");
        AssetDatabase.SaveAssets();
        StreetBuilder.BuildGeneratedScene();
    }

    private static void ApplySurfaces()
    {
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();
        var replacements = new Dictionary<Material, Material>();
        int terrainMaterials = 0, pavingMaterials = 0;
        foreach (var renderer in UnityEngine.Object.FindObjectsByType<MeshRenderer>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                var source = materials[i];
                if (!source || !source.shader || source.shader.name != "Street/TexturedSurface")
                    continue;

                bool meadow = source.GetFloat("_MeadowDrying") > 0;
                bool redPaving = source.name == "ReferenceRedPaving" || source.name == "PhotoRedRunningBond"
                              || source.name == "ResidentialAccessPaving";
                if (!meadow && !redPaving)
                    continue;

                if (!replacements.TryGetValue(source, out var material))
                {
                    material = CloneMaterial(source);
                    if (meadow)
                    {
                        material.SetVector("_MeadowSoilRange", new Vector4(.036f, .082f, .43f, .70f));
                        float exposure = .68f;
                        if (source.name == "RoadVergeSurface") exposure = .16f;
                        if (source.name == "CourtMaintainedGrass")
                        {
                            exposure = .10f;
                            material.SetColor("_Color", new Color(.63f, .73f, .54f));
                        }
                        material.SetFloat("_MeadowDrying", exposure);
                        terrainMaterials++;
                    }
                    else
                    {
                        material.SetFloat("_PaverPattern", 1);
                        material.SetFloat("_PaverGreyFraction", 0);
                        material.SetFloat("_PaverJointMeters", .004f);
                        material.SetColor("_Color", new Color(.85f, .93f, 1));
                        pavingMaterials++;
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
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }
        if (terrainMaterials < 3 || pavingMaterials < 2)
            throw new InvalidOperationException("The expected grass and red paving materials were not found.");
        Debug.Log($"STREET_PHOTO_SURFACES terrain={terrainMaterials} redPaving={pavingMaterials}");
    }

    private static Material CloneMaterial(Material source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        if (sourcePath.StartsWith(MaterialFolder + "/", StringComparison.Ordinal))
            return source;

        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        string path = $"{MaterialFolder}/{source.name}_{guid}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
            material.name = source.name;
        }
        return material;
    }
}
