using UnityEngine;

/// <summary>
/// Visual-only laser beam.
///
/// PATController supplies the desired beam endpoint through
/// SetTargetPosition().
///
/// This component does NOT perform:
/// - YOLO detection
/// - tracking
/// - prediction
/// - PAT
/// - gimbal control
///
/// Its only job is to draw one cylinder from laserOrigin to
/// the position supplied by PAT.
/// </summary>
public class LaserCylinderBeam : MonoBehaviour
{
    [Header("Beam Endpoints")]

    [Tooltip("Physical optical origin of the laser.")]
    public Transform laserOrigin;

    [Tooltip(
        "DEBUG fallback target. Leave empty when PATController " +
        "is driving the beam.")]
    public Transform target;

    [Header("Beam Visual")]

    [Tooltip(
        "Cylinder representing the laser. " +
        "Its long axis must be local Y.")]
    public Transform laserBeam;

    [Tooltip("Beam thickness.")]
    public float beamRadius = 0.01f;

    private const float DefaultCylinderHeight = 2f;

    private Vector3? overrideTargetPosition;


    // ============================================================
    // UNITY
    // ============================================================

    private void LateUpdate()
    {
        if (laserOrigin == null)
            return;

        if (laserBeam == null)
            return;

        Vector3? targetPosition =
            GetCurrentTargetPosition();

        if (!targetPosition.HasValue)
        {
            laserBeam.gameObject.SetActive(false);
            return;
        }

        laserBeam.gameObject.SetActive(true);

        DrawBeam(
            laserOrigin.position,
            targetPosition.Value
        );
    }


    // ============================================================
    // PUBLIC API
    // ============================================================

    /// <summary>
    /// PATController calls this every frame.
    /// </summary>
    public void SetTargetPosition(
        Vector3 worldPosition
    )
    {
        overrideTargetPosition =
            worldPosition;
    }


    /// <summary>
    /// Return control to the optional debug target.
    /// </summary>
    public void ClearTargetOverride()
    {
        overrideTargetPosition = null;
    }


    /// <summary>
    /// Change laser thickness.
    /// </summary>
    public void SetBeamRadius(
        float radius
    )
    {
        beamRadius =
            Mathf.Max(
                0.0001f,
                radius
            );
    }


    // ============================================================
    // TARGET
    // ============================================================

    private Vector3? GetCurrentTargetPosition()
    {
        if (overrideTargetPosition.HasValue)
        {
            return overrideTargetPosition.Value;
        }

        if (target != null)
        {
            return target.position;
        }

        return null;
    }


    // ============================================================
    // DRAW
    // ============================================================

    private void DrawBeam(
        Vector3 origin,
        Vector3 targetPosition
    )
    {
        Vector3 direction =
            targetPosition - origin;

        float distance =
            direction.magnitude;

        if (distance < 0.001f)
            return;

        direction.Normalize();


        // --------------------------------------------------------
        // POSITION
        // --------------------------------------------------------

        laserBeam.position =
            origin +
            direction *
            (distance * 0.5f);


        // --------------------------------------------------------
        // ORIENTATION
        //
        // Cylinder's long axis = local Y.
        // --------------------------------------------------------

        laserBeam.rotation =
            Quaternion.FromToRotation(
                Vector3.up,
                direction
            );


        // --------------------------------------------------------
        // SCALE
        // --------------------------------------------------------

        float yScale =
            distance /
            DefaultCylinderHeight;

        laserBeam.localScale =
            new Vector3(
                beamRadius,
                yScale,
                beamRadius
            );
    }
}