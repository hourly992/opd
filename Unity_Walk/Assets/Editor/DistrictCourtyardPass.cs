using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictCourtyardPass
{
    private const string ScenePath = "Assets/Scenes/Street.unity";
    private const string AuditPath = "street_qa/district_stage04_geometry_audit.json";
    private const string Output = "street_qa/district_courtyards";

    public static void EastAndBuild() => ApplyAndBuild("east", DistrictEastFinish.Apply);
    public static void SouthAndBuild() => ApplyAndBuild("south", DistrictSouthFinish.Apply);
    public static void WestAndBuild() => ApplyAndBuild("west", DistrictWestFinish.Apply);
    public static void EntryTerrainAndBuild() => ApplyAndBuild("entry_terrain", () =>
    {
        DistrictHouse15Finish.Apply();
        DistrictHouse13Finish.Apply();
        DistrictHouse1Finish.Apply();
        DistrictLibraryRecess.Apply();
    });

    public static void NorthCenterAndBuild() => ApplyAndBuild("north_center", () =>
    {
        DistrictEastFinish.Apply();
        DistrictNorthCenterFinish.Apply();
    });

    public static void Stage12AndBuild() => ApplyAndBuild("stage12", () =>
    {
        ApplyPart("stage12_canopy", DistrictHouse13Canopy.Apply);
        ApplyPart("stage12_drive", DistrictDriveSeams.Apply);
        ApplyPart("stage12_facade", DistrictHouse1Facade.Apply);
        ApplyPart("stage12_woodland", DistrictEasternWoodland.Apply);
    });

    public static void Stage13AndBuild() => ApplyAndBuild("stage13", () =>
    {
        ApplyWalkthroughStructures();
        ApplyWalkthroughEnvironment();
        ApplyPart("stage13_style", () =>
        {
            DistrictAutumnStyle.Apply();
            DistrictAutumnMaterials.Apply();
        });
    });

    public static void House5EntrancesAndBuild() => ApplyAndBuild("house5_entrances", () =>
    {
        House5PortalClosure.Apply();
        House5EntrancePaving.Apply();
    });

    public static void DistrictEntrancesAndBuild() => ApplyAndBuild("district_entrances", () =>
    {
        DistrictPortalRepairs.Apply();
        DistrictEntrancePavingRepairs.Apply();
        DistrictStairRepairs.Apply();
        DistrictEntranceSurvey.Capture("after");
    });

    private static void ApplyWalkthroughStructures()
    {
        ApplyPart("stage13_junctions", DistrictWalkthroughJunctions.Apply);
        ApplyPart("stage13_entrances", DistrictWalkthroughEntrances.Apply);
    }

    private static void ApplyAndBuild(string stage, Action apply)
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            if (EditorSceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save all open scenes before applying a courtyard pass.");
        var scene = EditorSceneManager.OpenScene(ScenePath);
        apply();
        CheckDistrict(stage);
        DistrictStage05.VerifyPoses();
        File.Copy("street_qa/district_stage05_editor/building_poses.json",
            Output + "/" + stage + "/building_poses.json", true);
        if (stage == "north_center")
            PhotoCourtJointsStage05.Audit();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("The edited district scene could not be saved.");
        UnityEditor.AssetDatabase.SaveAssets();
        StreetBuilder.BuildGeneratedScene();
    }

    public static void InspectExisting()
    {
        EditorSceneManager.OpenScene(ScenePath);
        Physics.SyncTransforms();
        CheckDistrict("baseline");
        var lines = new List<string>();
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        foreach (var route in access.routes)
            lines.Add($"ROUTE {route.address} ({route.points.Length}) " +
                      string.Join(" -> ", route.points.Take(4).Select(p => p.ToString("F3"))) +
                      " ... " + route.points.Last().ToString("F3"));
        InspectPhotoLine(lines, "center", new Vector3(19, 9, 17), new Vector3(44, .1f, 38),
            new[] { new Vector2(190, 607), new Vector2(240, 567), new Vector2(296, 525), new Vector2(337, 492) });
        InspectPhotoLine(lines, "north", new Vector3(-140, 8, 163), new Vector3(-90, 0, 139),
            new[] { new Vector2(32, 749), new Vector2(72, 750), new Vector2(549, 535), new Vector2(630, 550) });
        File.WriteAllLines(Output + "/baseline_geometry.txt", lines);
    }

    private static void ApplyWalkthroughEnvironment()
    {
        ApplyPart("stage13_parking", DistrictWalkthroughParking.RefreshSouthApron);
        ApplyPart("stage13_playgrounds", DistrictWalkthroughPlaygrounds.Apply);
        ApplyPart("stage13_backdrop", DistrictEasternWoodland.ApplyBackdrop);
    }

    private static void ApplyPart(string label, Action apply)
    {
        apply();
        CheckDistrict(label);
        DistrictStage05.VerifyPoses();
    }

    private static void CheckDistrict(string stage)
    {
        Physics.SyncTransforms();
        Directory.CreateDirectory(Output + "/" + stage);
        DistrictSurfaceAuditStage04.Check();
        File.Copy(AuditPath, Output + "/" + stage + "/geometry_audit.json", true);
        var audit = JsonUtility.FromJson<DistrictSurfaceAuditStage04.Report>(File.ReadAllText(AuditPath));
        if (!audit.passed)
            throw new InvalidOperationException($"District route audit failed in {stage}.");
    }

    private static void InspectPhotoLine(List<string> lines, string label, Vector3 from, Vector3 to, Vector2[] pixels)
    {
        var rotation = Quaternion.LookRotation(to - from);
        const float aspect = 1600f / 900;
        float scale = Mathf.Tan(55 * .5f * Mathf.Deg2Rad);
        foreach (var pixel in pixels)
        {
            for (int offset = -3; offset <= 3; offset += 3)
            {
                var direction = rotation * new Vector3(((pixel.x + offset) / 800 - 1) * scale * aspect,
                    (1 - pixel.y / 450) * scale, 1);
                var hits = Physics.RaycastAll(from, direction, 70).OrderBy(hit => hit.distance).Take(6).ToArray();
                lines.Add($"{label} pixel={pixel} dx={offset}");
                foreach (var hit in hits)
                {
                    string mesh = hit.collider is MeshCollider mc && mc.sharedMesh ? mc.sharedMesh.name : "box";
                    lines.Add($"  {hit.point:F4} normal={hit.normal:F3} triangle={hit.triangleIndex} {hit.collider.name} [{mesh}]");
                }
            }
        }
    }
}
