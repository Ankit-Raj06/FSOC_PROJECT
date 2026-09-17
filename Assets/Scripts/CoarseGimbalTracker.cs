using UnityEngine;

/// <summary>
/// Coarse acquisition controller for the FSOC gimbal.
///
/// Complementary to PATController:
/// - YOLO has a valid track -> PAT owns the gimbal.
/// - YOLO has no valid track -> this controller performs coarse
///   acquisition using the simulator's target position.
/// - YOLO reacquires -> PAT is handed control again.
///
/// The two controllers never write the gimbal at the same time.
/// </summary>
public class CoarseGimbalTracker : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Automatically follows the satellite selected in the simulator dropdown.")]
    public Transform targetSatellite;

    [Header("PAT Coordination")]
    [Tooltip("PATController that takes over after YOLO reacquires the target.")]
    public PATController patController;

    [Tooltip("Tracker used to determine whether YOLO currently has a valid target.")]
    public Tracker tracker;

    [Tooltip("Disable PAT while coarse acquisition is active, then restore it on YOLO reacquisition.")]
    public bool handOffToPAT = true;

    [Header("Gimbal")]
    [Tooltip("Yaw transform, normally CoarseGimbal_Y.")]
    public Transform yawGimbal;

    [Tooltip("Pitch transform, normally CoarseGimbal_X.")]
    public Transform pitchGimbal;

    [Header("Tracking Settings")]
    public float yawSpeed = 30f;
    public float pitchSpeed = 30f;

    [Header("Angle Limits")]
    public float minYaw = -80f;
    public float maxYaw = 80f;
    public float minPitch = -60f;
    public float maxPitch = 60f;

    [Header("Axis Settings")]
    public bool invertYaw = false;
    public bool invertPitch = false;

    [Header("Acquisition")]
    [Tooltip("Only use coarse pointing while YOLO has no valid detection.")]
    public bool acquireOnlyWhenPATHasNoTrack = true;

    [Tooltip("Ignore extremely small angular errors to prevent jitter.")]
    public float angularDeadband = 0.15f;

    private float currentYaw;
    private float currentPitch;

    private CameraController cameraController;
    private bool coarseActive;
    private bool previousTrackState;

    private void Start()
    {
        cameraController = FindFirstObjectByType<CameraController>();

        if (yawGimbal == null)
            yawGimbal = transform;

        // Initialize state from the actual transforms.
        SyncGimbalState();

        if (cameraController != null)
        {
            cameraController.OnTrackedBodyChanged += OnTrackedBodyChanged;

            if (cameraController.CurrentBody != null)
                targetSatellite = cameraController.CurrentBody.transform;
        }
        else
        {
            Debug.LogWarning(
                "[CoarseGimbalTracker] CameraController not found. " +
                "Assign targetSatellite manually."
            );
        }

        previousTrackState = HasPATTrack();
        UpdateControllerOwnership(previousTrackState);
    }

    private void OnDestroy()
    {
        if (cameraController != null)
            cameraController.OnTrackedBodyChanged -= OnTrackedBodyChanged;

        // Do not leave PAT disabled if this component is removed.
        if (patController != null && !patController.enabled)
            patController.enabled = true;
    }

    private void Update()
    {
        if (targetSatellite == null && cameraController != null)
        {
            if (cameraController.CurrentBody != null)
                targetSatellite = cameraController.CurrentBody.transform;
        }

        bool hasTrack = HasPATTrack();

        if (hasTrack != previousTrackState)
        {
            UpdateControllerOwnership(hasTrack);
            previousTrackState = hasTrack;
        }

        // PAT owns the gimbal whenever YOLO has a valid detection.
        if (hasTrack)
            return;

        if (targetSatellite == null ||
            yawGimbal == null ||
            pitchGimbal == null)
            return;

        TrackTargetCoarsely();
    }

    private bool HasPATTrack()
    {
        return tracker != null &&
               tracker.enabled &&
               tracker.targetDetected;
    }

    private void UpdateControllerOwnership(bool hasTrack)
    {
        coarseActive =
            acquireOnlyWhenPATHasNoTrack
                ? !hasTrack
                : true;

        if (!handOffToPAT || patController == null)
            return;

        // Only one controller writes the gimbal at a time.
        if (coarseActive)
        {
            // Synchronize immediately when coarse acquisition takes ownership.
            SyncGimbalState();

            if (patController.enabled)
            {
                patController.enabled = false;

                Debug.Log(
                    "[CoarseGimbalTracker] YOLO track lost -> " +
                    "coarse acquisition active, PAT paused."
                );
            }
        }
        else
        {
            if (!patController.enabled)
            {
                patController.enabled = true;

                Debug.Log(
                    "[CoarseGimbalTracker] YOLO reacquired target -> " +
                    "PAT control restored."
                );
            }
        }
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
            "[CoarseGimbalTracker] Acquisition target: " +
            newTarget.name
        );
    }

    /// <summary>
    /// Reads the ACTUAL gimbal transforms.
    ///
    /// The Transform is the single source of truth because PATController
    /// can also modify these transforms during normal tracking.
    /// </summary>
    private void SyncGimbalState()
    {
        if (yawGimbal != null)
        {
            currentYaw =
                NormalizeAngle(yawGimbal.localEulerAngles.y);
        }

        if (pitchGimbal != null)
        {
            currentPitch =
                NormalizeAngle(pitchGimbal.localEulerAngles.x);
        }
    }

    private void TrackTargetCoarsely()
    {
        if (!coarseActive)
            return;

        // ---------------------------------------------------------
        // IMPORTANT:
        // The actual Transform is authoritative.
        //
        // PATController may have changed the gimbal since our last
        // frame, so synchronize before calculating any new command.
        // ---------------------------------------------------------

        SyncGimbalState();

        // ---------------------------------------------------------
        // 1. TARGET DIRECTION
        // ---------------------------------------------------------

        Vector3 targetDirection =
            targetSatellite.position - yawGimbal.position;

        if (targetDirection.sqrMagnitude < 0.000001f)
            return;

        targetDirection.Normalize();

        // ---------------------------------------------------------
        // 2. YAW
        //
        // Calculate the target direction RELATIVE TO THE CURRENT
        // YAW GIMBAL ORIENTATION.
        //
        // This gives us an angular ERROR, not an absolute yaw.
        //
        // Therefore:
        //
        //     desiredYaw = currentYaw + yawError
        //
        // This avoids mixing the parent's coordinate frame with
        // the yaw gimbal's actual rotation frame.
        // ---------------------------------------------------------

        Vector3 localDirection =
            yawGimbal.InverseTransformDirection(targetDirection);

        float yawError =
            Mathf.Atan2(
                localDirection.x,
                localDirection.z
            ) * Mathf.Rad2Deg;

        yawError = Mathf.DeltaAngle(0f, yawError);

        // ---------------------------------------------------------
        // Target is behind the gimbal.
        //
        // At exactly 180 degrees, +180 and -180 are physically
        // identical but numerically opposite. Because the gimbal
        // has a limited range, choose whichever limit is closer
        // to the CURRENT gimbal position.
        //
        // This prevents tiny floating-point changes around 180°
        // from commanding the gimbal to suddenly switch sides.
        // ---------------------------------------------------------

        float desiredYaw;

        if (Mathf.Abs(yawError) > 90f)
        {
            float distanceToMin =
                Mathf.Abs(Mathf.DeltaAngle(currentYaw, minYaw));

            float distanceToMax =
                Mathf.Abs(Mathf.DeltaAngle(currentYaw, maxYaw));

            desiredYaw =
                distanceToMin <= distanceToMax
                    ? minYaw
                    : maxYaw;
        }
        else
        {
            desiredYaw =
                currentYaw + yawError;
        }

        if (invertYaw)
        {
            float invertedError = -yawError;

            if (Mathf.Abs(invertedError) > 90f)
            {
                float distanceToMin =
                    Mathf.Abs(Mathf.DeltaAngle(currentYaw, minYaw));

                float distanceToMax =
                    Mathf.Abs(Mathf.DeltaAngle(currentYaw, maxYaw));

                desiredYaw =
                    distanceToMin <= distanceToMax
                        ? minYaw
                        : maxYaw;
            }
            else
            {
                desiredYaw =
                    currentYaw + invertedError;
            }
        }

        desiredYaw =
            Mathf.Clamp(
                desiredYaw,
                minYaw,
                maxYaw
            );

        Debug.Log(
            $"COARSE YAW | " +
            $"Target={targetSatellite.name} " +
            $"LocalDir={localDirection} " +
            $"YawError={yawError:F2} " +
            $"Current={currentYaw:F2} " +
            $"Desired={desiredYaw:F2} " +
            $"Actual={NormalizeAngle(yawGimbal.localEulerAngles.y):F2}"
        );

        // ---------------------------------------------------------
        // MOVE YAW
        // ---------------------------------------------------------

        if (Mathf.Abs(
                Mathf.DeltaAngle(currentYaw, desiredYaw)
            ) > angularDeadband)
        {
            currentYaw =
                Mathf.MoveTowardsAngle(
                    currentYaw,
                    desiredYaw,
                    yawSpeed * Time.deltaTime
                );

            currentYaw =
                Mathf.Clamp(
                    currentYaw,
                    minYaw,
                    maxYaw
                );

            yawGimbal.localRotation =
                Quaternion.Euler(
                    0f,
                    currentYaw,
                    0f
                );
        }

        // ---------------------------------------------------------
        // 3. PITCH
        //
        // Same principle as yaw:
        // calculate the target relative to the CURRENT pitch
        // gimbal orientation and treat the result as an error.
        // ---------------------------------------------------------

        Vector3 pitchDirection =
            pitchGimbal.InverseTransformDirection(
                targetSatellite.position -
                pitchGimbal.position
            );

        if (pitchDirection.sqrMagnitude < 0.000001f)
            return;

        pitchDirection.Normalize();

        float pitchError =
            -Mathf.Atan2(
                pitchDirection.y,
                pitchDirection.z
            ) * Mathf.Rad2Deg;

        pitchError =
            Mathf.DeltaAngle(
                0f,
                pitchError
            );

        float desiredPitch =
            currentPitch + pitchError;

        if (invertPitch)
            desiredPitch =
                currentPitch - pitchError;

        desiredPitch =
            Mathf.Clamp(
                desiredPitch,
                minPitch,
                maxPitch
            );

        Debug.Log(
            $"COARSE PITCH | " +
            $"Target={targetSatellite.name} " +
            $"PitchDir={pitchDirection} " +
            $"PitchError={pitchError:F2} " +
            $"Current={currentPitch:F2} " +
            $"Desired={desiredPitch:F2} " +
            $"Actual={NormalizeAngle(pitchGimbal.localEulerAngles.x):F2}"
        );

        // ---------------------------------------------------------
        // MOVE PITCH
        // ---------------------------------------------------------

        if (Mathf.Abs(
                Mathf.DeltaAngle(currentPitch, desiredPitch)
            ) > angularDeadband)
        {
            currentPitch =
                Mathf.MoveTowardsAngle(
                    currentPitch,
                    desiredPitch,
                    pitchSpeed * Time.deltaTime
                );

            currentPitch =
                Mathf.Clamp(
                    currentPitch,
                    minPitch,
                    maxPitch
                );

            pitchGimbal.localRotation =
                Quaternion.Euler(
                    currentPitch,
                    0f,
                    0f
                );
        }

        // ---------------------------------------------------------
        // FINAL STATE CHECK
        // ---------------------------------------------------------

        Debug.Log(
            $"AFTER WRITE | " +
            $"Yaw State={currentYaw:F2} " +
            $"Yaw Actual={NormalizeAngle(yawGimbal.localEulerAngles.y):F2} | " +
            $"Pitch State={currentPitch:F2} " +
            $"Pitch Actual={NormalizeAngle(pitchGimbal.localEulerAngles.x):F2}"
        );
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
