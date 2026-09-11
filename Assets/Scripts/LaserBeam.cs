using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class LaserBeam : MonoBehaviour
{
    [Header("Laser")]
    public Transform laserOrigin;
    public Transform targetSatellite;

    [Header("Beam Settings")]
    public float beamWidth = 0.01f;

    private LineRenderer line;

    void Start()
    {
        line = GetComponent<LineRenderer>();

        line.useWorldSpace = true;
        line.positionCount = 2;

        line.startWidth = beamWidth;
        line.endWidth = beamWidth;
    }

    void Update()
    {
        if (laserOrigin == null || targetSatellite == null)
        {
            line.enabled = false;
            return;
        }

        line.enabled = true;

        line.SetPosition(0, laserOrigin.position);
        line.SetPosition(1, targetSatellite.position);
    }
}