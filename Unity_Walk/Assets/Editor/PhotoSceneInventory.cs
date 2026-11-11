using System;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

// Read-only snapshot of the authored scene. No generator or source-map files are used.
public static class PhotoSceneInventory
{
    [Serializable] private sealed class Pose
    {
        public string name, hierarchy;
        public Vector3 position, rotationEuler, forward, scale;
    }
    [Serializable] private sealed class Building
    {
        public string name, address;
        public long legacyId;
        public Pose transform;
        public Bounds worldBounds;
        public Pose[] accessMarkers;
    }
    [Serializable] private sealed class Playground
    {
        public string name;
        public long legacyId;
        public bool equipmentPhotoConfirmed;
        public Bounds worldBounds;
        public Vector3[] footprint;
        public Pose[] equipment, cameras;
    }
    [Serializable] private sealed class Road
    {
        public string name;
        public float streetLength;
        public Vector3[] walkingChecks, transitionChecks, junctionChecks;
    }
    [Serializable] private sealed class Inventory
    {
        public string scene, engine, checkedUtc;
        public Building[] buildings;
        public Pose pilotBlock, house5;
        public Bounds house5WorldBounds;
        public Pose[] house5Entrances, pilotCameras;
        public Playground[] playgrounds;
        public Road[] roads;
        public int renderers;
    }

    private static Pose Snapshot(Transform transform)
    {
        if (!transform) return null;
        string hierarchy = transform.name;
        for (var parent = transform.parent; parent; parent = parent.parent) hierarchy = parent.name + "/" + hierarchy;
        return new Pose { name = transform.name, hierarchy = hierarchy, position = transform.position,
            rotationEuler = transform.eulerAngles, forward = transform.forward, scale = transform.lossyScale };
    }

    private static Bounds WorldBounds(Transform transform)
    {
        if (!transform) return new Bounds();
        var renderers = transform.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(transform.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        return bounds;
    }

    public static void Export()
    {
        const string path = "Assets/Scenes/Street.unity";
        if (EditorSceneManager.GetActiveScene().path != path)
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save the open scene before exporting the Street inventory.");
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
        var pilot = UnityEngine.Object.FindAnyObjectByType<PilotBlock>();
        var transforms = UnityEngine.Object.FindObjectsByType<Transform>();
        var house = transforms.FirstOrDefault(t => t.name.StartsWith("Дом №5", StringComparison.Ordinal));
        var inventory = new Inventory {
            scene = path, engine = Application.unityVersion, checkedUtc = DateTime.UtcNow.ToString("O"),
            buildings = UnityEngine.Object.FindObjectsByType<StreetBuilding>().OrderBy(b => b.address).ThenBy(b => b.osmId)
                .Select(b => new Building { name = b.name, address = b.address, legacyId = b.osmId,
                    transform = Snapshot(b.transform), worldBounds = WorldBounds(b.transform),
                    accessMarkers = b.GetComponentsInChildren<Transform>().Where(t => t.name.StartsWith("Access · ", StringComparison.Ordinal))
                        .Select(Snapshot).ToArray() }).ToArray(),
            pilotBlock = pilot ? Snapshot(pilot.transform) : null, house5 = Snapshot(house), house5WorldBounds = WorldBounds(house),
            house5Entrances = pilot ? pilot.entrances.Select(Snapshot).ToArray() : Array.Empty<Pose>(),
            pilotCameras = pilot ? pilot.inspectionViews.Select(Snapshot).ToArray() : Array.Empty<Pose>(),
            playgrounds = UnityEngine.Object.FindObjectsByType<PlaygroundSite>().OrderBy(p => p.osmId)
                .Select(p => new Playground { name = p.name, legacyId = p.osmId, equipmentPhotoConfirmed = p.equipmentPhotoConfirmed,
                    worldBounds = p.worldBounds, footprint = p.footprint, equipment = p.equipment.Select(Snapshot).ToArray(),
                    cameras = p.inspectionViews.Select(Snapshot).ToArray() }).ToArray(),
            roads = UnityEngine.Object.FindObjectsByType<StreetRoadNetwork>().Select(r => new Road { name = r.name,
                streetLength = r.streetLength, walkingChecks = r.walkingChecks, transitionChecks = r.transitionProbePoints,
                junctionChecks = r.junctionProbePoints }).ToArray(),
            renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>().Length
        };
        Directory.CreateDirectory("street_qa");
        File.WriteAllText("street_qa/photo_scene_inventory.json", JsonUtility.ToJson(inventory, true));
        SavePlan();
        Debug.Log("PHOTO_SCENE_INVENTORY buildings=" + inventory.buildings.Length + " playgrounds=" + inventory.playgrounds.Length);
    }

    private static void SavePlan()
    {
        const int width = 1600, height = 1728;
        var previous = RenderTexture.active;
        var cameraObject = new GameObject("Temporary photo inventory camera") { hideFlags = HideFlags.HideAndDontSave };
        var camera = cameraObject.AddComponent<Camera>();
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = null;
        try
        {
            camera.transform.position = new Vector3(50, 500, -55);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            camera.orthographic = true; camera.orthographicSize = 205; camera.aspect = (float)width / height;
            camera.nearClipPlane = .1f; camera.farClipPlane = 1000;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.3f, .35f, .3f);
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes("street_qa/photo_scene_plan.png", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            if (image) UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
