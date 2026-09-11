using UnityEngine;

/// <summary>
/// LaserCylinderBeam
///
/// Purely visual/geometric component. Given an origin and a target
/// position, it positions, orients, and scales a Unity cylinder so
/// it visually represents the laser beam connecting the two points.
///
/// This script deliberately contains NO detection, tracking, Kalman
/// filtering, or PAT logic. It only ever asks "where does the beam
/// start, and where does it currently need to point?" and draws the
/// cylinder accordingly — it is the last step in the pipeline:
///
///     YOLO Detection -> Tracker -> Kalman Filter -> PAT Controller
///                                                        |
///                                                        v
///                                              LaserCylinderBeam
///                                                        |
///                                                        v
///                                                  Visible Laser
///
/// DEBUG MODE (current): the beam points directly at `target`
/// (e.g. sat3), so origin/orientation/length/direction can be
/// verified in isolation before tracking is wired in.
///
/// FINAL MODE (later): the PAT Controller calls
/// SetTargetPosition(predictedPosition) every frame instead, and
/// `target` can be left unassigned.
/// </summary>
public class LaserCylinderBeam : MonoBehaviour
{
    [Header("Beam Endpoints")]

    [Tooltip("Where the laser physically originates (Sat1's optical terminal).")]
    public Transform laserOrigin;

    [Tooltip("DEBUG ONLY — the beam points directly at this transform (e.g. sat3). " +
             "Leave empty once the PAT Controller drives the beam via SetTargetPosition().")]
    public Transform target;

    [Header("Beam Visual")]

    [Tooltip("The cylinder Transform that visually represents the beam. Its long axis must be local Y.")]
    public Transform laserBeam;

    [Tooltip("Beam thickness (X/Z scale). Length (Y scale) is calculated automatically every frame and is never touched here.")]
    public float beamRadius = 0.01f;

    // Unity's default primitive cylinder is 2 units tall in local
    // space (y = -1 to y = +1), so a Y-scale of 1 = a 2-unit-long
    // cylinder. This converts "desired length" -> "correct Y scale".
    private const float DefaultCylinderHeight = 2f;

    // Set by SetTargetPosition() when an upstream system (PAT
    // Controller) is driving the beam instead of the debug `target`.
    private Vector3? overrideTargetPosition = null;


    void LateUpdate()
    {
        // LateUpdate (not Update) guarantees this runs AFTER every
        // other script's Update() this frame — including
        // PATController's, which is what actually calls
        // SetTargetPosition(). This avoids a one-frame lag where
        // the beam would otherwise draw using last frame's target.
        if (laserOrigin == null || laserBeam == null)
            return;

        Vector3? targetPosition = GetCurrentTargetPosition();

        if (targetPosition == null)
            return;

        DrawBeam(laserOrigin.position, targetPosition.Value);
    }


    // ============================================================
    // PUBLIC API
    //
    // This is how the PAT Controller will eventually drive this
    // script, instead of the debug `target` Transform above.
    // ============================================================

    /// <summary>
    /// Point the beam at a specific world-space position. Call this
    /// every frame from the PAT Controller once tracking/prediction
    /// is wired in — it overrides the debug `target` Transform.
    /// </summary>
    public void SetTargetPosition(Vector3 worldPosition)
    {
        overrideTargetPosition = worldPosition;
    }

    /// <summary>
    /// Clears any override and reverts to following the debug
    /// `target` Transform (if one is assigned).
    /// </summary>
    public void ClearTargetOverride()
    {
        overrideTargetPosition = null;
    }

    /// <summary>
    /// Change beam thickness (X/Z) without affecting length.
    /// </summary>
    public void SetBeamRadius(float radius)
    {
        beamRadius = radius;
    }


    // ============================================================
    // INTERNAL
    // ============================================================

    private Vector3? GetCurrentTargetPosition()
    {
        if (overrideTargetPosition.HasValue)
            return overrideTargetPosition.Value;

        if (target != null)
            return target.position;

        return null;
    }

    private void DrawBeam(Vector3 origin, Vector3 targetPos)
    {
        Vector3 direction = targetPos - origin;
        float distance = direction.magnitude;

        if (distance < 0.001f)
            return;

        direction.Normalize();

        // 1) Position the cylinder at the midpoint between origin and target.
        laserBeam.position = origin + direction * (distance * 0.5f);

        // 2) Rotate the cylinder so its local Y axis points toward the target.
        laserBeam.rotation = Quaternion.FromToRotation(Vector3.up, direction);

        // 3) Scale ONLY the Y axis (length) to span the full distance.
        //    X/Z (thickness) always stay at beamRadius.
        float yScale = distance / DefaultCylinderHeight;

        laserBeam.localScale = new Vector3(beamRadius, yScale, beamRadius);
    }


#if UNITY_EDITOR
    // Editor-only: draw a line in the Scene view so origin/target
    // references can be checked visually even before hitting Play.
    void OnDrawGizmos()
    {
        if (laserOrigin == null)
            return;

        Vector3? t = Application.isPlaying
            ? GetCurrentTargetPosition()
            : (target != null ? target.position : (Vector3?)null);

        if (t == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawLine(laserOrigin.position, t.Value);
    }
#endif
}