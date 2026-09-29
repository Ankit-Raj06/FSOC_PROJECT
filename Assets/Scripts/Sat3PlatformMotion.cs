using UnityEngine;

/// <summary>
/// Applies platform-motion disturbances to Sat 3.
///
/// Motion geometry:
///
/// NONE
///     Sat 3 stays at its original position.
///
/// LINEAR
///     Back-and-forth motion along the local X axis.
///
/// CIRCULAR
///     Circular motion in the local YZ plane.
///     Local X is the axis perpendicular to the circle.
///
/// SPIRAL
///     Helical motion:
///     Sat 3 advances along local X while rotating
///     around the X axis in the YZ plane.
///
/// RANDOM
///     Smooth 3D random motion.
///
/// FIGURE8
///     Figure-eight motion in the YZ plane.
/// </summary>
public class Sat3PlatformMotion : MonoBehaviour
{
    [Header("References")]

    [Tooltip("Sat 3 transform. Leave empty to use this GameObject.")]
    public Transform target;


    [Header("Motion Axis")]

    [Tooltip(
        "Transform defining the local coordinate system used for " +
        "the motion. Leave empty to use Sat 3 itself."
    )]
    public Transform axisReference;


    [Tooltip(
        "Local axis used as the circular/spiral axis. " +
        "X = (1,0,0), Y = (0,1,0), Z = (0,0,1)."
    )]
    public Vector3 motionAxis = Vector3.right;


    [Header("Motion Settings")]

    [Tooltip("Fallback movement radius/amplitude.")]
    public float defaultAmplitude = 3f;

    [Tooltip("Fallback motion frequency in Hz.")]
    public float defaultFrequencyHz = 0.4f;


    [Header("Spiral Settings")]

    [Tooltip(
        "Distance travelled along the spiral axis during one full cycle."
    )]
    public float spiralLengthPerCycle = 6f;

    [Tooltip(
        "Number of turns before the spiral reverses direction."
    )]
    public float spiralTurns = 3f;


    [Header("Random Motion")]

    public float randomSmoothness = 0.35f;


    [Header("Debug")]

    public bool debugLogs = false;


    // =========================================================
    // STATE
    // =========================================================

    private Vector3 initialPosition;
    private Quaternion initialRotation;

    private DisturbanceManager disturbanceManager;

    private float randomSeedX = 17.31f;
    private float randomSeedY = 43.72f;
    private float randomSeedZ = 81.19f;

    private PlatformMotionType lastMotionType =
        PlatformMotionType.None;


    // =========================================================
    // UNITY
    // =========================================================

    private void Awake()
    {
        if (target == null)
            target = transform;

        if (axisReference == null)
            axisReference = target;

        initialPosition =
            target.localPosition;

        initialRotation =
            target.localRotation;

        disturbanceManager =
            DisturbanceManager.Instance;
    }


    private void Start()
    {
        if (disturbanceManager == null)
            disturbanceManager =
                DisturbanceManager.Instance;
    }


    private void Update()
    {
        if (target == null)
            return;


        if (disturbanceManager == null)
        {
            disturbanceManager =
                DisturbanceManager.Instance;
        }


        if (disturbanceManager == null)
        {
            RestoreInitialPosition();
            return;
        }


        DisturbanceSettings settings =
            disturbanceManager.settings;


        if (settings == null)
        {
            RestoreInitialPosition();
            return;
        }


        PlatformMotionType motionType =
            settings.platform.type;


        float amplitude =
            Mathf.Max(
                0f,
                settings.platform.amplitude
            );


        if (amplitude <= 0f)
            amplitude =
                defaultAmplitude;


        float frequency =
            settings.platform.randomFrequencyHz;


        if (frequency <= 0f)
            frequency =
                defaultFrequencyHz;


        // -----------------------------------------------------
        // MODE CHANGE
        // -----------------------------------------------------

        if (motionType != lastMotionType)
        {
            if (debugLogs)
            {
                Debug.Log(
                    "[Sat3PlatformMotion] " +
                    lastMotionType +
                    " -> " +
                    motionType
                );
            }


            if (motionType ==
                PlatformMotionType.None)
            {
                RestoreInitialPosition();
            }


            lastMotionType =
                motionType;
        }


        // -----------------------------------------------------
        // APPLY SELECTED MOTION
        // -----------------------------------------------------

        switch (motionType)
        {
            case PlatformMotionType.None:

                RestoreInitialPosition();

                break;


            case PlatformMotionType.Linear:

                ApplyLinear(
                    amplitude,
                    frequency
                );

                break;


            case PlatformMotionType.Circular:

                ApplyCircular(
                    amplitude,
                    frequency
                );

                break;


            case PlatformMotionType.Random:

                ApplyRandom(
                    amplitude,
                    frequency
                );

                break;


            case PlatformMotionType.Spiral:

                ApplySpiral(
                    amplitude,
                    frequency
                );

                break;


            case PlatformMotionType.Figure8:

                ApplyFigure8(
                    amplitude,
                    frequency
                );

                break;


            default:

                RestoreInitialPosition();

                break;
        }
    }


    // =========================================================
    // AXIS
    // =========================================================

    private Vector3 GetMotionAxis()
    {
        Vector3 axis =
            motionAxis;


        if (axis.sqrMagnitude <
            0.0001f)
        {
            axis =
                Vector3.right;
        }


        return axis.normalized;
    }


