using UnityEngine;

public class Tracker : MonoBehaviour
{
    [Header("Detection")]
    public Vector2 detection;

    public bool targetDetected;

    [Header("Prediction")]
    public float predictionTime = 0.15f;

    [Header("Debug")]
    public Vector2 trackedPosition;
    public Vector2 predictedPosition;
    public Vector2 trackedVelocity;

    private KalmanFilter kalman;

    private void Awake()
    {
        kalman = new KalmanFilter();
    }

    private void Update()
    {
        if (!targetDetected)
            return;

        float deltaTime = Time.deltaTime;

        trackedPosition =
            kalman.Update(
                detection,
                deltaTime
            );

        predictedPosition =
            kalman.Predict(
                predictionTime
            );

        trackedVelocity =
            kalman.Velocity;
    }

    public void SetDetection(Vector2 position)
    {
        detection = position;
        targetDetected = true;
    }

    public void ClearDetection()
    {
        targetDetected = false;
    }

    public Vector2 GetPredictedPosition()
    {
        return predictedPosition;
    }
}