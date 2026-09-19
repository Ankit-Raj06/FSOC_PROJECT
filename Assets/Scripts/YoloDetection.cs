using UnityEngine;
using Unity.InferenceEngine;

public class YoloDetection : MonoBehaviour
{
    public Tracker tracker;

    [Header("Debug")]
public bool showDebugPreview = false;
public ScreenCorner previewCorner = ScreenCorner.BottomLeft;
public Vector2 previewOffset = new Vector2(16, 16);
public float previewSize = 200f;

private void OnGUI()
{
    if (showDebugPreview && readbackTex != null)
    {
        Rect r = DisturbancePanel.Anchor(previewCorner, new Vector2(previewSize, previewSize), previewOffset);
        GUI.DrawTexture(r, readbackTex);
    }
}

    [Header("Camera")]
    [Tooltip("The camera whose view YOLO runs inference on.")]
    public Camera datasetCamera;

    [Header("Model")]
    public ModelAsset modelAsset;

    [Tooltip("Must match the imgsz used at export.")]
    public int inputSize = 640;

    [Range(0f, 1f)]
    public float confidenceThreshold = 0.4f;

    [Tooltip("How many frames to wait between inference passes. " +
             "1 = every frame. Raise this if inference cost is too high at high Time Scale.")]
    public int inferenceIntervalFrames = 1;

    [Header("Debug")]
    [Tooltip("Logs the single highest confidence value seen each inference pass, " +
             "regardless of threshold. Use this to calibrate confidenceThreshold, " +
             "then turn it back off.")]
    public bool logMaxConfidence = false;

    private Worker worker;
    private RenderTexture captureRT;
    private Texture2D readbackTex;
    private Tensor<float> inputTensor;
    private int frameCounter;

    private void Awake()
    {
        var runtimeModel = ModelLoader.Load(modelAsset);
        worker = new Worker(runtimeModel, BackendType.GPUCompute);

        captureRT = new RenderTexture(inputSize, inputSize, 24);
        readbackTex = new Texture2D(inputSize, inputSize, TextureFormat.RGB24, false);

        // Preallocated once, reused every frame — avoids per-frame
        // tensor allocation while running inference continuously.
        inputTensor = new Tensor<float>(new TensorShape(1, 3, inputSize, inputSize));
    }

    private void Update()
    {
        if (tracker == null || datasetCamera == null || modelAsset == null)
            return;

        frameCounter++;
        if (frameCounter % inferenceIntervalFrames != 0)
            return;

        RunInference();
    }

    private void RunInference()
    {
        // 1) Capture the camera's current view at model input resolution
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture prevTarget = datasetCamera.targetTexture;

        datasetCamera.targetTexture = captureRT;
	datasetCamera.Render();
	RenderTexture.active = captureRT;

	readbackTex.ReadPixels(new Rect(0, 0, inputSize, inputSize), 0, 0);

	// NEW: inject disturbances into the frame YOLO will see
	if (DisturbanceManager.Instance != null)
    		DisturbanceManager.Instance.ApplyImageDisturbances(readbackTex);

	readbackTex.Apply();

        datasetCamera.targetTexture = prevTarget;
        RenderTexture.active = prevActive;

        // 2) Convert to input tensor (NCHW, normalized 0-1) — writes
        //    into the preallocated inputTensor in place.
        TextureConverter.ToTensor(readbackTex, inputTensor, new TextureTransform());

        // 3) Run the model
        worker.Schedule(inputTensor);
        using Tensor<float> output = worker.PeekOutput() as Tensor<float>;
        using Tensor<float> outputCpu = output.ReadbackAndClone();

        // outputCpu shape is (1, 5, 8400): rows = [cx, cy, w, h, objectness],
        // already in pixel space (0..inputSize) thanks to the baked-in
        // decode ops in the export — no extra sigmoid/stride math needed.

        if (logMaxConfidence)
        {
            float maxConf = 0f;
            for (int i = 0; i < outputCpu.shape[2]; i++)
                maxConf = Mathf.Max(maxConf, outputCpu[0, 4, i]);
            Debug.Log("Max confidence this frame: " + maxConf);
        }

        // 4) Parse output, find best detection above threshold
        Vector2? bestBoxCenterPixels = ParseBestDetection(outputCpu);

        if (bestBoxCenterPixels == null)
        {
            tracker.ClearDetection(); // no valid detection this pass — tell the tracker, don't just go silent
            return;
        }

        // 5) Convert pixel-space center -> normalized viewport (0-1)
        // Image space: (0,0) = top-left. Viewport space: (0,0) = bottom-left.
        // X maps directly; Y must be flipped.
        float vx = bestBoxCenterPixels.Value.x / inputSize;
        float vy = 1f - (bestBoxCenterPixels.Value.y / inputSize);

        tracker.SetDetection(new Vector2(vx, vy));
    }

    private Vector2? ParseBestDetection(Tensor<float> output)
    {
        int numAnchors = output.shape[2]; // 8400

        float bestConf = confidenceThreshold;
        Vector2? best = null;

        for (int i = 0; i < numAnchors; i++)
        {
            float conf = output[0, 4, i];
            if (conf > bestConf)
            {
                bestConf = conf;
                float cx = output[0, 0, i];
                float cy = output[0, 1, i];
                best = new Vector2(cx, cy);
            }
        }

        return best;
    }

    private void OnDestroy()
    {
        worker?.Dispose();
        inputTensor?.Dispose();
    }
}
