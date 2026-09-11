using UnityEngine;

public class DatasetCameraAim : MonoBehaviour
{
    public Transform laserOrigin;
    public Transform targetSatellite;

    void LateUpdate()
    {
        if (laserOrigin == null || targetSatellite == null)
            return;

        // Put the camera at the exact position of the laser origin
        transform.position = laserOrigin.position;

        // Calculate the direction from Sat1 / laser origin to Sat3
        Vector3 direction =
            targetSatellite.position - laserOrigin.position;

        // Point the camera in the exact same direction as the laser
        if (direction.sqrMagnitude > 0.0001f)
        {
            transform.rotation =
                Quaternion.LookRotation(direction.normalized, Vector3.up);
        }
    }
}