using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Evaluation metrics recorder for the 26169 FSOC coarse-alignment benchmarks.
/// Measures acquisition/re-acquisition, centroiding error, RMSE, lock retention,
/// target loss and processing FPS. CSV export is supported in standalone builds.
/// </summary>
public class FSOCPerformanceRecorder : MonoBehaviour
{
    [Serializable]
    public class RunRecord
    {
        public string scenario;
        public float duration;
        public float acquisitionTime;
        public float reacquisitionTime;
        public float averageErrorPx;
        public float maxErrorPx;
        public float rmsePx;
        public float lockRetentionPct;
        public float targetLossPct;
        public float averageFps;
        public float lastInferenceMs;
        public string disturbanceState;
        public string timestamp;
    }

    public Tracker tracker;
    public YoloDetection detector;
    public Camera evaluationCamera;
    public float referenceWidthPx = 640f;
    public float referenceHeightPx = 480f;

    public bool IsRecording { get; private set; }
    public float Duration { get; private set; }
    public float AcquisitionTime { get; private set; } = -1f;
    public float ReacquisitionTime { get; private set; } = -1f;
    public float AverageErrorPx => errorSamples > 0 ? errorSum / errorSamples : 0f;
    public float MaxErrorPx { get; private set; }
    public float RMSEPx => errorSamples > 0 ? Mathf.Sqrt(squaredErrorSum / errorSamples) : 0f;
    public float LockRetentionPct => Duration > 0f ? 100f * lockedSeconds / Duration : 0f;
    public float TargetLossPct => Duration > 0f ? 100f * lostSeconds / Duration : 0f;
    public float AverageFps => frameSeconds > 0f ? frameCount / frameSeconds : 0f;
    public RunRecord LastRun { get; private set; }
    public IReadOnlyList<RunRecord> History => history;

    private readonly List<RunRecord> history = new List<RunRecord>();
    private float errorSum;
    private float squaredErrorSum;
    private float errorSamples;
    private float lockedSeconds;
    private float lostSeconds;
    private float frameSeconds;
    private int frameCount;
    private float acquisitionStart;
    private float lossStart = -1f;
    private float currentLockStart;
    private bool previousDetected;
    private string scenario = "Live Scenario";

    public void Begin(string scenarioName = "Live Scenario")
    {
        ResetCounters();
        scenario = string.IsNullOrWhiteSpace(scenarioName) ? "Live Scenario" : scenarioName;
        IsRecording = true;
        acquisitionStart = Time.unscaledTime;
        previousDetected = tracker != null && tracker.targetDetected;
        if (previousDetected)
        {
            AcquisitionTime = 0f;
            currentLockStart = Time.unscaledTime;
        }
    }

    public RunRecord Stop()
    {
        if (!IsRecording) return LastRun;
        SampleState(true);
        IsRecording = false;

        LastRun = new RunRecord
        {
            scenario = scenario,
            duration = Duration,
            acquisitionTime = AcquisitionTime < 0f ? Duration : AcquisitionTime,
            reacquisitionTime = ReacquisitionTime < 0f ? 0f : ReacquisitionTime,
            averageErrorPx = AverageErrorPx,
            maxErrorPx = MaxErrorPx,
            rmsePx = RMSEPx,
            lockRetentionPct = LockRetentionPct,
            targetLossPct = TargetLossPct,
            averageFps = AverageFps,
            lastInferenceMs = detector != null ? detector.LastInferenceMs : 0f,
            disturbanceState = DisturbanceManager.Instance != null ? DisturbanceManager.Instance.DescribeState() : "unavailable",
            timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };
        history.Add(LastRun);
        return LastRun;
    }

    public void ClearHistory() => history.Clear();

    private void Update()
    {
        if (!IsRecording || tracker == null) return;
        SampleState(false);
    }

    private void SampleState(bool finalSample)
    {
        float dt = finalSample ? 0f : Time.unscaledDeltaTime;
        Duration += dt;
        frameSeconds += dt;
        frameCount++;

        bool detected = tracker.targetDetected;
        if (detected)
        {
            lockedSeconds += dt;
            if (!previousDetected)
            {
                float now = Time.unscaledTime;
                if (AcquisitionTime < 0f)
                    AcquisitionTime = now - acquisitionStart;
                else if (lossStart >= 0f)
                    ReacquisitionTime = now - lossStart;
                currentLockStart = now;
                lossStart = -1f;
            }

            Vector2 p = tracker.detection;
            Vector2 centre = new Vector2(0.5f, 0.5f);
            Vector2 error = p - centre;
            float pxX = error.x * referenceWidthPx;
            float pxY = error.y * referenceHeightPx;
            float mag = new Vector2(pxX, pxY).magnitude;
            errorSum += mag;
            squaredErrorSum += mag * mag;
            MaxErrorPx = Mathf.Max(MaxErrorPx, mag);
            errorSamples++;
        }
        else
        {
            lostSeconds += dt;
            if (previousDetected)
                lossStart = Time.unscaledTime;
        }

        previousDetected = detected;
    }

    private void ResetCounters()
    {
        Duration = 0f;
        AcquisitionTime = -1f;
        ReacquisitionTime = -1f;
        errorSum = 0f;
        squaredErrorSum = 0f;
        errorSamples = 0f;
        MaxErrorPx = 0f;
        lockedSeconds = 0f;
        lostSeconds = 0f;
        frameSeconds = 0f;
        frameCount = 0;
        acquisitionStart = Time.unscaledTime;
        lossStart = -1f;
        currentLockStart = 0f;
        previousDetected = false;
    }

    public string ExportHistoryCsv()
    {
        string path = Path.Combine(Application.persistentDataPath, "FSOC_Performance_Log.csv");
        using (var sw = new StreamWriter(path, false))
        {
            sw.WriteLine("Timestamp,Scenario,Duration_s,Acquisition_s,Reacquisition_s,AverageError_px,MaxError_px,RMSE_px,LockRetention_pct,TargetLoss_pct,AverageFPS,LastInference_ms,Disturbances");
            foreach (var r in history)
            {
                string disturbance = (r.disturbanceState ?? "").Replace(",", ";");
                sw.WriteLine($"{r.timestamp},{r.scenario},{r.duration:F3},{r.acquisitionTime:F3},{r.reacquisitionTime:F3},{r.averageErrorPx:F3},{r.maxErrorPx:F3},{r.rmsePx:F3},{r.lockRetentionPct:F2},{r.targetLossPct:F2},{r.averageFps:F2},{r.lastInferenceMs:F2},\"{disturbance}\"");
            }
        }
        return path;
    }
}