    /// <summary>
    /// Converts the local motion axis into the target's local
    /// coordinate system.
    ///
    /// The circular/spiral plane is perpendicular to this axis.
    /// </summary>
    private Vector3 GetAxis()
    {
        return GetMotionAxis();
    }


    /// <summary>
    /// Creates two perpendicular vectors forming the plane
    /// perpendicular to the motion axis.
    ///
    /// For X axis:
    ///     plane axes = Y and Z
    ///
    /// For Y axis:
    ///     plane axes = X and Z
    ///
    /// For Z axis:
    ///     plane axes = X and Y
    /// </summary>
    private void GetPerpendicularAxes(
        out Vector3 axis1,
        out Vector3 axis2)
    {
        Vector3 axis =
            GetAxis();


        Vector3 reference =
            Mathf.Abs(
                Vector3.Dot(
                    axis,
                    Vector3.up
                )
            ) > 0.9f
                ? Vector3.right
                : Vector3.up;


        axis1 =
            Vector3.Cross(
                axis,
                reference
            ).normalized;


        axis2 =
            Vector3.Cross(
                axis,
                axis1
            ).normalized;
    }


    // =========================================================
    // NONE
    // =========================================================

    private void RestoreInitialPosition()
    {
        target.localPosition =
            initialPosition;

        target.localRotation =
            initialRotation;
    }


    // =========================================================
    // LINEAR
    // =========================================================

    private void ApplyLinear(
        float amplitude,
        float frequency)
    {
        Vector3 axis =
            GetAxis();


        float t =
            Time.time *
            frequency *
            Mathf.PI *
            2f;


        float offset =
            Mathf.Sin(t) *
            amplitude;


        target.localPosition =
            initialPosition +
            axis * offset;
    }


    // =========================================================
    // CIRCULAR
    // =========================================================

    private void ApplyCircular(
        float amplitude,
        float frequency)
    {
        GetPerpendicularAxes(
            out Vector3 axis1,
            out Vector3 axis2
        );


        float angle =
            Time.time *
            frequency *
            Mathf.PI *
            2f;


        float x =
            Mathf.Cos(angle) *
            amplitude;


        float y =
            Mathf.Sin(angle) *
            amplitude;


        Vector3 offset =
            axis1 * x +
            axis2 * y;


        target.localPosition =
            initialPosition +
            offset;
    }


    // =========================================================
    // SPIRAL / HELIX
    // =========================================================

    private void ApplySpiral(
        float amplitude,
        float frequency)
    {
        GetPerpendicularAxes(
            out Vector3 axis1,
            out Vector3 axis2
        );


        Vector3 axis =
            GetAxis();


        float angle =
            Time.time *
            frequency *
            Mathf.PI *
            2f;


        // -----------------------------------------------------
        // CONSTANT RADIUS
        // -----------------------------------------------------

        float radialX =
            Mathf.Cos(angle) *
            amplitude;


        float radialY =
            Mathf.Sin(angle) *
            amplitude;


        Vector3 radialOffset =
            axis1 * radialX +
            axis2 * radialY;


        // -----------------------------------------------------
        // FORWARD MOTION ALONG AXIS
        // -----------------------------------------------------

        // One complete revolution produces one
        // spiralLengthPerCycle of travel.
        float cycle =
            Time.time *
            frequency;


        float distanceAlongAxis =
            Mathf.Repeat(
                cycle *
                spiralLengthPerCycle,
                spiralLengthPerCycle
            );


        // Center the helix around Sat 3's starting position.
        float centeredDistance =
            distanceAlongAxis -
            spiralLengthPerCycle * 0.5f;


        Vector3 axialOffset =
            axis *
            centeredDistance;


        // -----------------------------------------------------
        // FINAL HELICAL POSITION
        // -----------------------------------------------------

        target.localPosition =
            initialPosition +
            radialOffset +
            axialOffset;
    }


    // =========================================================
    // RANDOM
    // =========================================================

    private void ApplyRandom(
        float amplitude,
        float frequency)
    {
        float t =
            Time.time *
            frequency *
            randomSmoothness;


        float x =
            Mathf.PerlinNoise(
                randomSeedX,
                t
            ) * 2f - 1f;


        float y =
            Mathf.PerlinNoise(
                randomSeedY,
                t
            ) * 2f - 1f;


        float z =
            Mathf.PerlinNoise(
                randomSeedZ,
                t
            ) * 2f - 1f;


        Vector3 offset =
            new Vector3(
                x,
                y,
                z
            ) *
            amplitude;


        target.localPosition =
            initialPosition +
            offset;
    }


    // =========================================================
    // FIGURE 8
    // =========================================================

    private void ApplyFigure8(
        float amplitude,
        float frequency)
    {
        GetPerpendicularAxes(
            out Vector3 axis1,
            out Vector3 axis2
        );


        float angle =
            Time.time *
            frequency *
            Mathf.PI *
            2f;


        float x =
            Mathf.Sin(angle) *
            amplitude;


        float y =
            Mathf.Sin(angle) *
            Mathf.Cos(angle) *
            amplitude;


        Vector3 offset =
            axis1 * x +
            axis2 * y;


        target.localPosition =
            initialPosition +
            offset;
    }


    // =========================================================
    // RESET
    // =========================================================

    public void ResetPosition()
    {
        RestoreInitialPosition();

        lastMotionType =
            PlatformMotionType.None;
    }


    private void OnDisable()
    {
        if (target != null)
            RestoreInitialPosition();
    }
}