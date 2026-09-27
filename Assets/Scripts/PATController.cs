using UnityEngine;

/// <summary>
/// Integrated Search-Acquisition + PAT (Point-Acquire-Track) controller.
///
/// SEARCH MODE  (GimbalOwner = Search)
///   Runs when tracker.targetDetected == false.
///   Sweeps ±searchYawRange (default ±180°) × ±searchPitchRange (default ±90°)
///   in a boustrophedon raster until YOLO fires a real detection.
///   The transmitter is never given the satellite's true world position —
///   detection is entirely vision-based (YoloDetection → Tracker).
///
/// PAT MODE  (GimbalOwner = PAT)
///   Runs when tracker.targetDetected == true.
///   Handoff from Search → PAT is IMMEDIATE: the very next Update() after
///   tracker.targetDetected flips true switches to PAT with no hold-time
///   guard on this transition.  "If the receiver is in the FOV, PAT takes
///   over no matter what."
///
///   PAT limits (horizontalLimit / verticalLimit) default to ±180° / ±90°
///   so the laser can point anywhere in the full sphere reachable by the
///   2-axis gimbal.  Note: a Y-then-X gimbal has gimbal lock at pitch ≈ ±90°
///   (the yaw axis collapses). True omnidirectional coverage beyond that
///   requires a 3-axis gimbal.
///
///   Tracks using the Kalman-predicted viewport position (tracker.GetPredictedPosition())
///   converted to a world-space ray via the camera's current rotation and FOV —
///   does NOT use cam.ViewportPointToRay() which reads the stale cameraToWorldMatrix.
///
/// PAT → SEARCH handoff
///   When YOLO detection drops, PAT holds the gimbal frozen at its last
///   commanded angles for reacquisitionHoldTime seconds to bridge momentary
///   inference gaps.  After that window, ownership is released and Search
///   resumes its sweep.
///
/// Prerequisites:
///   CoarseGimbalTracker  — DISABLED (uses true satellite position)
///   SimulatedDetection   — DISABLED (uses true satellite position)
///   DatasetCameraAim     — DISABLED (uses true satellite position)
///   YoloDetection        — ENABLED  (only legitimate detection source)
/// </summary>
public class PATController : MonoBehaviour
{
    // ============================================================
    // INSPECTOR
    // ============================================================

    [Header("References")]
    public Tracker tracker;
    public Transform laserOrigin;
    public Transform cameraTransform;

    [Tooltip("Only used for optional beam-length projection. " +
             "PAT never reads this position for tracking — " +
             "all tracking is vision-based.")]
    public Transform targetSatellite;

    [Header("Gimbal")]
    public Transform coarseGimbalY;
    public Transform coarseGimbalX;

    [Header("PAT — Control")]
    [Tooltip("Maximum gimbal slew rate while PAT is tracking, degrees/second.")]
    public float rotationSpeed = 150f;

    [Header("PAT — Beam Output")]
    public LaserCylinderBeam laserBeamController;

    [Tooltip("Fallback beam length (metres) when target distance is unknown.")]
    public float predictionDistance = 10f;

    [Tooltip("If true, projects the beam to the satellite's actual depth " +
             "(requires targetSatellite to be assigned).")]
    public bool useTargetDistanceForBeamLength = false;

    [Header("PAT — Reacquisition Hold")]
    [Tooltip("Seconds PAT keeps the gimbal frozen after YOLO detection drops " +
             "before releasing ownership back to Search. " +
             "Bridges momentary inference gaps. Set 0 for instant release.")]
    public float reacquisitionHoldTime = 1.0f;

    [Header("PAT — Gimbal Limits")]
    [Tooltip("Maximum yaw the gimbal can reach during PAT tracking.\n" +
             "Set to 180 for full 360° azimuth coverage.\n" +
             "Note: a 2-axis Y→X gimbal has gimbal lock at pitch ≈ ±90°.")]
    public float horizontalLimit = 180f;

    [Tooltip("Maximum pitch the gimbal can reach during PAT tracking.\n" +
             "Set to 90 for full hemisphere elevation coverage.\n" +
             "Values > 90° cause the gimbal to flip past nadir/zenith.")]
    public float verticalLimit = 90f;

    [Header("Search — Sweep")]
    [Tooltip("Gimbal slew rate while searching, degrees/second.")]
    public float scanSpeed = 200f;

    [Tooltip("Half-range of the yaw sweep during search, degrees. " +
             "Default 180 sweeps the full 360° circle. " +
             "Search runs from -searchYawRange to +searchYawRange.")]
    public float searchYawRange = 180f;

