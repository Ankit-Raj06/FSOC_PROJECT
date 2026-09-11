using UnityEngine;

public class PATController : MonoBehaviour
{
    [Header("References")]
    public Tracker tracker;

    public Transform laserOrigin;
    public Transform cameraTransform;
    public Transform targetSatellite;

    [Header("Gimbal")]
    public Transform coarseGimbalY;
    public Transform coarseGimbalX;

    [Header("Control")]
    public float rotationSpeed = 5f;

    [Header("Gimbal Limits")]
    public float horizontalLimit = 80f;
    public float verticalLimit = 60f;

    [Header("Prediction")]
    public float predictionDistance = 10f;

    [Header("Beam Output")]
    [Tooltip("The LaserCylinderBeam this controller drives with its predicted pointing direction.")]
    public LaserCylinderBeam laserBeamController;

    [Tooltip("If true, the beam's length uses the actual distance to targetSatellite (known/assumed range) " +
             "instead of the fixed predictionDistance. Direction still comes entirely from PAT either way — " +
             "this only affects where the beam visually ends.")]
    public bool useTargetDistanceForBeamLength = false;

    [Header("Search / Reacquisition")]
    [Tooltip("If true, whenever targetSatellite is geometrically outside the camera's frame, the gimbal " +
             "slews toward its true direction using a direct 3D angle (not viewport projection), until it " +
             "re-enters view.\n\n" +
             "IMPORTANT — this is a GROUND-TRUTH-based fallback for demo purposes only. It uses " +
             "targetSatellite's real position directly, which a real system would not have access to. " +
             "It is NOT the uncertainty-aware search strategy (widened FOV / motion-based prediction / " +
             "recovery sweep) described in your project write-up — don't present this as that in your viva. " +
             "It's useful for demonstrating that the gimbal recovers from target loss at all.")]
    public bool autoReacquireWhenOutOfView = true;

    private Camera cam;

