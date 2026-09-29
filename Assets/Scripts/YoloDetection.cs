using System;
using UnityEngine;
using Unity.InferenceEngine;

public class YoloDetection : MonoBehaviour
{
    [Header("Tracker")]
    public Tracker tracker;

    [Header("Camera")]
    [Tooltip("The camera whose view is captured for YOLO.")]
    public Camera datasetCamera;

    [Header("Model")]
    public ModelAsset modelAsset;

    [Tooltip("Must match the image size used when exporting the model.")]
    public int inputSize = 640;

    [Range(0f, 1f)]
    public float confidenceThreshold = 0.4f;

    [Tooltip("1 = inference every frame. Increase to reduce inference load.")]
    public int inferenceIntervalFrames = 1;

    [Header("Disturbed Camera Feed")]
    [Tooltip("When enabled, the processed frame is exposed as DisturbedCameraFrame for the dashboard.")]
    public bool provideDisturbedCameraFeed = true;

    [Header("Debug Preview")]
    public bool showDebugPreview = false;
    public ScreenCorner previewCorner = ScreenCorner.BottomLeft;
    public Vector2 previewOffset = new Vector2(16f, 16f);
    public float previewSize = 200f;

    [Header("Debug")]
    public bool logMaxConfidence = false;
    public bool logInitialization = true;

    private Worker worker;
    private RenderTexture captureRT;
    private Texture2D readbackTex;
    private Tensor<float> inputTensor;
    private int frameCounter;
    private bool initialized;

    public float LastConfidence { get; private set; }
    public float LastInferenceMs { get; private set; }

    /// <summary>
    /// Latest disturbed camera frame. This is the same Texture2D sent to YOLO.
    /// </summary>
    public Texture2D DisturbedCameraFrame
    {
        get { return readbackTex; }
    }

    public bool HasDisturbedCameraFrame
    {
        get { return initialized && readbackTex != null; }
    }

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        initialized = false;

        if (modelAsset == null)
        {
            Debug.LogError("[YoloDetection] ModelAsset is not assigned.");
            enabled = false;
            return;
        }

        if (datasetCamera == null)
        {
            Debug.LogError("[YoloDetection] Dataset Camera is not assigned.");
            enabled = false;
            return;
        }

        if (inputSize <= 0)
        {
            Debug.LogError("[YoloDetection] inputSize must be greater than zero.");
            enabled = false;
            return;
        }

        try
        {
            var runtimeModel = ModelLoader.Load(modelAsset);

            if (runtimeModel == null)
            {
                Debug.LogError("[YoloDetection] ModelLoader returned a null runtime model.");
                enabled = false;
                return;
            }

            worker = new Worker(runtimeModel, BackendType.GPUCompute);

            captureRT = new RenderTexture(
                inputSize,
                inputSize,
                24,
                RenderTextureFormat.ARGB32
            );

            captureRT.name = "FSOC_YOLO_Capture";
            captureRT.filterMode = FilterMode.Bilinear;
            captureRT.wrapMode = TextureWrapMode.Clamp;
            captureRT.Create();

            readbackTex = new Texture2D(
                inputSize,
                inputSize,
                TextureFormat.RGB24,
                false
            );

            readbackTex.name = "FSOC_Disturbed_Camera_Frame";
            readbackTex.wrapMode = TextureWrapMode.Clamp;
            readbackTex.filterMode = FilterMode.Bilinear;

            inputTensor = new Tensor<float>(
                new TensorShape(1, 3, inputSize, inputSize)
            );

            if (worker == null ||
                captureRT == null ||
                !captureRT.IsCreated() ||
                readbackTex == null ||
                inputTensor == null)
            {
                Debug.LogError(
                    "[YoloDetection] Initialization failed. " +
                    "One or more inference resources are null."
                );

                CleanupResources();
                enabled = false;
                return;
            }

            initialized = true;

            if (logInitialization)
            {
                Debug.Log(
                    "[YoloDetection] INITIALIZED | " +
                    "Camera=" + (datasetCamera != null) + " | " +
                    "Model=" + (modelAsset != null) + " | " +
                    "Worker=" + (worker != null) + " | " +
                    "CaptureRT=" + (captureRT != null) + " | " +
                    "CaptureRTCreated=" + captureRT.IsCreated() + " | " +
                    "ReadbackTexture=" + (readbackTex != null) + " | " +
                    "InputTensor=" + (inputTensor != null)
                );
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[YoloDetection] Initialization failed:\n" + e);
            CleanupResources();
            enabled = false;
        }
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (tracker == null)
            return;

        if (datasetCamera == null)
            return;

        if (worker == null || captureRT == null ||
            readbackTex == null || inputTensor == null)
            return;

        frameCounter++;

        int interval = Mathf.Max(1, inferenceIntervalFrames);

        if (frameCounter % interval != 0)
            return;

        RunInference();
    }

