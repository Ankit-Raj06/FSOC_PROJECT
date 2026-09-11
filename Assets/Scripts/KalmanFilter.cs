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

        // Predict position
        Vector2 predictedPosition =
            position + velocity * deltaTime;

        float predictedVariance =
            positionVariance + processNoise;

        // Kalman gain
        float kalmanGain =
            predictedVariance /
            (predictedVariance + measurementNoise);

        // Correct prediction using measurement
        Vector2 newPosition =
            predictedPosition +
            kalmanGain * (measurement - predictedPosition);

        // Estimate velocity
        if (deltaTime > 0.0001f)
        {
            velocity =
                (newPosition - position) / deltaTime;
        }

        position = newPosition;

        positionVariance =
            (1f - kalmanGain) * predictedVariance;

        return position;
    }

    public Vector2 Predict(float secondsAhead)
    {
        return position + velocity * secondsAhead;
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