    private void Awake()
    {
        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                Debug.LogWarning(
                    "PATController: cameraTransform has no Camera component. " +
                    "autoReacquireWhenOutOfView will be unavailable until this is fixed."
                );
            }
        }
    }

    private void Update()
    {
        if (tracker == null)
            return;

        if (!tracker.enabled)
            return;

        float horizontalAngle;
        float verticalAngle;

        // ------------------------------------------------
        // DECIDE WHICH ERROR SIGNAL TO USE THIS FRAME
        //
        // Normal case: Kalman-filtered viewport prediction from
        // Tracker, converted via the linear viewportOffset -> angle
        // approximation below. That approximation is only valid
        // near the center of frame (which is where normal tracking
        // operates), NOT at large angles.
        //
        // Reacquire case: computed directly as a 3D angle in
        // cameraTransform's own local space via Atan2, which stays
        // bounded and correct at ANY angular offset — including
        // targets near/behind the camera, where a viewport-based
        // error signal blows up toward infinity (tan(angle) as
        // angle -> 90 degrees) and drives the gimbal the wrong way.
        // This was the actual cause of the erratic reacquire
        // behavior: WorldToViewportPoint's x/y are a tangent
        // projection, not an angle — using them as an angle-scaled
        // error is only ever a good approximation close to center.
        // ------------------------------------------------

        bool useReacquire =
            autoReacquireWhenOutOfView &&
            cam != null &&
            targetSatellite != null &&
            !IsTargetInView();

        if (useReacquire)
        {
            Vector3 toTarget =
                targetSatellite.position - cameraTransform.position;

            if (toTarget.sqrMagnitude < 0.0001f)
            {
                // Degenerate — target essentially at camera position.
                // Fall back to the tracker rather than dividing by
                // near-zero.
                Vector2 fallback = tracker.GetPredictedPosition();
                horizontalAngle = (fallback.x - 0.5f) * horizontalLimit;
                verticalAngle = (fallback.y - 0.5f) * verticalLimit;
            }
            else
            {
                // Direction to target, expressed in cameraTransform's
                // OWN local space (local +Z = forward, local +X =
                // right, local +Y = up). Because this uses the
                // camera's live world orientation, it automatically
                // accounts for whatever fixed offset exists between
                // the gimbal axes and the camera's actual forward —
                // no need to know that offset explicitly.
                Vector3 localToTarget =
                    cameraTransform.InverseTransformDirection(toTarget.normalized);

                // Yaw: angle in the horizontal (X/Z) plane.
                horizontalAngle =
                    Mathf.Atan2(localToTarget.x, localToTarget.z) * Mathf.Rad2Deg;

                // Pitch: angle in the vertical (Y/Z) plane. Negated
                // to match the existing sign convention below, where
                // verticalAngle is subtracted to move X.
                verticalAngle =
                    Mathf.Atan2(-localToTarget.y, localToTarget.z) * Mathf.Rad2Deg;
            }
        }
        else
        {
            // Trust the Kalman-filtered tracker, using the existing
            // linear viewport-offset approximation (valid because
            // normal tracking keeps the target near center of frame).
            Vector2 predicted = tracker.GetPredictedPosition();

            float horizontalError = predicted.x - 0.5f;
            float verticalError = predicted.y - 0.5f;

            horizontalAngle = horizontalError * horizontalLimit;
            verticalAngle = verticalError * verticalLimit;
        }

        // ------------------------------------------------
        // HORIZONTAL GIMBAL
        //
        // Target angle is CURRENT angle + increment, not just the
        // increment on its own — this is what makes the control
        // law converge to true zero error instead of a steady-state
        // offset. Unaffected by which branch above produced the
        // increment — reacquire's larger increments simply clamp to
        // the gimbal limit and slew there at the normal rate.
        // ------------------------------------------------

        if (coarseGimbalY != null)
        {
            Vector3 currentRotation =
                coarseGimbalY.localEulerAngles;

            float currentY =
                NormalizeAngle(currentRotation.y);

            float targetY =
                Mathf.Clamp(
                    currentY + horizontalAngle,
                    -horizontalLimit,
                    horizontalLimit
                );

            float newY =
                Mathf.MoveTowards(
                    currentY,
                    targetY,
                    rotationSpeed * 100f * Time.deltaTime
                );

            coarseGimbalY.localRotation =
                Quaternion.Euler(
                    0f,
                    newY,
                    0f
                );
        }

        // ------------------------------------------------
        // VERTICAL GIMBAL
        // ------------------------------------------------

        if (coarseGimbalX != null)
        {
            Vector3 currentRotation =
                coarseGimbalX.localEulerAngles;

            float currentX =
                NormalizeAngle(currentRotation.x);

            float targetX =
                Mathf.Clamp(
                    currentX + (-verticalAngle),
                    -verticalLimit,
                    verticalLimit
                );

            float newX =
                Mathf.MoveTowards(
                    currentX,
                    targetX,
                    rotationSpeed * 100f * Time.deltaTime
                );

            coarseGimbalX.localRotation =
                Quaternion.Euler(
                    newX,
                    0f,
                    0f
                );
        }

        // ------------------------------------------------
        // DRIVE THE LASER BEAM WITH THE PAT-COMMANDED DIRECTION
        // ------------------------------------------------

        if (laserBeamController != null && laserOrigin != null && cameraTransform != null)
        {
            float beamLength = predictionDistance;

            if (useTargetDistanceForBeamLength && targetSatellite != null)
            {
                Vector3 toTarget =
                    targetSatellite.position - laserOrigin.position;

                beamLength =
                    Vector3.Dot(
                        toTarget,
                        cameraTransform.forward
                    );
            }

            Vector3 predictedWorldPosition =
                laserOrigin.position +
                cameraTransform.forward * beamLength;

            laserBeamController.SetTargetPosition(predictedWorldPosition);
        }
    }

    // ============================================================
    // Geometric "is the target currently visible" check — used only
    // to decide whether to fall back to ground-truth reacquisition.
    // ============================================================

    private bool IsTargetInView()
    {
        if (cam == null || targetSatellite == null)
            return false;

        Vector3 viewportPos =
            cam.WorldToViewportPoint(targetSatellite.position);

        return viewportPos.z > 0f &&
               viewportPos.x >= 0f && viewportPos.x <= 1f &&
               viewportPos.y >= 0f && viewportPos.y <= 1f;
    }

    private float NormalizeAngle(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}