    private void RunInference()
    {
        if (!ResourcesValid())
            return;

        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = datasetCamera.targetTexture;

        try
        {
            // -------------------------------------------------
            // 1. Render the tracking camera into the capture RT
            // -------------------------------------------------
            datasetCamera.targetTexture = captureRT;
            datasetCamera.Render();

            RenderTexture.active = captureRT;

            // -------------------------------------------------
            // 2. Copy the camera image to CPU Texture2D
            // -------------------------------------------------
            readbackTex.ReadPixels(
                new Rect(0, 0, inputSize, inputSize),
                0,
                0,
                false
            );

            // -------------------------------------------------
            // 3. Apply FSOC image disturbances
            // -------------------------------------------------
            if (provideDisturbedCameraFeed &&
                DisturbanceManager.Instance != null)
            {
                DisturbanceManager.Instance.ApplyImageDisturbances(
                    readbackTex
                );
            }

            // Upload the processed pixels.
            readbackTex.Apply(false, false);

            // -------------------------------------------------
            // 4. Convert disturbed image to YOLO tensor
            // -------------------------------------------------
            if (readbackTex == null)
            {
                Debug.LogError("[YoloDetection] readbackTex became null before ToTensor.");
                return;
            }

            if (inputTensor == null)
            {
                Debug.LogError("[YoloDetection] inputTensor became null before ToTensor.");
                return;
            }

            if (worker == null)
            {
                Debug.LogError("[YoloDetection] worker became null before ToTensor.");
                return;
            }

            try
            {
                TextureConverter.ToTensor(
                    readbackTex,
                    inputTensor,
                    new TextureTransform()
                );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    "[YoloDetection] TextureConverter.ToTensor failed:\n" +
                    e
                );

                tracker.ClearDetection();
                return;
            }

            // -------------------------------------------------
            // 5. Run model
            // -------------------------------------------------
            float inferenceStart = Time.realtimeSinceStartup;

            worker.Schedule(inputTensor);

            using Tensor<float> output =
                worker.PeekOutput() as Tensor<float>;

            if (output == null)
            {
                Debug.LogError(
                    "[YoloDetection] Worker returned a null output tensor."
                );

                tracker.ClearDetection();
                return;
            }

            using Tensor<float> outputCpu =
                output.ReadbackAndClone();

            LastInferenceMs =
                (Time.realtimeSinceStartup - inferenceStart) * 1000f;

            // -------------------------------------------------
            // 6. Optional confidence diagnostics
            // -------------------------------------------------
            if (logMaxConfidence)
            {
                float maxConf = 0f;

                for (int i = 0; i < outputCpu.shape[2]; i++)
                {
                    maxConf = Mathf.Max(
                        maxConf,
                        outputCpu[0, 4, i]
                    );
                }

                Debug.Log(
                    "[YoloDetection] Max confidence: " +
                    maxConf.ToString("F3")
                );
            }

            // -------------------------------------------------
            // 7. Parse best detection
            // -------------------------------------------------
            Vector2? bestBoxCenterPixels =
                ParseBestDetection(outputCpu);

            if (!bestBoxCenterPixels.HasValue)
            {
                LastConfidence = 0f;
                tracker.ClearDetection();
                return;
            }

            // -------------------------------------------------
            // 8. Pixel coordinates -> viewport coordinates
            // -------------------------------------------------
            float vx =
                bestBoxCenterPixels.Value.x /
                inputSize;

            float vy =
                1f -
                (
                    bestBoxCenterPixels.Value.y /
                    inputSize
                );

            tracker.SetDetection(
                new Vector2(
                    Mathf.Clamp01(vx),
                    Mathf.Clamp01(vy)
                )
            );
        }
        catch (Exception e)
        {
            Debug.LogError(
                "[YoloDetection] RunInference failed:\n" +
                e
            );

            if (tracker != null)
                tracker.ClearDetection();
        }
        finally
        {
            // Always restore the camera/render state.
            datasetCamera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
        }
    }

    private bool ResourcesValid()
    {
        if (worker == null)
        {
            Debug.LogError("[YoloDetection] Worker is null.");
            return false;
        }

        if (captureRT == null)
        {
            Debug.LogError("[YoloDetection] Capture RenderTexture is null.");
            return false;
        }

        if (!captureRT.IsCreated())
        {
            Debug.LogWarning(
                "[YoloDetection] Capture RenderTexture was not created. Recreating."
            );

            captureRT.Create();

            if (!captureRT.IsCreated())
            {
                Debug.LogError(
                    "[YoloDetection] Capture RenderTexture could not be created."
                );
                return false;
            }
        }

        if (readbackTex == null)
        {
            Debug.LogError("[YoloDetection] Readback Texture2D is null.");
            return false;
        }

        if (inputTensor == null)
        {
            Debug.LogError("[YoloDetection] Input tensor is null.");
            return false;
        }

        return true;
    }

    private Vector2? ParseBestDetection(Tensor<float> output)
    {
        if (output == null)
            return null;

        if (output.shape.rank < 3)
        {
            Debug.LogError(
                "[YoloDetection] Unexpected output tensor rank: " +
                output.shape.rank
            );

            return null;
        }

        int numAnchors = output.shape[2];

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

        LastConfidence =
            best.HasValue ? bestConf : 0f;

        return best;
    }

    private void OnGUI()
    {
        if (!showDebugPreview)
            return;

        if (readbackTex == null)
            return;

        Rect r =
            DisturbancePanel.Anchor(
                previewCorner,
                new Vector2(previewSize, previewSize),
                previewOffset
            );

        GUI.DrawTexture(
            r,
            readbackTex,
            ScaleMode.ScaleToFit,
            false
        );
    }

    private void OnDisable()
    {
        if (tracker != null)
            tracker.ClearDetection();
    }

    private void OnDestroy()
    {
        CleanupResources();
    }

    private void CleanupResources()
    {
        initialized = false;

        if (worker != null)
        {
            worker.Dispose();
            worker = null;
        }

        if (inputTensor != null)
        {
            inputTensor.Dispose();
            inputTensor = null;
        }

        if (captureRT != null)
        {
            if (captureRT.IsCreated())
                captureRT.Release();

            Destroy(captureRT);
            captureRT = null;
        }

        if (readbackTex != null)
        {
            Destroy(readbackTex);
            readbackTex = null;
        }
    }
}
