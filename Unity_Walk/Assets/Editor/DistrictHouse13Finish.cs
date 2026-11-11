using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class DistrictHouse13Finish
{
    private const string Root = "Assets/PhotoReconstruction/Stage11East";
    private const string GroupName = "№13 · площадка северного входа";
    private const float Back = 98.14f;
    private const float Front = 98.99f;
    private const float Top = .139f;
    private const float RampEnd = 100.16f;

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene.");
        if (GameObject.Find(GroupName))
            return;
        var building = UnityEngine.Object.FindObjectsByType<StreetBuilding>().Single(b => b.address == "13" &&
            b.GetComponentsInChildren<Transform>().Any(t => t.name == "Access · торцевой вход 10"));
        var door = building.GetComponentsInChildren<Transform>().Single(t => t.name == "Access · торцевой вход 10");
        if (Vector3.Distance(door.position, new Vector3(166.8544f, 0, 98.192f)) > .02f || door.forward.z < .99f)
            throw new InvalidOperationException("The matched north entrance moved.");

        Physics.SyncTransforms();
        var access = UnityEngine.Object.FindAnyObjectByType<ResidentialAccess>();
        var existing = GameObject.Find("Скруглённое мощение · 1").GetComponent<MeshCollider>();
        var red = existing.GetComponent<MeshRenderer>().sharedMaterials.Single(m => m && m.name == "ResidentialAccessPaving");
        var stone = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/PhotoReconstruction/Stage08Autumn/Materials/EntranceConcrete_11f4a63e99d58d742a27d41bee95e59e.mat");
        if (!stone || !red)
            throw new InvalidOperationException("The saved paving and concrete materials are required.");
        var route = access.routes.Single(r => r.points.Length > 2 &&
            Vector2.Distance(new Vector2(r.points[0].x, r.points[0].z), new Vector2(door.position.x, door.position.z)) < .05f);
        int segment = Array.FindIndex(route.points, p => p.z >= RampEnd);
        if (segment < 1)
            throw new InvalidOperationException("The north approach cannot be joined to the saved route.");
        Vector3 a = route.points[segment - 1], b = route.points[segment];
        float noseCenter = Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.z, b.z, RampEnd));
        float left = noseCenter - .57f, right = noseCenter + .57f;
        float leftY = FloorY(existing, left, RampEnd), rightY = FloorY(existing, right, RampEnd);
        if (Mathf.Abs(rightY - leftY) > .035f || Mathf.Max(leftY, rightY) >= Top - .025f)
            throw new InvalidOperationException("The pavement at the foot of the new ramp has unexpected levels.");

        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var mesh = new EnvironmentMesh();
        // The photo shows a raised strip alongside the wall and a lower red path.
        // Dimensions are fitted to the saved north-facing doorway and its actual path.
        mesh.Box(stone, new Vector3(166.96f, (Top - .037f) * .5f, (Back + Front) * .5f),
            new Vector3(4.10f, Top + .037f, Front - Back));
        mesh.Box(stone, new Vector3(165.56f, .0215f, Front + .15f),
            new Vector3(1.27f, .117f, .30f));

        var leftHigh = new Vector3(166.32f, Top, Front);
        var rightHigh = new Vector3(167.95f, Top, Front);
        var leftLow = new Vector3(left, leftY + .002f, RampEnd);
        var rightLow = new Vector3(right, rightY + .002f, RampEnd);
        mesh.Face(red, Vector3.up, leftHigh, rightHigh, rightLow);
        mesh.Face(red, Vector3.up, leftHigh, rightLow, leftLow);
        Vector3 Foot(Vector3 point) => new Vector3(point.x, -.032f, point.z);
        mesh.Face(stone, Vector3.left, leftHigh, leftLow, Foot(leftLow), Foot(leftHigh));
        mesh.Face(stone, Vector3.right, rightLow, rightHigh, Foot(rightHigh), Foot(rightLow));

        var group = new GameObject(GroupName);
        var obj = mesh.Create("House13NorthLanding", "Серая площадка, две ступени и красный скат", Root, true);
        obj.transform.SetParent(group.transform, false);
        GameObjectUtility.SetStaticEditorFlags(obj, StaticEditorFlags.BatchingStatic);
        var collider = obj.AddComponent<MeshCollider>();
        collider.sharedMesh = obj.GetComponent<MeshFilter>().sharedMesh;
        access.surfaces = access.surfaces.Append(collider).ToArray();
        EditorUtility.SetDirty(access);
        Physics.SyncTransforms();
        float first = FloorY(collider, 165.55f, Front + .15f);
        float second = FloorY(collider, 165.55f, Front - .16f);
        if (Mathf.Abs(first - .08f) > .002f || Mathf.Abs(second - Top) > .002f)
            throw new InvalidOperationException("The two raised levels are missing.");
        for (int i = 1; i < 7; i++)
        {
            float t = i / 7f, z = Mathf.Lerp(Front, RampEnd, t);
            float x = Mathf.Lerp((leftHigh.x + rightHigh.x) * .5f, noseCenter, t);
            FloorY(collider, x - .35f, z);
            FloorY(collider, x + .35f, z);
        }

        var evidence = new GameObject("Фотоориентир · северный корпус №13") { tag = "EditorOnly" };
        evidence.transform.SetParent(group.transform, false);
        var note = evidence.AddComponent<PhotoSurveyMarker>();
        note.category = "№13 · площадка и две низкие ступени";
        note.observation = "Адресный кадр показывает угол дома, общую низкую площадку и боковой подход. На спутниковом кадре полоса навеса совпадает с северным торцевым входом.";
        note.uncertainty = "Привязка по углу фасада и навесу является выводом из сопоставления. Габариты и высоты ступеней оценены по месту.";
        note.geometryProvisional = true;
        note.studyBounds = collider.bounds;
        note.sourceUrls = new[] {
            "https://yandex.ru/maps/org/169841655322/",
            "https://avatars.mds.yandex.net/get-altay/16569030/2a00000198543ee61cdd8958713a87e97a35/orig",
            "https://yandex.ru/maps/57/khanty-mansiysk/?l=sat&ll=69.065496%2C61.000576&z=19"
        };
        Debug.Log($"STAGE11_HOUSE13 step1={first:F3} landing={second:F3} rampSeam={leftY:F3}/{rightY:F3}");
    }

    private static float FloorY(MeshCollider floor, float x, float z)
    {
        if (!floor.Raycast(new Ray(new Vector3(x, 1, z), Vector3.down), out var hit, 1.4f) || hit.normal.y < .84f)
            throw new InvalidOperationException($"No supported floor at ({x:F3}, {z:F3}).");
        return hit.point.y;
    }
}
