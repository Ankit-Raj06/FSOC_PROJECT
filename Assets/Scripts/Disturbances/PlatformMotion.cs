using UnityEngine;

/// <summary>
/// Moves the terminal's mount (the child transform that holds the gimbal,
/// camera and laser origin) to simulate platform motion.
///
/// Assign `mount` to a CHILD of the satellite that is not driven by NBody
/// physics. Motion is applied as a local-position delta on top of whatever
/// else moves it, so it never fights other scripts.
///
/// Modes: Linear (mandatory), Circular, Random, Spiral, Figure8.
/// All modes start at zero offset, and are deterministic for a given seed.
/// </summary>
public class PlatformMotion : MonoBehaviour
{
    public DisturbanceManager manager;

    [Tooltip("Child transform to move. Empty = this transform.")]
    public Transform mount;

    private Vector3 applied;
    private readonly float[] phases = new float[8];
    private int builtSeed;
    private bool built;

    private static readonly float[] FreqMul = { 1f, 1.73f, 2.91f, 4.37f };

    private void Start()
    {
        if (mount == null) mount = transform;
        if (manager == null) manager = DisturbanceManager.Instance;
    }

    private void Update()
    {
        if (manager == null)
        {
            manager = DisturbanceManager.Instance;
            if (manager == null) return;
        }

        var m = manager.settings.platform;

        Vector3 offset = Vector3.zero;
        if (manager.disturbancesEnabled)
        {
            EnsurePhases(manager.settings.seed);
            offset = ComputeOffset(m, manager.SimTime);
        }

        mount.localPosition += offset - applied;
        applied = offset;
    }

    private void OnDisable()
    {
        if (mount != null)
            mount.localPosition -= applied;
        applied = Vector3.zero;
    }

    // ------------------------------------------------------------

    private Vector3 ComputeOffset(PlatformMotionSettings m, float t)
    {
        float th = Mathf.Deg2Rad * m.angularSpeedDeg * t;

        switch (m.type)
        {
            case PlatformMotionType.Linear:
            {
                Vector3 d = m.linearDirection.sqrMagnitude > 1e-6f
                    ? m.linearDirection.normalized
                    : Vector3.right;

                float dist = m.linearSpeed * t;
                if (m.linearRange > 0f)
                    dist = Mathf.PingPong(dist, m.linearRange);

                return d * dist;
            }

            case PlatformMotionType.Circular:
                // Circle passes through the origin so there is no jump at t=0.
                return OnPlane(m.plane,
                    m.amplitude * (Mathf.Cos(th) - 1f),
                    m.amplitude * Mathf.Sin(th));

            case PlatformMotionType.Spiral:
            {
                float phase = Mathf.PingPong(t / Mathf.Max(0.1f, m.spiralRadialSeconds), 1f);
                float r = Mathf.Lerp(m.spiralMinRadius, m.spiralMaxRadius, phase);
                return OnPlane(m.plane,
                    r * Mathf.Cos(th) - m.spiralMinRadius,
                    r * Mathf.Sin(th));
            }

            case PlatformMotionType.Figure8:
                return OnPlane(m.plane,
                    m.amplitude * Mathf.Sin(th),
                    0.5f * m.amplitude * Mathf.Sin(2f * th));

            case PlatformMotionType.Random:
                return OnPlane(m.plane,
                    RandomAxis(m, t, 0) - RandomAxis(m, 0f, 0),
                    RandomAxis(m, t, 1) - RandomAxis(m, 0f, 1));

            default:
                return Vector3.zero;
        }
    }

    /// <summary>Smooth, bounded, seeded wander: weighted sum of 4 sinusoids per axis.</summary>
    private float RandomAxis(PlatformMotionSettings m, float t, int axis)
    {
        float sum = 0f, norm = 0f, w = 1f;

        for (int k = 0; k < 4; k++)
        {
            float f = m.randomFrequencyHz * FreqMul[k];
            sum += w * Mathf.Sin(2f * Mathf.PI * f * t + phases[axis * 4 + k]);
            norm += w;
            w *= 0.5f;
        }

        return m.amplitude * sum / norm;
    }

    private void EnsurePhases(int seed)
    {
        if (built && builtSeed == seed)
            return;

        var rng = new DetRng(DetRng.Mix(seed, 77));
        for (int i = 0; i < phases.Length; i++)
            phases[i] = rng.NextFloat() * 2f * Mathf.PI;

        builtSeed = seed;
        built = true;
    }

    private static Vector3 OnPlane(MotionPlane plane, float a, float b)
    {
        switch (plane)
        {
            case MotionPlane.XY: return new Vector3(a, b, 0f);
            case MotionPlane.YZ: return new Vector3(0f, a, b);
            default:             return new Vector3(a, 0f, b);   // XZ
        }
    }
}
