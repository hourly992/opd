using UnityEngine;

// Create a small test world when Play starts.
public class WalkScene : MonoBehaviour
{
    private void Awake()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Floor");

        // Keep the walkable area enclosed so the player cannot fall off it.
        CreateWall(new Vector3(0f, 1.5f, 50f), new Vector3(102f, 3f, 1f));
        CreateWall(new Vector3(0f, 1.5f, -50f), new Vector3(102f, 3f, 1f));
        CreateWall(new Vector3(50f, 1.5f, 0f), new Vector3(1f, 3f, 100f));
        CreateWall(new Vector3(-50f, 1.5f, 0f), new Vector3(1f, 3f, 100f));

        // Simple landmarks make movement visible and provide collision testing.
        CreateBlock(new Vector3(0f, 1f, 8f));
        CreateBlock(new Vector3(-6f, 1f, 12f));
        CreateBlock(new Vector3(6f, 1f, 12f));

        GameObject sun = new GameObject("Directional Light");
        Light lightComponent = sun.AddComponent<Light>();
        lightComponent.type = LightType.Directional;
        lightComponent.intensity = 1f;
        sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        GameObject player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 1.05f, 0f);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.35f;

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 0.65f, 0f);
        cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        player.AddComponent<FirstPersonWalk>().SetCamera(cameraObject.transform);
    }

    private static void CreateBlock(Vector3 position)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = "Landmark";
        block.transform.position = position;
        block.transform.localScale = Vector3.one * 2f;
        block.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Block");
    }

    private static void CreateWall(Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Boundary";
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<MeshRenderer>().sharedMaterial = Resources.Load<Material>("Wall");
    }
}
