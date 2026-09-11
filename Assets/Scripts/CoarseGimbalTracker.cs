using UnityEngine;

public class CoarseGimbalTracker : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Automatically follows the satellite selected in the simulator dropdown.")]
    public Transform targetSatellite;

    [Header("Gimbal")]
    public Transform pitchGimbal;

    [Header("Tracking Settings")]
    public float yawSpeed = 30f;
    public float pitchSpeed = 30f;

    [Header("Angle Limits")]
    public float minPitch = -60f;
    public float maxPitch = 60f;

    [Header("Axis Settings")]
    public bool invertYaw = false;
    public bool invertPitch = false;

    private float currentYaw;
    private float currentPitch;

    private CameraController cameraController;

    void Start()
    {
        // Get the simulator's existing camera controller.
        cameraController = FindFirstObjectByType<CameraController>();

        // Start from the gimbal's current rotation.
        currentYaw = transform.localEulerAngles.y;
        currentPitch = pitchGimbal.localEulerAngles.x;

        // Convert Unity's 0-360 representation into -180 to +180.
        if (currentYaw > 180f)
            currentYaw -= 360f;

        if (currentPitch > 180f)
            currentPitch -= 360f;

        // Connect to the currently selected satellite.
        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged += OnTrackedBodyChanged;

            if (cameraController.CurrentBody != null)
            {
                targetSatellite =
                    cameraController.CurrentBody.transform;
            }
        }
        else
        {
            Debug.LogWarning(
                "[CoarseGimbalTracker] CameraController not found."
            );
        }
    }

    void OnDestroy()
    {
        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged -= OnTrackedBodyChanged;
        }
    }

    void Update()
    {
        // If the simulator hasn't selected a satellite yet,
        // keep checking for the CameraController's current target.
        if (targetSatellite == null && cameraController != null)
        {
            if (cameraController.CurrentBody != null)
            {
                targetSatellite =
                    cameraController.CurrentBody.transform;
            }
        }

        if (targetSatellite == null || pitchGimbal == null)
            return;

        TrackTarget();
    }

    private void OnTrackedBodyChanged(NBody newTarget)
    {
        if (newTarget == null)
        {
            targetSatellite = null;
            return;
        }

        targetSatellite = newTarget.transform;

        Debug.Log(
            "[CoarseGimbalTracker] Tracking: " +
            newTarget.name
        );
    }

    void TrackTarget()
    {
        // ---------------------------------------------------------
        // 1. FIND TARGET DIRECTION
        // ---------------------------------------------------------

        Vector3 targetDirection =
            targetSatellite.position - transform.position;

        if (targetDirection.sqrMagnitude < 0.001f)
            return;

        targetDirection.Normalize();


        // ---------------------------------------------------------
        // 2. YAW CALCULATION
        // ---------------------------------------------------------

        Vector3 localDirection =
            transform.parent != null
            ? transform.parent.InverseTransformDirection(targetDirection)
            : targetDirection;

        float desiredYaw =
            Mathf.Atan2(
                localDirection.x,
                localDirection.z
            ) * Mathf.Rad2Deg;

        if (invertYaw)
            desiredYaw = -desiredYaw;


        // ---------------------------------------------------------
        // 3. MOVE Y GIMBAL
        // ---------------------------------------------------------

        currentYaw = Mathf.MoveTowardsAngle(
            currentYaw,
            desiredYaw,
            yawSpeed * Time.deltaTime
        );

        transform.localRotation =
            Quaternion.Euler(
                0f,
                currentYaw,
                0f
            );


        // ---------------------------------------------------------
        // 4. FIND TARGET DIRECTION AFTER YAW
        // ---------------------------------------------------------

        Vector3 pitchDirection =
            pitchGimbal.InverseTransformDirection(
                targetSatellite.position -
                pitchGimbal.position
            );

        if (pitchDirection.sqrMagnitude < 0.001f)
            return;

        pitchDirection.Normalize();


        // ---------------------------------------------------------
        // 5. PITCH CALCULATION
        // ---------------------------------------------------------

        float desiredPitch =
            -Mathf.Atan2(
                pitchDirection.y,
                pitchDirection.z
            ) * Mathf.Rad2Deg;

        if (invertPitch)
            desiredPitch = -desiredPitch;


        // ---------------------------------------------------------
        // 6. LIMIT PITCH
        // ---------------------------------------------------------

        desiredPitch = Mathf.Clamp(
            desiredPitch,
            minPitch,
            maxPitch
        );


        // ---------------------------------------------------------
        // 7. MOVE X GIMBAL
        // ---------------------------------------------------------

        currentPitch = Mathf.MoveTowardsAngle(
            currentPitch,
            desiredPitch,
            pitchSpeed * Time.deltaTime
        );

        pitchGimbal.localRotation =
            Quaternion.Euler(
                currentPitch,
                0f,
                0f
            );
    }
}