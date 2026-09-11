using UnityEngine;

public class SimulatedDetection : MonoBehaviour
{
    public Tracker tracker;

    [Header("Real Detection")]
    [Tooltip("Camera used to project the target's world position into viewport (0-1) coordinates.")]
    public Transform cameraTransform;

    [Tooltip("The satellite whose real position is being 'detected'.")]
    public Transform targetSatellite;

    [Header("Mode")]
    [Tooltip("If true, detection is computed from targetSatellite's real projected position. " +
             "If false, falls back to the original synthetic sine/cosine wander — useful for " +
             "isolating and testing PAT/gimbal behavior independently of tracking accuracy.")]
    public bool useRealDetection = true;

    [Header("Synthetic Fallback Movement")]
    public float horizontalSpeed = 0.35f;
    public float verticalSpeed = 0.25f;

    [Header("Synthetic Fallback Detection Range")]
    [Range(0.05f, 0.45f)]
    public float horizontalRange = 0.30f;

    [Range(0.05f, 0.45f)]
    public float verticalRange = 0.20f;

    private float time;
    private Camera cam;

    private void Awake()
    {
        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                Debug.LogWarning(
                    "SimulatedDetection: cameraTransform has no Camera component. " +
                    "Real detection will be unavailable until this is fixed."
                );
            }
        }
    }

    private void Update()
    {
        if (tracker == null)
            return;

        if (useRealDetection)
        {
            UpdateRealDetection();
        }
        else
        {
            UpdateSyntheticDetection();
        }
    }

    // ============================================================
    // REAL DETECTION
    //
    // Projects the target satellite's actual world-space position
    // through the camera to get its viewport-space (0-1) position —
    // this is what a real detector would ultimately output (a
    // normalized image-plane position), just computed geometrically
    // instead of via computer vision.
    // ============================================================

    private void UpdateRealDetection()
    {
        if (cam == null || targetSatellite == null)
        {
            tracker.ClearDetection();
            return;
        }

        Vector3 viewportPos =
            cam.WorldToViewportPoint(targetSatellite.position);

        // viewportPos.z <= 0 means the target is BEHIND the camera —
        // there is no valid detection to report in that case.
        //
        // viewportPos.x/y outside [0,1] means the target is in FRONT
        // of the camera but outside the frame (or, at grazing angles
        // near z == 0, the perspective divide has blown x/y up to
        // huge or near-infinite values). Clamping those into [0,1]
        // would report a confident false detection sitting at the
        // edge of frame and yank the tracker toward it — reject
        // instead of clamping.
        bool inFrustum =
            viewportPos.z > 0f &&
            viewportPos.x >= 0f && viewportPos.x <= 1f &&
            viewportPos.y >= 0f && viewportPos.y <= 1f;

        if (!inFrustum)
        {
            tracker.ClearDetection();
            return;
        }

        Vector2 detection = new Vector2(viewportPos.x, viewportPos.y);

        tracker.SetDetection(detection);
    }

    // ============================================================
    // SYNTHETIC FALLBACK (original behavior)
    // ============================================================

    private void UpdateSyntheticDetection()
    {
        time += Time.deltaTime;

        float x =
            0.5f +
            Mathf.Sin(time * horizontalSpeed) *
            horizontalRange;

        float y =
            0.5f +
            Mathf.Cos(time * verticalSpeed) *
            verticalRange;

        tracker.SetDetection(
            new Vector2(x, y)
        );
    }
}