using UnityEditor;
using UnityEngine;

// Incomplete study, deliberately excluded from the player. Do not substitute an invented human figure.
internal static class MonumentReferenceBuilder
{
    internal static void Generate(Transform parent)
    {
        var root = new GameObject("10 · Памятник Дунину-Горкавичу · НЕЗАВЕРШЁННЫЙ макет · EditorOnly");
        root.tag = "EditorOnly";
        var info = root.AddComponent<MonumentReference>();
        info.identity = "Памятник А. А. Дунину-Горкавичу, ул. Дунина-Горкавича, 1";
        info.source = "https://visit-hm.ru/places/216";
        info.latitude = 61.001164; info.longitude = 69.060422;
        info.accuracy = "Точка с туристического портала; центр и ориентация требуют проверки. Размеры макета постамента условны. Бронзовая фигура и растительный рельеф отсутствуют. Не является точной копией. См. StreetSource/monument_reference.json.";
        var granite = EnvironmentMesh.Material("MonumentGraniteStudy", new Color(.42f, .25f, .24f), 0, .30f);
        StreetSurfaceLibrary.Apply(granite, "concrete_wall_006", .6f, new Color(.66f, .40f, .38f), .12f);
        var mesh = new EnvironmentMesh();
        mesh.RoundedBox(granite, new Vector3(0, .06f, 0), new Vector3(.92f, .12f, .72f), .025f);
        mesh.Box(granite, new Vector3(0, .68f, .05f), new Vector3(.52f, 1.24f, .45f));
        mesh.Box(granite, new Vector3(-.60f, 2.34f, .22f), new Vector3(.44f, 2.06f, .25f));
        // Rounded upper shoulder of the tall left slab is visible on both available frontal photos.
        Vector3[] outline = { new Vector3(-.82f, 3.37f, .22f), new Vector3(-.82f, 3.72f, .22f),
            new Vector3(-.79f, 3.91f, .22f), new Vector3(-.70f, 4.07f, .22f), new Vector3(-.56f, 4.18f, .22f),
            new Vector3(-.38f, 4.21f, .22f), new Vector3(-.38f, 3.37f, .22f) };
        mesh.Face(granite, Vector3.back, outline);
        mesh.Face(granite, Vector3.forward, System.Array.ConvertAll(outline, p => p + Vector3.forward * .25f));
        for (int i = 0; i < outline.Length; i++)
        {
            var a = outline[i]; var b = outline[(i + 1) % outline.Length];
            mesh.Face(granite, Vector3.Cross(b - a, Vector3.forward).normalized, a, b, b + Vector3.forward * .25f, a + Vector3.forward * .25f);
        }
        mesh.Box(granite, new Vector3(.62f, 1.96f, .26f), new Vector3(.24f, 1.28f, .24f));
        var support = mesh.Create("DuninGorkavichSupportStudy", "Черновой постамент · без скульптуры · размеры требуют обмера");
        support.transform.SetParent(root.transform, false);
        // Keep a reusable study asset and evidence anchor without adding a false monument to the game.
        PrefabUtility.SaveAsPrefabAsset(root, EnvironmentMesh.Root + "/Prefabs/DuninGorkavich_PlacementStudy.prefab");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(-112.31054f, 0, 162.30456f);
    }
}
