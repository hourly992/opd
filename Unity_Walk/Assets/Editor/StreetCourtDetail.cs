using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreetCourtDetail
{
    private const string Root = "Assets/PhotoReconstruction/Stage09Court";
    private const string MarkerName = "Двор №7 · отделка низкого входа";
    private const float Wall = -1.34f;
    private const float ApproachJoinX = 60.141938f;
    private static readonly Vector3 Entrance = new Vector3(61.091938f, 0, 38.305f);
    private static readonly Vector3 Forward = Vector3.left;
    private static readonly Vector3 Right = Vector3.forward;
    private static readonly Quaternion Rotation = Quaternion.LookRotation(Forward);

    public static void Apply()
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Street.unity")
            throw new InvalidOperationException("Open the saved Street scene before refining No.7.");
        var door = GameObject.Find("Фотоориентир · Court7 · размеры приблизительные");
        var threshold = GameObject.Find("House 7 · low courtyard threshold")?.GetComponent<MeshCollider>();
        var approach = UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
            .SingleOrDefault(c => c.enabled && c.sharedMesh && c.sharedMesh.name == "CourtDoorContinuousPaving05");
        var building = door ? door.GetComponentInParent<StreetBuilding>() : null;
        var group = building ? building.GetComponent<LODGroup>() : null;
        if (!group || building.address != "7" || !threshold || !approach)
            throw new InvalidOperationException("The matched No.7 bay and its saved paved approach are required.");

        ClipRedPavingAtThreshold(threshold);
        if (GameObject.Find(MarkerName))
            return;

        var lods = group.GetLODs();
        var existing = lods.Select((level, i) => level.renderers.Single(r => r &&
            r.GetComponent<MeshFilter>()?.sharedMesh?.name == $"Court7_PhotoEntrance_LOD{i}")).ToArray();
        var originalDoor = FindMaterial(existing[0], "PhotoDoorGrey");
        var originalCanopy = FindMaterial(existing[0], "PhotoCanopyGreen");
        var originalFrame = FindMaterial(existing[0], "PhotoFrameGraphite");
        var approachRenderer = approach.GetComponent<MeshRenderer>();
        var thresholdRenderer = threshold.GetComponent<MeshRenderer>();
        if (!approachRenderer || approachRenderer.sharedMaterials.Length != 1 || !approachRenderer.sharedMaterial ||
            !thresholdRenderer || thresholdRenderer.sharedMaterials.Length != 1 ||
            threshold.GetComponent<MeshFilter>().sharedMesh != threshold.sharedMesh)
            throw new InvalidOperationException("The saved threshold renderer and collider must share one paved surface.");

        // Match the already authored low approach. Photos confirm the short level
        // transition but provide no surveyed elevation or gradient for the street.
        Physics.SyncTransforms();
        float joinError = 0;
        foreach (float across in new[] { -.8f, 0, .8f })
        {
            float before = Height(approach, ApproachJoinX - .018f, Entrance.z + across);
            float after = Height(threshold, ApproachJoinX + .018f, Entrance.z + across);
            joinError = Mathf.Max(joinError, Mathf.Abs(after - before));
        }
        float doorHeight = Height(threshold, Entrance.x + 1.30f, Entrance.z);
        if (joinError > .04f || Mathf.Abs(doorHeight - .16f) > .004f)
            throw new InvalidOperationException($"The existing approach no longer matches the low doorway: seam {joinError:F3}, floor {doorHeight:F3}.");

        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var slate = Finish(originalDoor, "Court7DoorSlate", new Color(.31f, .35f, .36f), .09f);
        var canopy = Finish(originalCanopy, "Court7CanopySlateGreen", new Color(.22f, .27f, .25f), .10f);
        var surround = Finish(originalDoor, "Court7JambStone", new Color(.47f, .47f, .44f), .02f);
        var edging = Finish(originalFrame, "Court7CoatedEdging", new Color(.46f, .48f, .43f), .20f);
        var thresholdMetal = Finish(originalFrame, "Court7SillMetal", new Color(.32f, .34f, .34f), .28f);
        var root = new GameObject(MarkerName);
        root.transform.SetParent(door.transform, false);

        // The same stone paving carries across the rendered ramp. Its shape and
        // collider are the original, including the checked rise into the sill.
        thresholdRenderer.sharedMaterial = approachRenderer.sharedMaterial;
        PrefabUtility.RecordPrefabInstancePropertyModifications(thresholdRenderer);

        for (int lod = 0; lod < lods.Length; lod++)
        {
            var renderer = existing[lod];
            renderer.sharedMaterials = renderer.sharedMaterials.Select(material => material.name switch
            {
                "PhotoDoorGrey" => slate,
                "PhotoCanopyGreen" => canopy,
                _ => material
            }).ToArray();
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

            var mesh = new EnvironmentMesh();
            AddTrim(mesh, surround, edging, thresholdMetal, lod);
            var details = mesh.Create($"Court7Trim_LOD{lod}", $"Откосы, порог и козырёк · LOD{lod}", Root, true);
            details.transform.SetParent(root.transform, true);
            GameObjectUtility.SetStaticEditorFlags(details, StaticEditorFlags.BatchingStatic);
            lods[lod].renderers = lods[lod].renderers.Append(details.GetComponent<MeshRenderer>()).ToArray();
        }
        group.SetLODs(lods);
        group.RecalculateBounds();
        PrefabUtility.RecordPrefabInstancePropertyModifications(group);
        EditorUtility.SetDirty(group);

        var evidence = new GameObject("Фотоориентир · размеры отделки оценочные");
        evidence.tag = "EditorOnly";
        evidence.transform.SetParent(root.transform, false);
        var survey = evidence.AddComponent<PhotoSurveyMarker>();
        survey.category = "Двор №7 · низкий вход и мощение";
        survey.observation = "Видны непрерывное серое мощение, низкий серый вход и широкий козырёк с тонким светлым контуром.";
        survey.uncertainty = "Откосы, наличник, кромки и детали креплений оценочные. Сохранены прежние уровни, контуры и коллизии подхода.";
        survey.sourceUrls = new[] {
            "https://realty.yandex.ru/offer/4006141392816993274/",
            "https://avatars.mds.yandex.net/get-realty-offers/15038303/2d86a840-819c-4ef2-a67e-6098b9d2a49e/app_large"
        };
        survey.geometryProvisional = true;
        survey.studyBounds = new Bounds(P(0, 1.5f, -1), new Vector3(3, 3, 5.3f));
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"STREET_COURT_DETAIL three LODs; unchanged sill y={doorHeight:F3}; largest sampled approach seam={joinError:F3} m");
    }

    private static void ClipRedPavingAtThreshold(MeshCollider threshold)
    {
        var redObject = GameObject.Find("Скруглённое мощение · 1");
        var red = redObject ? redObject.GetComponent<MeshFilter>() : null;
        var redCollider = redObject ? redObject.GetComponent<MeshCollider>() : null;
        if (!red || !red.sharedMesh || !redCollider || redCollider.sharedMesh != red.sharedMesh)
            throw new InvalidOperationException("The saved red pavement must share its mesh with its collider.");
        const string oldMesh = "CourtTrim_2127949119";
        string output = $"{Root}/Meshes/CourtRetained_{GlobalObjectId.GetGlobalObjectIdSlow(red).targetObjectId}.asset";
        if (AssetDatabase.GetAssetPath(red.sharedMesh) == output)
            return;
        if (red.sharedMesh.name != oldMesh ||
            !redObject.GetComponent<MeshRenderer>().sharedMaterials.Any(m => m && m.name == "ResidentialAccessPaving"))
            throw new InvalidOperationException("Unexpected source geometry for the red sliver beside No.7.");

        // Clip only the red paving below the existing grey landing. The sloped
        // landing and adjacent red walkway remain at their authored elevations.
        Physics.SyncTransforms();
        var bounds = threshold.bounds;
        var min = bounds.min;
        var max = bounds.max;
        if (Mathf.Abs(min.x - ApproachJoinX) > .01f || Mathf.Abs(bounds.size.x - 2.4f) > .01f ||
            Mathf.Abs(bounds.size.z - 2.4f) > .01f || Mathf.Abs(bounds.center.z - Entrance.z) > .01f)
            throw new InvalidOperationException("The No.7 landing no longer matches the inspected footprint.");
        var cut = new List<Vector2> {
            new Vector2(min.x, min.z), new Vector2(max.x, min.z),
            new Vector2(max.x, max.z), new Vector2(min.x, max.z)
        };
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        PhotoCentralCourtyardStage04.RemoveCoveredHardscape(
            new[] { red }, new List<List<Vector2>> { cut }, Root);
        if (AssetDatabase.GetAssetPath(red.sharedMesh) != output || red.sharedMesh != redCollider.sharedMesh)
            throw new InvalidOperationException("The overlapping red paving was not cut cleanly.");
        Debug.Log("STREET_COURT_DETAIL removed red overlap under the saved No.7 landing.");
    }

    private static float Height(MeshCollider collider, float x, float z)
    {
        var ray = new Ray(new Vector3(x, 1.3f, z), Vector3.down);
        if (!collider.Raycast(ray, out var hit, 2) || hit.normal.y < .9f)
            throw new InvalidOperationException($"No walkable No.7 pavement at ({x:F3}, {z:F3}).");
        return hit.point.y;
    }

    private static Material FindMaterial(Renderer renderer, string name) =>
        renderer.sharedMaterials.First(material => material && material.name == name);

    private static Material Finish(Material source, string name, Color tint, float metallic)
    {
        string path = $"{Root}/Materials/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(source);
            AssetDatabase.CreateAsset(material, path);
        }
        material.name = name;
        material.SetColor("_Color", tint);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_RoughnessBias", .22f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Vector3 P(float across, float y, float depth) => Entrance + Right * across + Vector3.up * y + Forward * depth;

    private static void AddTrim(EnvironmentMesh mesh, Material stone, Material edging, Material metal, int lod)
    {
        void Box(Material material, float across, float y, float depth, float width, float height, float thickness) =>
            mesh.Box(material, P(across, y, depth), new Vector3(width, height, thickness), Rotation);

        // A second, thinner outer casing frames the existing recessed door jamb.
        foreach (int side in new[] { -1, 1 })
        {
            Box(stone, side * .897f, 1.29f, Wall + .024f, .075f, 2.22f, .038f);
            Box(edging, side * 2.19f, 2.52f, Wall + .472f, .024f, .34f, .011f);
            Box(edging, side * 1.84f, 2.371f, Wall + .475f, .93f, .016f, .012f);
            if (lod == 0)
            {
                foreach (float y in new[] { 2.43f, 2.55f })
                    mesh.Cylinder(metal, P(side * 2.15f, y, Wall + .470f), P(side * 2.15f, y, Wall + .481f), .011f, sides: 8);
            }
        }
        Box(stone, 0, 2.415f, Wall + .024f, 1.869f, .067f, .038f);
        Box(edging, 0, 2.744f, Wall + .814f, 5.20f, .022f, .018f);
        Box(edging, 0, 2.944f, Wall + .814f, 5.20f, .022f, .018f);
        Box(metal, 0, .190f, Wall + .027f, 1.79f, .009f, .23f);
        if (lod == 0)
        {
            // Millimetre high grip ribs stay within the original sill footprint.
            foreach (float depth in new[] { .095f, .114f, .133f })
                Box(edging, 0, .195f, Wall + depth, 1.72f, .003f, .006f);
        }
    }
}
