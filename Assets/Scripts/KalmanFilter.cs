using UnityEngine;

public class KalmanFilter
{
    private Vector2 position;
    private Vector2 velocity;

    private float positionVariance = 1f;
    private float processNoise = 0.01f;
    private float measurementNoise = 0.1f;

    private bool initialized = false;

    public Vector2 Update(Vector2 measurement, float deltaTime)
    {
        if (!initialized)
        {
            position = measurement;
            velocity = Vector2.zero;
            initialized = true;

            return position;
        }

        float dt = Mathf.Max(deltaTime, 0.0001f);

        // Predict position.
        Vector2 predictedPosition =
            position + velocity * dt;

        float predictedVariance =
            positionVariance + processNoise;

        // Kalman gain.
        float denominator =
            predictedVariance + measurementNoise;

        float kalmanGain =
            denominator > 0f
                ? predictedVariance / denominator
                : 0f;

        // Correct prediction using measurement.
        Vector2 newPosition =
            predictedPosition +
            kalmanGain *
            (measurement - predictedPosition);

        // Estimate velocity.
        velocity =
            (newPosition - position) / dt;

        position = newPosition;

        positionVariance =
            Mathf.Max(
                0f,
                (1f - kalmanGain) * predictedVariance
            );

        return position;
    }

    public Vector2 Predict(float secondsAhead)
    {
        return position +
               velocity *
               Mathf.Max(0f, secondsAhead);
    }

    public Vector2 Position
    {
        get { return position; }
    }

    public Vector2 Velocity
    {
        get { return velocity; }
    }
}
