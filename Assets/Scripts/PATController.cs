using UnityEngine;

/// <summary>
/// Integrated Search-Acquisition + PAT (Point-Acquire-Track) controller.
///
/// SEARCH MODE
///   Sweeps the gimbal until the tracker reports a target detection.
///
/// PAT MODE
///   Uses the detected/predicted image position to steer the camera/gimbal.
///
/// LASER
///   The laser is ALWAYS aligned with the tracking camera's forward axis.
///   This means the laser remains visible during Search, PAT, and
///   temporary target-loss/reacquisition periods.
///
/// IMPORTANT:
///   The laser does NOT use the true satellite position for pointing.
///   It only follows the camera/gimbal direction.
/// </summary>
public class PATController : MonoBehaviour
{
    [Header("References")]

    [Tooltip("Tracker providing target detection and predicted viewport position.")]
    public Tracker tracker;

    [Tooltip("Physical origin of the optical laser.")]
    public Transform laserOrigin;

    [Tooltip("Movable tracking camera.")]
    public Transform cameraTransform;

    [Tooltip("Optional satellite reference. Used ONLY for optional beam length.")]
    public Transform targetSatellite;


    [Header("Gimbal")]

    [Tooltip("Yaw gimbal transform.")]
    public Transform coarseGimbalY;

    [Tooltip("Pitch gimbal transform.")]
    public Transform coarseGimbalX;


    [Header("PAT - Control")]

    [Tooltip("Camera/gimbal angular movement speed. Requirement target: 5-10 deg/s.")]
    public float rotationSpeed = 8f;

    [Tooltip("Pixels of image-space error considered locked.")]
    public float lockTolerancePixels = 10f;


    [Header("PAT - Beam Output")]

    [Tooltip("Controls the visual laser.")]
    public LaserCylinderBeam laserBeamController;

    [Tooltip("Laser length when target-distance mode is disabled.")]
    public float predictionDistance = 100f;

    [Tooltip("If enabled, beam length is based on the satellite distance projected along the camera axis.")]
    public bool useTargetDistanceForBeamLength = false;


    [Header("PAT - Reacquisition Hold")]

    [Tooltip("How long PAT holds the gimbal after detection is temporarily lost.")]
    public float reacquisitionHoldTime = 1.0f;


    [Header("PAT - Gimbal Limits")]

    public float horizontalLimit = 180f;
    public float verticalLimit = 90f;


    [Header("Search - Sweep")]

    [Tooltip("Search sweep angular speed.")]
    public float scanSpeed = 30f;

    [Tooltip("Maximum search yaw.")]
    public float searchYawRange = 180f;

    [Tooltip("Maximum search pitch.")]
    public float searchPitchRange = 90f;

    [Tooltip("Pitch increment between horizontal search rows.")]
    public float pitchStepDegrees = 2.5f;

    [Tooltip("Distance from a search waypoint before moving to the next row.")]
    public float waypointTolerance = 1f;


    [Header("Axis Settings")]

    [Tooltip("Enable if yaw moves opposite to the target.")]
    public bool invertYaw = false;

    [Tooltip("Enable if pitch moves opposite to the target.")]
    public bool invertPitch = false;


    [Header("Debug")]

    public bool debugLogs = false;


    // ---------------------------------------------------------
    // PUBLIC TELEMETRY
    // ---------------------------------------------------------

    public string CurrentModeName
    {
        get { return GimbalOwner.Current.ToString(); }
    }

    /// <summary>
    /// Current image-space alignment error in pixels.
    /// </summary>
    public float CurrentAlignmentErrorPixels { get; private set; }

    /// <summary>
    /// True when the target is detected and is within the
    /// configured pixel lock tolerance.
    /// </summary>
    public bool IsTargetLocked { get; private set; }

    /// <summary>
    /// Current predicted/detected viewport position.
    /// </summary>
    public Vector2 CurrentTrackingPosition { get; private set; }


    // ---------------------------------------------------------
    // PRIVATE VARIABLES
    // ---------------------------------------------------------

    private Camera cam;

    private float trackLostTimer = 0f;

    private float searchYaw;
    private float searchPitch;

    private int searchYawDir = 1;

    private bool wasSearching = false;

    private float searchStartTime = 0f;

    private int sweepPassCount = 0;

    private bool axisDiagLogged = false;


    // ---------------------------------------------------------
    // UNITY
    // ---------------------------------------------------------

