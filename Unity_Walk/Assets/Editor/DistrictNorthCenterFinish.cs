using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictNorthCenterFinish
{
    private const string Root = "Assets/PhotoReconstruction/Stage10Center";
    private const string Group = "Центр · очищенный стык старой дорожки";
    private const string Source = "https://realty.yandex.ru/offer/4006141392816993274/";

    public static void Apply()
    {
        if (EditorSceneManager.GetActiveScene().path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene.");
        var existingMarker = GameObject.Find(Group);

        var floor = GameObject.Find("Серое прямоугольное мощение · внутренний двор")?.GetComponent<MeshCollider>();
        if (!floor || !floor.enabled)
            throw new InvalidOperationException("The saved grey courtyard is missing.");
        Physics.SyncTransforms();
        Directory.CreateDirectory(Root);
        AssetDatabase.Refresh();
        // Raycast through the visible diagonal seam in the saved district frame.
        // This box stays on the supported side of the existing grey slab.
        var clip = Rect.MinMaxRect(24.002f, 40.20f, 30.594f, 46.80f);
        int total = 0;
        int candidates = 0;
        foreach (var name in new[] { "Скруглённое мощение · 1", "Низкие кромки скруглённых подходов" })
        {
            var obj = GameObject.Find(name);
            var filter = obj?.GetComponent<MeshFilter>();
            var collider = obj?.GetComponent<MeshCollider>();
            if (!filter || !collider || collider.sharedMesh != filter.sharedMesh)
                throw new InvalidOperationException("The saved approach must use the same visual and physical mesh: " + name);
            candidates++;
            var mesh = PhotoCourtSeamRepairStage05.ClipOverPaving(filter, floor, clip, out int trimmed);
            if (!mesh)
                continue;
            string path = $"{Root}/Retained_{GlobalObjectId.GetGlobalObjectIdSlow(filter).targetObjectId}.asset";
            var saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (saved)
            {
                EditorUtility.CopySerialized(mesh, saved);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(saved);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
                saved = mesh;
            }
            filter.sharedMesh = saved;
            collider.sharedMesh = null;
            collider.sharedMesh = saved;
            EditorUtility.SetDirty(filter);
            EditorUtility.SetDirty(collider);
            total += trimmed;
        }
        if (total == 0 && candidates > 0 && !existingMarker)
            throw new InvalidOperationException("The documented overlap could not be isolated.");
        Physics.SyncTransforms();

        var marker = existingMarker ? existingMarker : new GameObject(Group);
        marker.tag = "EditorOnly";
        var survey = marker.GetComponent<PhotoSurveyMarker>() ?? marker.AddComponent<PhotoSurveyMarker>();
        survey.category = "Западный стык серого мощения двора";
        survey.observation = "Серое мощение образует единое полотно. По игровому кадру и лучевым пробам найден выступ старой красной дорожки внутри полотна.";
        survey.uncertainty = "Срез ограничен участком с опорой на существующее серое мощение. Высоты взяты из сохранённой геометрии.";
        survey.sourceUrls = new[] { Source };
        survey.studyBounds = new Bounds(new Vector3(clip.center.x, .1f, clip.center.y), new Vector3(clip.width, .4f, clip.height));
        survey.geometryProvisional = true;
        Debug.Log($"DISTRICT_CENTER overlappingTriangles={total}");
    }
}
