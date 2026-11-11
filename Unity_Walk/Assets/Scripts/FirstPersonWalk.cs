using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalk : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float gravity = -20f;

    private CharacterController controller;
    [SerializeField] private Transform view;
    private float pitch;
    private float verticalSpeed;

    public void SetCamera(Transform cameraTransform)
    {
        view = cameraTransform;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        LockCursor();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)
                 && GetComponent<StreetSession>() == null)
        {
            LockCursor();
        }

        if (Cursor.lockState == CursorLockMode.Locked && view != null)
        {
            transform.Rotate(Vector3.up * (Input.GetAxisRaw("Mouse X") * mouseSensitivity));
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -85f, 85f);
            view.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        float z = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        Vector3 direction = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
        if (Cursor.lockState != CursorLockMode.Locked || !Application.isFocused)
            direction = Vector3.zero;

        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;
        verticalSpeed += gravity * Time.deltaTime;
        controller.Move((direction * walkSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            ReleaseCursor();
    }

    private void OnDisable()
    {
        ReleaseCursor();
    }

    private static void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
