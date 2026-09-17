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
    [Tooltip("Maximum gimbal rotation speed in degrees/second.")]
    public float rotationSpeed = 150f;

    [Header("Gimbal Limits")]
    public float horizontalLimit = 80f;
    public float verticalLimit = 60f;

    [Header("Prediction")]
    public float predictionDistance = 10f;

    [Header("Beam Output")]
    public LaserCylinderBeam laserBeamController;

    public bool useTargetDistanceForBeamLength = false;

    [Header("Search / Reacquisition")]
    public bool autoReacquireWhenOutOfView = false;

    [Header("Axis Settings")]
    public bool invertYaw = false;
    public bool invertPitch = false;

    [Header("Debug")]
    public bool debugLogs = false;

    private Camera cam;

    private void Awake()
    {
        if (cameraTransform != null)
        {
            cam = cameraTransform.GetComponent<Camera>();

            if (cam == null)
            {
                Debug.LogWarning(
                    "[PATController] Camera Transform has no Camera component."
                );
            }
        }
    }

    private void Update()
    {
        if (tracker == null ||
            !tracker.enabled ||
            cameraTransform == null ||
            coarseGimbalY == null ||
            coarseGimbalX == null)
            return;

        if (cam == null)
            return;

        // PAT only runs when Tracker says YOLO has a valid target.
        if (!tracker.targetDetected)
            return;

        Vector2 predicted =
            tracker.GetPredictedPosition();

        predicted.x = Mathf.Clamp01(predicted.x);
        predicted.y = Mathf.Clamp01(predicted.y);

        TrackUsingCameraRay(predicted);

        UpdateLaserBeam();
    }

    // ============================================================
    // PAT TRACKING
    // ============================================================

    private void TrackUsingCameraRay(Vector2 predicted)
    {
    	// --------------------------------------------------------
    	// 1. Convert YOLO viewport position into a WORLD-SPACE ray.
    	//
    	// This ray represents exactly where the detected target
    	// appears in the camera image.
    	// --------------------------------------------------------

    	Ray ray =
        	cam.ViewportPointToRay(
            		new Vector3(
                	predicted.x,
                	predicted.y,
                	0f
            	)
        );

    Vector3 targetDirection =
        ray.direction.normalized;

    // --------------------------------------------------------
    // 2. CURRENT OPTICAL AXIS
    //
    // Use the actual camera forward direction.
    // Do NOT assume the camera's +Z is aligned with the
    // gimbal's +Z.
    // --------------------------------------------------------

    Vector3 cameraForward =
        cameraTransform.forward.normalized;

    // --------------------------------------------------------
    // 3. YAW AXIS
    //
    // CoarseGimbal_Y rotates around its local Y axis.
    // In world space that axis is yawGimbal.up.
    // --------------------------------------------------------

    Vector3 yawAxis =
        coarseGimbalY.up.normalized;

    // Project both directions onto the plane perpendicular
    // to the yaw axis.
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

    if (currentYawDirection.sqrMagnitude > 0.000001f &&
        targetYawDirection.sqrMagnitude > 0.000001f)
    {
        yawError =
            Vector3.SignedAngle(
                currentYawDirection,
                targetYawDirection,
                yawAxis
            );
    }

    // --------------------------------------------------------
    // 4. PITCH AXIS
    //
    // CoarseGimbal_X rotates around its local X axis.
    // In world space that axis is coarseGimbalX.right.
    // --------------------------------------------------------

    Vector3 pitchAxis =
        coarseGimbalX.right.normalized;

    // After accounting for yaw, use the current camera
    // direction and target direction to determine pitch.
    //
    // Project onto the plane perpendicular to the pitch axis.
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

    if (currentPitchDirection.sqrMagnitude > 0.000001f &&
        targetPitchDirection.sqrMagnitude > 0.000001f)
    {
        pitchError =
            Vector3.SignedAngle(
                currentPitchDirection,
                targetPitchDirection,
                pitchAxis
            );
    }

    // --------------------------------------------------------
    // 5. AXIS INVERSION
    // --------------------------------------------------------

    if (invertYaw)
        yawError = -yawError;

    if (invertPitch)
        pitchError = -pitchError;

    // --------------------------------------------------------
    // 6. APPLY
    // --------------------------------------------------------

    ApplyYawError(yawError);
    ApplyPitchError(pitchError);

    // --------------------------------------------------------
    // DEBUG
    // --------------------------------------------------------

    if (debugLogs)
    {
        Debug.Log(
            $"PAT | " +
            $"Predicted={predicted} " +
            $"TargetDir={targetDirection} " +
            $"CameraForward={cameraForward} " +
            $"YawError={yawError:F2} " +
            $"PitchError={pitchError:F2}"
        );
    }
}

    // ============================================================
    // YAW
    // ============================================================

    private void ApplyYawError(float yawError)
    {
        float currentYaw =
            NormalizeAngle(
                coarseGimbalY.localEulerAngles.y
            );

        float targetYaw =
            currentYaw + yawError;

        targetYaw =
            Mathf.Clamp(
                targetYaw,
                -horizontalLimit,
                horizontalLimit
            );

        float newYaw =
            Mathf.MoveTowardsAngle(
                currentYaw,
                targetYaw,
                rotationSpeed * Time.deltaTime
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
    }

    // ============================================================
    // PITCH
    // ============================================================

    private void ApplyPitchError(float pitchError)
    {
        float currentPitch =
            NormalizeAngle(
                coarseGimbalX.localEulerAngles.x
            );

        float targetPitch =
            currentPitch + pitchError;

        targetPitch =
            Mathf.Clamp(
                targetPitch,
                -verticalLimit,
                verticalLimit
            );

        float newPitch =
            Mathf.MoveTowardsAngle(
                currentPitch,
                targetPitch,
                rotationSpeed * Time.deltaTime
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
    }

    // ============================================================
    // LASER
    // ============================================================

    private void UpdateLaserBeam()
    {
        if (laserBeamController == null ||
            laserOrigin == null ||
            cameraTransform == null)
            return;

        float beamLength =
            predictionDistance;

        if (useTargetDistanceForBeamLength &&
            targetSatellite != null)
        {
            Vector3 toTarget =
                targetSatellite.position -
                laserOrigin.position;

            float projectedDistance =
                Vector3.Dot(
                    toTarget,
                    cameraTransform.forward
                );

            beamLength =
                Mathf.Max(
                    0.01f,
                    projectedDistance
                );
        }

        Vector3 beamEnd =
            laserOrigin.position +
            cameraTransform.forward *
            beamLength;

        laserBeamController.SetTargetPosition(
            beamEnd
        );
    }

    // ============================================================
    // ANGLE NORMALIZATION
    // ============================================================

    private float NormalizeAngle(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
