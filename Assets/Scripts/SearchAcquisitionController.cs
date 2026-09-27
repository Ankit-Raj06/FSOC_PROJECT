using UnityEngine;

/// <summary>
/// Vision-only initial acquisition search. Never reads any
/// satellite's true position — it only ever looks at
/// Tracker.targetDetected (which itself only becomes true from a
/// real YOLO detection, so long as SimulatedDetection's
/// useRealDetection ground-truth path is disabled).
///
/// Sweeps the full reachable gimbal envelope in a boustrophedon
/// (back-and-forth) raster pattern until YOLO gets a detection.
/// Once tracker.targetDetected is true, this script goes idle and
/// PATController — which already no-ops with no track — takes over
/// on its own; no explicit hand-off logic needed.
/// </summary>
public class SearchAcquisitionController : MonoBehaviour
{
    [Header("References")]
    public Tracker tracker;
    public Transform coarseGimbalY;
    public Transform coarseGimbalX;

    [Header("Gimbal Limits")]
    [Tooltip("Must match PATController's horizontalLimit/verticalLimit " +
             "(as +/- values) so the search never sweeps somewhere PAT can't reach.")]
    public float minYaw = -80f;
    public float maxYaw = 80f;
    public float minPitch = -60f;
    public float maxPitch = 60f;

    [Header("Sweep")]
    [Tooltip("Gimbal slew rate while searching, deg/sec.")]
    public float scanSpeed = 200f;

    [Tooltip("Pitch step between rows, deg. Should be <= camera FOV * overlapFactor " +
             "so no gap in coverage is possible.")]
    public float pitchStepDegrees = 25f;

    [Tooltip("Angle tolerance for 'reached waypoint', deg.")]
    public float waypointTolerance = 1f;

    [Header("Debug")]
    public bool debugLogs = false;

    private float currentYaw;
    private float currentPitch;
    private int yawDirection = 1;   // +1 sweeps toward maxYaw, -1 toward minYaw
    private bool wasSearching = false;
    private float searchStartTime;
    private int sweepPassCount = 0;

    private void Start()
    {
        SyncFromTransforms();
        currentPitch = minPitch; // always start a fresh search from the bottom row
    }

    private void Update()
    {
        if (tracker == null || coarseGimbalY == null || coarseGimbalX == null)
            return;

        if (tracker.targetDetected)
        {
            if (wasSearching)
            {
                if (debugLogs)
                    Debug.Log($"[Search] Acquired after {Time.time - searchStartTime:F2}s, {sweepPassCount} pass(es).");
                wasSearching = false;
            }
            return; // PATController owns the gimbal now.
        }

        if (!wasSearching)
        {
            searchStartTime = Time.time;
            wasSearching = true;
            if (debugLogs)
                Debug.Log("[Search] No track — starting acquisition sweep.");
        }

        // The actual transform is authoritative (PAT may have moved it
        // before losing lock), so re-sync every frame rather than
        // trusting our own last-written value.
        SyncFromTransforms();

        float rowTargetYaw = yawDirection > 0 ? maxYaw : minYaw;

        currentYaw = Mathf.MoveTowards(currentYaw, rowTargetYaw, scanSpeed * Time.deltaTime);
        currentYaw = Mathf.Clamp(currentYaw, minYaw, maxYaw);
        coarseGimbalY.localRotation = Quaternion.Euler(0f, currentYaw, 0f);

        if (Mathf.Abs(currentYaw - rowTargetYaw) <= waypointTolerance)
        {
            // Reached the end of this row — step down/up a pitch row
            // and reverse yaw direction for the next pass.
            currentPitch += pitchStepDegrees;
            yawDirection *= -1;

            if (currentPitch > maxPitch)
            {
                currentPitch = minPitch;
                sweepPassCount++;
                if (debugLogs)
                    Debug.LogWarning($"[Search] Completed full sweep #{sweepPassCount} with no detection — restarting.");
            }
        }

        currentPitch = Mathf.Clamp(currentPitch, minPitch, maxPitch);
        coarseGimbalX.localRotation = Quaternion.Euler(currentPitch, 0f, 0f);

        if (debugLogs)
        {
            Debug.Log(
                $"SEARCH | Yaw={currentYaw:F1} (->{rowTargetYaw}) " +
                $"Pitch={currentPitch:F1} Dir={yawDirection}"
            );
        }
    }

    private void SyncFromTransforms()
    {
        currentYaw = NormalizeAngle(coarseGimbalY.localEulerAngles.y);
        currentPitch = NormalizeAngle(coarseGimbalX.localEulerAngles.x);
    }

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;
        if (angle > 180f) angle -= 360f;
        return angle;
    }
}