    private void Awake()
    {
        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                Debug.LogWarning(
                    "[PATController] cameraTransform does not have a Camera component."
                );
            }
        }
        else
        {
            Debug.LogWarning(
                "[PATController] Camera Transform is not assigned."
            );
        }
    }


    private void Start()
    {
        if (coarseGimbalY != null)
        {
            searchYaw =
                NormalizeAngle(coarseGimbalY.localEulerAngles.y);
        }

        searchPitch = -searchPitchRange;

        // Initialize laser immediately.
        UpdateLaserBeam();

        if (debugLogs)
        {
            Debug.Log(
                "[PATController] Initialised. Starting Search mode."
            );
        }
    }


    private void OnDisable()
    {
        if (GimbalOwner.Current == GimbalController.PAT ||
            GimbalOwner.Current == GimbalController.Search)
        {
            GimbalOwner.Current = GimbalController.None;
        }

        wasSearching = false;
        IsTargetLocked = false;
    }


    private void Update()
    {
        if (tracker == null ||
            !tracker.enabled ||
            cameraTransform == null ||
            coarseGimbalY == null ||
            coarseGimbalX == null ||
            cam == null)
        {
            // Keep laser alive even if PAT references are incomplete.
            UpdateLaserBeam();
            return;
        }


        // -----------------------------------------------------
        // TARGET DETECTED
        // -----------------------------------------------------

        if (tracker.targetDetected)
        {
            RunPAT();
        }


        // -----------------------------------------------------
        // NO TARGET
        // -----------------------------------------------------

        else
        {
            IsTargetLocked = false;
            CurrentAlignmentErrorPixels = 0f;

            if (GimbalOwner.Current == GimbalController.PAT)
            {
                trackLostTimer += Time.deltaTime;

                // Hold current gimbal position temporarily.
                if (trackLostTimer < reacquisitionHoldTime)
                {
                    if (debugLogs)
                    {
                        Debug.Log(
                            $"[PAT] Target temporarily lost. " +
                            $"Holding for " +
                            $"{reacquisitionHoldTime - trackLostTimer:F2}s."
                        );
                    }

                    // Laser STILL follows camera.
                    UpdateLaserBeam();

                    return;
                }


                // Reacquisition hold expired.
                trackLostTimer = 0f;

                GimbalOwner.Current =
                    GimbalController.None;

                if (debugLogs)
                {
                    Debug.Log(
                        "[PAT] Reacquisition hold expired. " +
                        "Releasing gimbal to Search."
                    );
                }
            }

            RunSearch();
        }


        // -----------------------------------------------------
        // LASER ALWAYS FOLLOWS CAMERA
        // -----------------------------------------------------

        UpdateLaserBeam();
    }


    // =========================================================
    // PAT
    // =========================================================

    private void RunPAT()
    {
        trackLostTimer = 0f;

        if (wasSearching)
        {
            if (debugLogs)
            {
                Debug.Log(
                    $"[Search -> PAT] Target acquired after " +
                    $"{Time.time - searchStartTime:F2}s and " +
                    $"{sweepPassCount} full sweep(s)."
                );
            }

            wasSearching = false;
        }


        GimbalOwner.Current =
            GimbalController.PAT;


        // Get predicted target position from Tracker.
        Vector2 predicted =
            tracker.GetPredictedPosition();

        predicted.x =
            Mathf.Clamp01(predicted.x);

        predicted.y =
            Mathf.Clamp01(predicted.y);


        CurrentTrackingPosition =
            predicted;


        // Calculate image-space error in pixels.
        CalculateAlignmentError(predicted);


        // Move camera/gimbal toward target.
        TrackUsingCameraRay(predicted);
    }


    // =========================================================
    // IMAGE-SPACE ALIGNMENT
    // =========================================================

    private void CalculateAlignmentError(Vector2 predicted)
    {
        Vector2 centre =
            new Vector2(0.5f, 0.5f);

        Vector2 normalizedError =
            predicted - centre;


        float width =
            Mathf.Max(1f, cam.pixelWidth);

        float height =
            Mathf.Max(1f, cam.pixelHeight);


        float pixelX =
            normalizedError.x * width;

        float pixelY =
            normalizedError.y * height;


        CurrentAlignmentErrorPixels =
            Mathf.Sqrt(
                pixelX * pixelX +
                pixelY * pixelY
            );


        IsTargetLocked =
            CurrentAlignmentErrorPixels <=
            lockTolerancePixels;
    }


    // =========================================================
    // PAT CAMERA TRACKING
    // =========================================================

    private void TrackUsingCameraRay(Vector2 predicted)
    {
        if (!axisDiagLogged)
        {
            axisDiagLogged = true;

            Vector3 centreRay =
                cameraTransform.forward.normalized;

            Debug.Log(
                "[PAT AXIS DIAG] " +
                $"YawAxis={coarseGimbalY.up} | " +
                $"PitchAxis={coarseGimbalX.right} | " +
                $"CamFwd={cameraTransform.forward} | " +
                $"FOV={cam.fieldOfView:F1} | " +
                $"Aspect={cam.aspect:F3} | " +
                $"invertYaw={invertYaw} | " +
                $"invertPitch={invertPitch}"
            );
        }


        // -----------------------------------------------------
        // CAMERA FOV
        // -----------------------------------------------------

        float halfFovY =
            cam.fieldOfView *
            0.5f *
            Mathf.Deg2Rad;


        float halfFovX =
            Mathf.Atan(
                cam.aspect *
                Mathf.Tan(halfFovY)
            );


        // -----------------------------------------------------
        // CONVERT VIEWPORT POSITION
        // TO CAMERA LOCAL DIRECTION
        // -----------------------------------------------------

        float px =
            predicted.x * 2f - 1f;

        float py =
            predicted.y * 2f - 1f;


        Vector3 localRay =
            new Vector3(
                px * Mathf.Tan(halfFovX),
                py * Mathf.Tan(halfFovY),
                1f
            );


        Vector3 targetDirection =
            (
                cameraTransform.rotation *
                localRay
            ).normalized;


        Vector3 cameraForward =
            cameraTransform.forward.normalized;


        // -----------------------------------------------------
        // YAW
        // -----------------------------------------------------

        Vector3 yawAxis =
            coarseGimbalY.up.normalized;


        Vector3 currentYawDirection =
            Vector3.ProjectOnPlane(
                cameraForward,
                yawAxis
            ).normalized;


        Vector3 targetYawDirection =
            Vector3.ProjectOnPlane(
                targetDirection,
                yawAxis
            ).normalized;


        float yawError = 0f;


        if (currentYawDirection.sqrMagnitude > 1e-6f &&
            targetYawDirection.sqrMagnitude > 1e-6f)
        {
            yawError =
                Vector3.SignedAngle(
                    currentYawDirection,
                    targetYawDirection,
                    yawAxis
                );
        }


        if (invertYaw)
        {
            yawError = -yawError;
        }


        float currentYaw =
            NormalizeAngle(
                coarseGimbalY.localEulerAngles.y
            );


        float desiredYaw =
            Mathf.Clamp(
                currentYaw + yawError,
                -horizontalLimit,
                horizontalLimit
            );


        float newYaw =
            Mathf.MoveTowardsAngle(
                currentYaw,
                desiredYaw,
                rotationSpeed *
                Time.deltaTime
            );


        newYaw =
            Mathf.Clamp(
                newYaw,
                -horizontalLimit,
                horizontalLimit
            );


        coarseGimbalY.localRotation =
            Quaternion.Euler(
                0f,
                newYaw,
                0f
            );


        // -----------------------------------------------------
        // PITCH
        // -----------------------------------------------------

        cameraForward =
            cameraTransform.forward.normalized;


        Vector3 pitchAxis =
            coarseGimbalX.right.normalized;


        Vector3 currentPitchDirection =
            Vector3.ProjectOnPlane(
                cameraForward,
                pitchAxis
            ).normalized;


        Vector3 targetPitchDirection =
            Vector3.ProjectOnPlane(
                targetDirection,
                pitchAxis
            ).normalized;


        float pitchError = 0f;


        if (currentPitchDirection.sqrMagnitude > 1e-6f &&
            targetPitchDirection.sqrMagnitude > 1e-6f)
        {
            pitchError =
                Vector3.SignedAngle(
                    currentPitchDirection,
                    targetPitchDirection,
                    pitchAxis
                );
        }


        if (invertPitch)
        {
            pitchError = -pitchError;
        }


        float currentPitch =
            NormalizeAngle(
                coarseGimbalX.localEulerAngles.x
            );


        float desiredPitch =
            Mathf.Clamp(
                currentPitch + pitchError,
                -verticalLimit,
                verticalLimit
            );


        float newPitch =
            Mathf.MoveTowardsAngle(
                currentPitch,
                desiredPitch,
                rotationSpeed *
                Time.deltaTime
            );


        newPitch =
            Mathf.Clamp(
                newPitch,
                -verticalLimit,
                verticalLimit
            );


        coarseGimbalX.localRotation =
            Quaternion.Euler(
                newPitch,
                0f,
                0f
            );


        if (debugLogs)
        {
            Debug.Log(
                $"[PAT] " +
                $"Owner={GimbalOwner.Current} | " +
                $"Detection={tracker.detection} | " +
                $"Predicted={predicted} | " +
                $"ErrorPx={CurrentAlignmentErrorPixels:F1} | " +
                $"Locked={IsTargetLocked} | " +
                $"Yaw={newYaw:F2} | " +
                $"Pitch={newPitch:F2}"
            );
        }
    }


    // =========================================================
    // SEARCH
    // =========================================================

    private void RunSearch()
    {
        GimbalOwner.Current =
            GimbalController.Search;


        if (!wasSearching)
        {
            searchStartTime =
                Time.time;

            wasSearching = true;

            sweepPassCount = 0;


            if (debugLogs)
            {
                Debug.Log(
                    "[Search] No target. " +
                    "Starting acquisition sweep."
                );
            }
        }


        searchYaw =
            NormalizeAngle(
                coarseGimbalY.localEulerAngles.y
            );


        float rowTargetYaw =
            searchYawDir > 0
                ? searchYawRange
                : -searchYawRange;


        searchYaw =
            Mathf.MoveTowards(
                searchYaw,
                rowTargetYaw,
                scanSpeed *
                Time.deltaTime
            );


        searchYaw =
            Mathf.Clamp(
                searchYaw,
                -searchYawRange,
                searchYawRange
            );


        coarseGimbalY.localRotation =
            Quaternion.Euler(
                0f,
                searchYaw,
                0f
            );


        // -----------------------------------------------------
        // REACHED YAW EDGE
        // -----------------------------------------------------

        if (Mathf.Abs(
                searchYaw -
                rowTargetYaw
            ) <= waypointTolerance)
        {
            searchPitch +=
                pitchStepDegrees;

            searchYawDir *= -1;


            if (searchPitch >
                searchPitchRange)
            {
                searchPitch =
                    -searchPitchRange;

                sweepPassCount++;


                if (debugLogs)
                {
                    Debug.LogWarning(
                        $"[Search] Full sweep #" +
                        $"{sweepPassCount} completed " +
                        "without detection."
                    );
                }
            }
        }


        searchPitch =
            Mathf.Clamp(
                searchPitch,
                -searchPitchRange,
                searchPitchRange
            );


        coarseGimbalX.localRotation =
            Quaternion.Euler(
                searchPitch,
                0f,
                0f
            );


        if (debugLogs)
        {
            Debug.Log(
                $"[SEARCH] " +
                $"Yaw={searchYaw:F1} -> " +
                $"{rowTargetYaw:F1} | " +
                $"Pitch={searchPitch:F1} | " +
                $"Direction={searchYawDir}"
            );
        }
    }


    // =========================================================
    // LASER
    // =========================================================

    /// <summary>
    /// Keeps the laser aligned with the tracking camera.
    ///
    /// This function intentionally does NOT point the laser directly
    /// at targetSatellite. The laser represents the optical terminal's
    /// current pointing direction.
    /// </summary>
    private void UpdateLaserBeam()
    {
        if (laserBeamController == null)
            return;

        if (laserOrigin == null)
            return;

        if (cameraTransform == null)
            return;


        float beamLength =
            predictionDistance;


        // Optional beam length based on satellite distance.
        // This DOES NOT affect the pointing direction.
        if (useTargetDistanceForBeamLength &&
            targetSatellite != null)
        {
            float projectedDistance =
                Vector3.Dot(
                    targetSatellite.position -
                    laserOrigin.position,
                    cameraTransform.forward
                );


            beamLength =
                Mathf.Max(
                    0.01f,
                    projectedDistance
                );
        }


        Vector3 direction =
            cameraTransform.forward.normalized;


        Vector3 laserEnd =
            laserOrigin.position +
            direction *
            beamLength;


        laserBeamController.SetTargetPosition(
            laserEnd
        );
    }


    // =========================================================
    // UTILITY
    // =========================================================

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;


        if (angle > 180f)
        {
            angle -= 360f;
        }


        return angle;
    }
}