    [Tooltip("Half-range of the pitch sweep during search, degrees. " +
             "Default 90 covers the full hemisphere. " +
             "Search runs from -searchPitchRange to +searchPitchRange.")]
    public float searchPitchRange = 90f;

    [Tooltip("Pitch step between raster rows, degrees. " +
             "Should be ≤ camera vertical FOV to avoid coverage gaps.")]
    public float pitchStepDegrees = 25f;

    [Tooltip("Angular tolerance for declaring 'reached end of row', degrees.")]
    public float waypointTolerance = 1f;

    [Header("Axis Settings")]
    public bool invertYaw   = false;
    public bool invertPitch = false;

    [Header("Debug")]
    public bool debugLogs = false;

    // ============================================================
    // UI TELEMETRY
    // ============================================================

    /// <summary>Human-readable state exposed to the evaluation dashboard.</summary>
    public string CurrentModeName
    {
        get { return GimbalOwner.Current.ToString(); }
    }

    // ============================================================
    // PRIVATE STATE
    // ============================================================

    private Camera cam;

    // --- PAT state ---
    private float trackLostTimer  = 0f;
    private bool  axisDiagLogged  = false;

    // --- Search state ---
    private float searchYaw;
    private float searchPitch;
    private int   searchYawDir    = 1;   // +1 → maxYaw, -1 → minYaw
    private bool  wasSearching    = false;
    private float searchStartTime = 0f;
    private int   sweepPassCount  = 0;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();
            if (cam == null)
                Debug.LogWarning("[PATController] cameraTransform has no Camera component.");
        }
    }

    private void Start()
    {
        // Seed yaw from the actual transform (PAT may have left it anywhere).
        searchYaw   = NormalizeAngle(coarseGimbalY.localEulerAngles.y);
        // Always start a fresh sweep from the bottom pitch row of the search envelope.
        searchPitch = -searchPitchRange;

        if (debugLogs)
            Debug.Log("[PATController] Initialised. Entering Search mode.");
    }

    private void OnDisable()
    {
        if (GimbalOwner.Current == GimbalController.PAT ||
            GimbalOwner.Current == GimbalController.Search)
        {
            GimbalOwner.Current = GimbalController.None;
        }
        wasSearching = false;
    }

    // ============================================================
    // UPDATE  — state machine entry point
    // ============================================================

    private void Update()
    {
        if (tracker       == null || !tracker.enabled ||
            cameraTransform == null ||
            coarseGimbalY   == null ||
            coarseGimbalX   == null ||
            cam             == null)
            return;

        if (tracker.targetDetected)
        {
            // ── YOLO has a live detection → PAT mode ──────────────────────────
            // Handoff from Search is IMMEDIATE — no guard here.
            RunPAT();
        }
        else
        {
            // ── No detection ─────────────────────────────────────────────────
            if (GimbalOwner.Current == GimbalController.PAT)
            {
                // Reacquisition hold: freeze gimbal for reacquisitionHoldTime
                // so Search doesn't sweep away from a momentary YOLO drop.
                trackLostTimer += Time.deltaTime;

                if (trackLostTimer < reacquisitionHoldTime)
                {
                    if (debugLogs)
                        Debug.Log($"[PAT] Track lost — holding for {reacquisitionHoldTime - trackLostTimer:F2}s more.");
                    return; // keep PAT ownership, write nothing (angles stay frozen)
                }

                // Hold expired — surrender ownership.
                trackLostTimer = 0f;
                GimbalOwner.Current = GimbalController.None;

                if (debugLogs)
                    Debug.Log("[PAT] Hold expired. Releasing gimbal → Search.");
            }

            // ── SEARCH mode ───────────────────────────────────────────────────
            RunSearch();
        }
    }

    // ============================================================
    // PAT MODE
    // ============================================================

    private void RunPAT()
    {
        // Reset timers whenever we have a valid track.
        trackLostTimer = 0f;

        // If we were searching, log the handoff.
        if (wasSearching)
        {
            if (debugLogs)
                Debug.Log($"[Search→PAT] Target acquired after {Time.time - searchStartTime:F2}s " +
                          $"and {sweepPassCount} full sweep(s). Handing off to PAT.");
            wasSearching = false;
        }

        GimbalOwner.Current = GimbalController.PAT;

        Vector2 predicted = tracker.GetPredictedPosition();
        predicted.x = Mathf.Clamp01(predicted.x);
        predicted.y = Mathf.Clamp01(predicted.y);

        TrackUsingCameraRay(predicted);
        UpdateLaserBeam();
    }

    // ============================================================
    // PAT TRACKING — camera-ray based yaw / pitch
    // ============================================================

    private void TrackUsingCameraRay(Vector2 predicted)
    {
        // ── Axis diagnostic (fires once on first lock) ────────────────────
        if (!axisDiagLogged)
        {
            axisDiagLogged = true;
            Vector3 centreRay = (cameraTransform.rotation * Vector3.forward).normalized;
            Debug.Log(
                "[PAT AXIS DIAG] " +
                $"YawAxis={coarseGimbalY.up} | PitchAxis={coarseGimbalX.right} | " +
                $"CamFwd={cameraTransform.forward} | CentreRay={centreRay} | " +
                $"Aligned={Vector3.Dot(cameraTransform.forward.normalized, centreRay) > 0.999f} | " +
                $"FOV={cam.fieldOfView:F1}° Aspect={cam.aspect:F3} | " +
                $"invertYaw={invertYaw} invertPitch={invertPitch}"
            );
        }

        // ── 1. Build target ray analytically from camera rotation + FOV ──
        // We do NOT use cam.ViewportPointToRay() — it reads the stale
        // cameraToWorldMatrix (frozen by Unity when the camera is rendered
        // manually, as YoloDetection does). Instead we use the live
        // cameraTransform.rotation quaternion which always reflects the
        // actual gimbal state.
        float halfFovY = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float halfFovX = Mathf.Atan(cam.aspect * Mathf.Tan(halfFovY));

        float px = predicted.x * 2f - 1f;  // [-1, 1]
        float py = predicted.y * 2f - 1f;

        Vector3 localRay = new Vector3(
            px * Mathf.Tan(halfFovX),
            py * Mathf.Tan(halfFovY),
            1f
        );
        Vector3 targetDirection = (cameraTransform.rotation * localRay).normalized;
        Vector3 cameraForward   = cameraTransform.forward.normalized;

        // ── 2. Yaw error (around coarseGimbalY.up) ───────────────────────
        Vector3 yawAxis             = coarseGimbalY.up.normalized;
        Vector3 currentYawDirection = Vector3.ProjectOnPlane(cameraForward,   yawAxis).normalized;
        Vector3 targetYawDirection  = Vector3.ProjectOnPlane(targetDirection, yawAxis).normalized;

        float yawError = 0f;
        if (currentYawDirection.sqrMagnitude > 1e-6f && targetYawDirection.sqrMagnitude > 1e-6f)
            yawError = Vector3.SignedAngle(currentYawDirection, targetYawDirection, yawAxis);

        if (invertYaw) yawError = -yawError;

        // ── 3. Apply yaw ─────────────────────────────────────────────────
        float curYaw = NormalizeAngle(coarseGimbalY.localEulerAngles.y);
        float newYaw = Mathf.Clamp(
            Mathf.MoveTowardsAngle(curYaw, Mathf.Clamp(curYaw + yawError, -horizontalLimit, horizontalLimit), rotationSpeed * Time.deltaTime),
            -horizontalLimit, horizontalLimit
        );
        coarseGimbalY.localRotation = Quaternion.Euler(0f, newYaw, 0f);

        // ── 4. Pitch error (around coarseGimbalX.right, after yaw) ───────
        Vector3 pitchAxis              = coarseGimbalX.right.normalized;
        cameraForward                  = cameraTransform.forward.normalized;  // re-read after yaw
        Vector3 currentPitchDirection  = Vector3.ProjectOnPlane(cameraForward,   pitchAxis).normalized;
        Vector3 targetPitchDirection   = Vector3.ProjectOnPlane(targetDirection, pitchAxis).normalized;

        float pitchError = 0f;
        if (currentPitchDirection.sqrMagnitude > 1e-6f && targetPitchDirection.sqrMagnitude > 1e-6f)
            pitchError = Vector3.SignedAngle(currentPitchDirection, targetPitchDirection, pitchAxis);

        if (invertPitch) pitchError = -pitchError;

        // ── 5. Apply pitch ────────────────────────────────────────────────
        float curPitch = NormalizeAngle(coarseGimbalX.localEulerAngles.x);
        float newPitch = Mathf.Clamp(
            Mathf.MoveTowardsAngle(curPitch, Mathf.Clamp(curPitch + pitchError, -verticalLimit, verticalLimit), rotationSpeed * Time.deltaTime),
            -verticalLimit, verticalLimit
        );
        coarseGimbalX.localRotation = Quaternion.Euler(newPitch, 0f, 0f);

        // ── 6. Debug ──────────────────────────────────────────────────────
        if (debugLogs)
        {
            Debug.Log(
                $"[PAT] Owner={GimbalOwner.Current} | Detection={tracker.detection} | " +
                $"Predicted={predicted} | CamFwd={cameraForward} | TargetRay={targetDirection} | " +
                $"Yaw=[cur:{curYaw:F2} new:{newYaw:F2} err:{yawError:F2}] | " +
                $"Pitch=[cur:{curPitch:F2} new:{newPitch:F2} err:{pitchError:F2}]"
            );
        }

        Debug.DrawRay(cameraTransform.position, cameraForward   * 100f, Color.green);
        Debug.DrawRay(cameraTransform.position, targetDirection  * 100f, Color.red);
    }

    // ============================================================
    // SEARCH MODE — full-sphere boustrophedon raster sweep
    //
    // Sweeps ±searchYawRange (default ±180°) × ±searchPitchRange
    // (default ±90°) so the satellite can be found at any angle.
    //
    // Ordering is critical for correct pitch-step behaviour:
    //   1. Sync YAW ONLY from the transform (authoritative after PAT
    //      releases). Pitch is kept as independent state — syncing it
    //      from the transform would silently discard the row-step
    //      increment every frame.
    //   2. Move yaw → write yaw to transform.
    //   3. Check row end → increment pitch, flip yaw direction.
    //   4. Clamp + write pitch AFTER the step so the new row value
    //      is committed to the transform for this frame.
    // ============================================================

    private void RunSearch()
    {
        GimbalOwner.Current = GimbalController.Search;

        if (!wasSearching)
        {
            searchStartTime = Time.time;
            wasSearching    = true;
            sweepPassCount  = 0;
            if (debugLogs)
                Debug.Log("[Search] No track — starting full-sphere acquisition sweep. GimbalOwner=Search");
        }

        // Sync yaw from the transform every frame — it is authoritative
        // because PAT may have left the gimbal at any yaw angle before
        // releasing ownership. Pitch is NOT synced; it is maintained as
        // independent state so row-step increments persist across frames.
        searchYaw = NormalizeAngle(coarseGimbalY.localEulerAngles.y);

        // ── Move yaw toward the end of the current row ────────────────────
        float rowTargetYaw = searchYawDir > 0 ? searchYawRange : -searchYawRange;

        searchYaw = Mathf.MoveTowards(searchYaw, rowTargetYaw, scanSpeed * Time.deltaTime);
        searchYaw = Mathf.Clamp(searchYaw, -searchYawRange, searchYawRange);
        coarseGimbalY.localRotation = Quaternion.Euler(0f, searchYaw, 0f);

        // ── Row end: step pitch, reverse yaw direction ────────────────────
        if (Mathf.Abs(searchYaw - rowTargetYaw) <= waypointTolerance)
        {
            searchPitch  += pitchStepDegrees;
            searchYawDir *= -1;

            if (searchPitch > searchPitchRange)
            {
                // Completed one full raster pass — restart from the bottom.
                searchPitch = -searchPitchRange;
                sweepPassCount++;
                if (debugLogs)
                    Debug.LogWarning($"[Search] Full sphere sweep #{sweepPassCount} complete with no detection — restarting.");
            }
        }

        // ── Write pitch AFTER the step so the committed value is in the
        //    transform when the next Update reads it ────────────────────────
        searchPitch = Mathf.Clamp(searchPitch, -searchPitchRange, searchPitchRange);
        coarseGimbalX.localRotation = Quaternion.Euler(searchPitch, 0f, 0f);

        if (debugLogs)
        {
            Debug.Log(
                $"SEARCH | Yaw={searchYaw:F1} (→{rowTargetYaw:F1}) " +
                $"Pitch={searchPitch:F1} Dir={searchYawDir}"
            );
        }
    }

    // ============================================================
    // LASER BEAM
    // ============================================================

    private void UpdateLaserBeam()
    {
        if (laserBeamController == null || laserOrigin == null || cameraTransform == null)
            return;

        float beamLength = predictionDistance;

        if (useTargetDistanceForBeamLength && targetSatellite != null)
        {
            float projected = Vector3.Dot(
                targetSatellite.position - laserOrigin.position,
                cameraTransform.forward
            );
            beamLength = Mathf.Max(0.01f, projected);
        }

        laserBeamController.SetTargetPosition(
            laserOrigin.position + cameraTransform.forward * beamLength
        );
    }

    // ============================================================
    // UTILITIES
    // ============================================================

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}