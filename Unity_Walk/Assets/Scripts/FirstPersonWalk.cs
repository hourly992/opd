using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class FirstPersonWalk : MonoBehaviour
{
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float runSpeed = 6.2f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float gravity = -20f;
    [SerializeField, Range(0f, .02f)] private float walkBobHeight = .009f;
    [SerializeField, Range(1f, 1.5f)] private float runBobMultiplier = 1.3f;

    private CharacterController controller;
    [SerializeField] private Transform view;
    private float pitch;
    private float verticalSpeed;
    private bool hasSession;
    private Transform sampledView;
    private Vector3 neutralViewPosition;
    private float viewYaw, viewRoll;
    private float bobPhase, bobEnvelope, runEnvelope;

    public void SetCamera(Transform cameraTransform)
    {
        ResetBob();
        view = cameraTransform;
        CaptureView();
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        // Runtime builders add the session after adding this controller.
        hasSession = GetComponent<StreetSession>() != null;
        LockCursor();
    }

    private void OnEnable()
    {
        CaptureView();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0)
                 && !hasSession)
        {
            LockCursor();
        }

        if (view != sampledView)
        {
            ResetBob();
            CaptureView();
        }
        float dt = Time.deltaTime;
        bool hasControl = Cursor.lockState == CursorLockMode.Locked && Application.isFocused && dt > 0f;
        if (hasControl && view != null)
        {
            transform.Rotate(Vector3.up * (Input.GetAxisRaw("Mouse X") * mouseSensitivity));
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -85f, 85f);
        }

        float x = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
        float z = (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1f : 0f)
                - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1f : 0f);
        Vector3 direction = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
        if (!hasControl)
            direction = Vector3.zero;

        if (!controller || !controller.enabled)
        {
            ResetBob();
            return;
        }
        bool running = hasControl && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
        float speed = running ? Mathf.Max(walkSpeed, runSpeed) : walkSpeed;
        if (controller.isGrounded && verticalSpeed < 0f)
            verticalSpeed = -2f;
        verticalSpeed += gravity * dt;
        Vector3 previousPosition = transform.position;
        CollisionFlags collision = controller.Move((direction * speed + Vector3.up * verticalSpeed) * dt);
        Vector3 moved = transform.position - previousPosition;
        moved.y = 0f;
        bool grounded = controller.isGrounded || (collision & CollisionFlags.Below) != 0;
        bool moving = hasControl && grounded && direction.sqrMagnitude > .0001f && moved.sqrMagnitude > .00000001f;
        UpdateCameraMotion(moved.magnitude, moving, running, hasControl, dt);
    }

    private void CaptureView()
    {
        sampledView = view;
        bobPhase = bobEnvelope = runEnvelope = 0f;
        if (!sampledView) return;
        neutralViewPosition = sampledView.localPosition;
        Vector3 angles = sampledView.localEulerAngles;
        pitch = Mathf.DeltaAngle(0f, angles.x);
        viewYaw = Mathf.DeltaAngle(0f, angles.y);
        viewRoll = Mathf.DeltaAngle(0f, angles.z);
    }

    private void UpdateCameraMotion(float distance, bool moving, bool running, bool hasControl, float dt)
    {
        if (!sampledView) return;
        if (!hasControl)
        {
            ResetBob();
            return;
        }
        bobEnvelope = Mathf.MoveTowards(bobEnvelope, moving ? 1f : 0f, dt * 6f);
        runEnvelope = Mathf.MoveTowards(runEnvelope, moving && running ? 1f : 0f, dt * 5f);
        if (moving)
        {
            // Measured travel drives the phase: holding W against a wall cannot
            // shake the view. Frequencies at full speed are 1.65 / 2.1 Hz.
            float cyclesPerMetre = Mathf.Lerp(1.65f / Mathf.Max(.1f, walkSpeed),
                2.1f / Mathf.Max(.1f, runSpeed), runEnvelope);
            bobPhase = Mathf.Repeat(bobPhase + distance * cyclesPerMetre * Mathf.PI * 2f, Mathf.PI * 4f);
        }
        if (bobEnvelope <= 0f)
        {
            ResetBob();
            return;
        }
        float amount = bobEnvelope * Mathf.Lerp(1f, runBobMultiplier, runEnvelope);
        float sway = Mathf.Sin(bobPhase * .5f);
        float rise = Mathf.Sin(bobPhase);
        sampledView.localPosition = neutralViewPosition +
            new Vector3(sway * .0024f, rise * walkBobHeight, 0f) * amount;
        // No random shake, zoom pulses or large roll: less than 0.08 degrees
        // even while running with the default settings.
        sampledView.localRotation = Quaternion.Euler(pitch + rise * .025f * amount,
            viewYaw, viewRoll + sway * .055f * amount);
    }

    private void ResetBob()
    {
        bobPhase = bobEnvelope = runEnvelope = 0f;
        if (!sampledView) return;
        sampledView.localPosition = neutralViewPosition;
        sampledView.localRotation = Quaternion.Euler(pitch, viewYaw, viewRoll);
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
        {
            // QA disables this component before positioning the camera.
            if (isActiveAndEnabled) ResetBob();
            ReleaseCursor();
        }
    }

    private void OnDisable()
    {
        ResetBob();
        ReleaseCursor();
    }

    private static void ReleaseCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
