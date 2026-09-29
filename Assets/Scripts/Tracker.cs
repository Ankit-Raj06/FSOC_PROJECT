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
        Initialize();
    }

    private void Initialize()
    {
        kalman = new KalmanFilter();

        detection = Vector2.zero;
        trackedPosition = Vector2.zero;
        predictedPosition = Vector2.zero;
        trackedVelocity = Vector2.zero;

        targetDetected = false;
    }

    private void Update()
    {
        if (!targetDetected)
            return;

        if (kalman == null)
        {
            Debug.LogWarning(
                "[Tracker] KalmanFilter was unexpectedly null. Reinitializing."
            );

            Initialize();
            return;
        }

        float deltaTime = Time.deltaTime;

        if (deltaTime <= 0f)
            return;

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
        detection = new Vector2(
            Mathf.Clamp01(position.x),
            Mathf.Clamp01(position.y)
        );

        targetDetected = true;
    }

    public void ClearDetection()
    {
        targetDetected = false;
    }

    public Vector2 GetPredictedPosition()
    {
        if (kalman == null)
            return detection;

        return predictedPosition;
    }
}
