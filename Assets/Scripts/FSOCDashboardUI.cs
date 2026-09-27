using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// FSOC evaluation dashboard.
///
/// Layout is intentionally simple and fixed:
///     HEADER
///     LEFT PIPELINE | CENTER CAMERA / PAGE | RIGHT TELEMETRY
///     FOOTER NAVIGATION
///
/// The dashboard does not create full-screen tab panels anymore. This prevents
/// the overlap seen in the previous version. Tabs only replace the contents of
/// the CENTER column; the pipeline and telemetry columns remain visible.
///
/// Optional performance fields are read through reflection so this UI remains
/// compatible with the current Tracker/YoloDetection/PATController scripts even
/// if the performance recorder's public API changes.
/// </summary>
public class FSOCDashboardUI : MonoBehaviour
{
    // ============================================================
    // PAGE
    // ============================================================

    private enum Page
    {
        Run,
        Scenario,
        Disturbances,
        Performance,
        Video,
        Logs
    }

    // ============================================================
    // SYSTEM REFERENCES
    // ============================================================

    private Tracker tracker;
    private YoloDetection detector;
    private PATController pat;
    private DisturbanceManager disturbances;
    private Camera mainCamera;
    private MonoBehaviour performanceRecorder;

    // ============================================================
    // UI ROOTS
    // ============================================================

    private Canvas canvas;
    private RectTransform centerContent;

    // ============================================================
    // LIVE TEXT REFERENCES
    // ============================================================

    private TMP_Text headerStatus;
    private TMP_Text pipelineText;
    private TMP_Text statusText;
    private TMP_Text confidenceText;
    private TMP_Text errorText;
    private TMP_Text fpsText;
    private TMP_Text acquisitionText;
    private TMP_Text reacquisitionText;
    private TMP_Text lockText;
    private TMP_Text rmseText;
    private TMP_Text maxErrorText;
    private TMP_Text inferenceText;
    private TMP_Text modeText;
    private TMP_Text disturbanceText;

    // ============================================================
    // COLORS
    // ============================================================

    private static readonly Color Background =
        new Color(0.035f, 0.040f, 0.045f, 1f);

    private static readonly Color Panel =
        new Color(0.045f, 0.055f, 0.065f, 0.88f);

    private static readonly Color PanelTransparent =
        new Color(0f, 0f, 0f, 0f);

    private static readonly Color Border =
        new Color(0.82f, 0.86f, 0.88f, 0.90f);

    private static readonly Color Text =
        new Color(0.92f, 0.94f, 0.96f, 1f);

    private static readonly Color DimText =
        new Color(0.62f, 0.66f, 0.70f, 1f);

    private static readonly Color Accent =
        new Color(0.35f, 0.78f, 1f, 1f);

    private static readonly Color Good =
        new Color(0.25f, 1f, 0.55f, 1f);

    private static readonly Color Warning =
        new Color(1f, 0.75f, 0.25f, 1f);

    private static readonly Color Bad =
        new Color(1f, 0.35f, 0.35f, 1f);

    // ============================================================
    // UNITY
    // ============================================================

    private void Awake()
    {
        FindSystems();
        ConfigureCameraForSpaceView();
        BuildDashboard();
        ShowPage(Page.Run);
    }

    private void Update()
    {
        RefreshLiveTelemetry();
    }

    // ============================================================
    // FIND EXISTING SYSTEMS
    // ============================================================

    private void FindSystems()
    {
        tracker = FindFirstObjectByType<Tracker>();
        detector = FindFirstObjectByType<YoloDetection>();
        pat = FindFirstObjectByType<PATController>();
        disturbances = FindFirstObjectByType<DisturbanceManager>();
        mainCamera = Camera.main != null
            ? Camera.main
            : FindFirstObjectByType<Camera>();

        performanceRecorder = FindOptionalComponent("FSOCPerformanceRecorder");
    }

    private void ConfigureCameraForSpaceView()
    {
        if (mainCamera == null)
            return;

        // The dashboard center is intentionally transparent so the real Unity
        // camera remains visible. Ensure the camera itself does not clear to
        // the default grey color. Prefer the existing skybox when one exists;
        // otherwise use a black space background.
        if (RenderSettings.skybox != null)
        {
            mainCamera.clearFlags = CameraClearFlags.Skybox;
        }
        else
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = Color.black;
        }
    }

    private MonoBehaviour FindOptionalComponent(string typeName)
    {
        MonoBehaviour[] components =
            FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);

        foreach (MonoBehaviour component in components)
        {
            if (component != null &&
                component.GetType().Name == typeName)
            {
                return component;
            }
        }

        return null;
    }

    // ============================================================
    // BUILD DASHBOARD
    // ============================================================

    private void BuildDashboard()
    {
        canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();

        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = gameObject.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (gameObject.GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        // IMPORTANT: the dashboard itself must NOT paint over the 3D scene.
        // Only the header, left panel, right panel and footer are opaque UI.
        // The center remains the real Unity camera view.
        GameObject root = CreatePanel(
            canvas.transform,
            "DashboardBackground",
            PanelTransparent,
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            Vector2.zero,
            Vector2.zero,
            false
        );

        BuildHeader(root.transform);
        BuildMainColumns(root.transform);
        BuildFooter(root.transform);
    }

    // ============================================================
    // HEADER
    // ============================================================

    private void BuildHeader(Transform parent)
    {
        GameObject header = CreatePanel(
            parent,
            "Header",
            Background,
            new Vector2(0f, 0.885f),
            new Vector2(1f, 0.985f),
            new Vector2(18f, 0f),
            new Vector2(-18f, 0f),
            true
        );

        TMP_Text title = CreateText(
            header.transform,
            "FSOC COARSE ALIGNMENT SYSTEM",
            25f,
            Text,
            TextAlignmentOptions.Left
        );

        SetTopLeft(title.rectTransform, 18f, 14f, 720f, 34f);

        TMP_Text subtitle = CreateText(
            header.transform,
            "AI CAMERA TRACKING / COARSE PAT",
            13f,
            Accent,
            TextAlignmentOptions.Left
        );

        SetTopLeft(subtitle.rectTransform, 18f, 45f, 600f, 25f);

        headerStatus = CreateText(
            header.transform,
            "●  SYSTEM READY",
            15f,
            Good,
            TextAlignmentOptions.Right
        );

        SetTopRight(headerStatus.rectTransform, 18f, 16f, 260f, 30f);
    }

    // ============================================================
    // MAIN THREE-COLUMN AREA
    // ============================================================

    private void BuildMainColumns(Transform parent)
    {
        // LEFT: 18% of width
        GameObject left = CreatePanel(
            parent,
            "SystemPipeline",
            Panel,
            new Vector2(0f, 0.085f),
            new Vector2(0.205f, 0.875f),
            new Vector2(18f, 0f),
            new Vector2(-6f, 0f),
            true
        );

        // CENTER: approximately 61%
        GameObject center = CreatePanel(
            parent,
            "CenterView",
            PanelTransparent,
            new Vector2(0.205f, 0.085f),
            new Vector2(0.805f, 0.875f),
            new Vector2(6f, 0f),
            new Vector2(-6f, 0f),
            true
        );

        // RIGHT: approximately 18%
        GameObject right = CreatePanel(
            parent,
            "Telemetry",
            Panel,
            new Vector2(0.805f, 0.085f),
            new Vector2(1f, 0.875f),
            new Vector2(6f, 0f),
            new Vector2(-18f, 0f),
            true
        );

        BuildPipeline(left.transform);
        BuildCenter(center.transform);
        BuildTelemetry(right.transform);
    }

    // ============================================================
    // LEFT PIPELINE
    // ============================================================

    private void BuildPipeline(Transform parent)
    {
        TMP_Text heading = CreateText(
            parent,
            "SYSTEM\nPIPELINE",
            16f,
            Text,
            TextAlignmentOptions.Left
        );

        SetTopLeft(heading.rectTransform, 16f, 45f, 240f, 65f);

        pipelineText = CreateText(
            parent,
            "",
            14f,
            Text,
            TextAlignmentOptions.Left
        );

        SetTopLeft(
            pipelineText.rectTransform,
            16f,
            130f,
            280f,
            390f
        );
    }

    // ============================================================
    // CENTER VIEW
    // ============================================================

    private void BuildCenter(Transform parent)
    {
        centerContent = new GameObject(
            "CenterContent",
            typeof(RectTransform)
        ).GetComponent<RectTransform>();

        centerContent.SetParent(parent, false);
        centerContent.anchorMin = Vector2.zero;
        centerContent.anchorMax = Vector2.one;
        centerContent.offsetMin = Vector2.zero;
        centerContent.offsetMax = Vector2.zero;
    }

    // ============================================================
    // RIGHT TELEMETRY
    // ============================================================

    private void BuildTelemetry(Transform parent)
    {
        TMP_Text heading = CreateText(
            parent,
            "LIVE\nTELEMETRY",
            16f,
            Text,
            TextAlignmentOptions.Left
        );

        SetTopLeft(heading.rectTransform, 16f, 45f, 220f, 65f);

        confidenceText = Metric(parent, "Confidence", 130f);
        errorText = Metric(parent, "Error", 190f);
        fpsText = Metric(parent, "FPS", 250f);
        acquisitionText = Metric(parent, "Acquisition", 310f);
        reacquisitionText = Metric(parent, "Re-acq", 370f);
        lockText = Metric(parent, "Lock", 430f);
        rmseText = Metric(parent, "RMSE", 490f);
        maxErrorText = Metric(parent, "Max Error", 550f);
        inferenceText = Metric(parent, "Inference", 610f);
        modeText = Metric(parent, "Mode", 670f);
        disturbanceText = Metric(parent, "Disturbance", 730f);

        statusText = CreateText(
            parent,
            "● ACQUIRING",
            13f,
            Warning,
            TextAlignmentOptions.Left
        );

        SetTopLeft(statusText.rectTransform, 16f, 90f, 250f, 25f);
    }

    private TMP_Text Metric(
        Transform parent,
        string label,
        float y
    )
    {
        TMP_Text text = CreateText(
            parent,
            label + "\n--",
            13f,
            Text,
            TextAlignmentOptions.Left
        );

        SetTopLeft(
            text.rectTransform,
            16f,
            y,
            280f,
            48f
        );

        return text;
    }

    // ============================================================
    // FOOTER
    // ============================================================

    private void BuildFooter(Transform parent)
    {
        GameObject footer = CreatePanel(
            parent,
            "Footer",
            Background,
            new Vector2(0f, 0.015f),
            new Vector2(1f, 0.075f),
            new Vector2(18f, 0f),
            new Vector2(-18f, 0f),
            true
        );

        string[] labels =
        {
            "RUN",
            "SCENARIO",
            "DISTURBANCES",
            "PERFORMANCE",
            "VIDEO",
            "LOGS"
        };

        Page[] pages =
        {
            Page.Run,
            Page.Scenario,
            Page.Disturbances,
            Page.Performance,
            Page.Video,
            Page.Logs
        };

        float width = 1f / labels.Length;

        for (int i = 0; i < labels.Length; i++)
        {
            int index = i;
            Button button = CreateFooterButton(
                footer.transform,
                labels[index],
                index * width,
                (index + 1) * width,
                () => ShowPage(pages[index])
            );

            if (index > 0)
            {
                GameObject separator = CreatePanel(
                    footer.transform,
                    "Separator_" + index,
                    Border,
                    new Vector2(index * width, 0.15f),
                    new Vector2(index * width, 0.85f),
                    Vector2.zero,
                    Vector2.zero,
                    false
                );

                separator.GetComponent<Image>().color =
                    new Color(0.75f, 0.78f, 0.80f, 0.7f);
            }
        }
    }

    private Button CreateFooterButton(
        Transform parent,
        string label,
        float minX,
        float maxX,
        UnityAction action
    )
    {
        GameObject go = new GameObject(
            "Nav_" + label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(minX, 0f);
        rect.anchorMax = new Vector2(maxX, 1f);
        rect.offsetMin = new Vector2(1f, 1f);
        rect.offsetMax = new Vector2(-1f, -1f);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.05f, 0.055f, 0.065f, 0.5f);

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(action);

        TMP_Text text = CreateText(
            go.transform,
            label,
            13f,
            Text,
            TextAlignmentOptions.Center
        );

        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        return button;
    }

    // ============================================================
    // PAGE SWITCHING
    // ============================================================

    private void ShowPage(Page page)
    {
        if (centerContent == null)
            return;

        ClearCenterContent();

        switch (page)
        {
            case Page.Run:
                BuildRunPage();
                break;

            case Page.Scenario:
                BuildScenarioPage();
                break;

            case Page.Disturbances:
                BuildDisturbancePage();
                break;

            case Page.Performance:
                BuildPerformancePage();
                break;

            case Page.Video:
                BuildVideoPage();
                break;

            case Page.Logs:
                BuildLogsPage();
                break;
        }
    }

    private void ClearCenterContent()
    {
        for (int i = centerContent.childCount - 1; i >= 0; i--)
        {
            Destroy(centerContent.GetChild(i).gameObject);
        }
    }

    // ============================================================
    // RUN PAGE
    // ============================================================

    private void BuildRunPage()
    {
        TMP_Text title = CreateText(
            centerContent,
            "CAMERA VIEW",
            15f,
            Text,
            TextAlignmentOptions.Center
        );

        AnchorCenter(title.rectTransform, 120f, 35f, 0f, 0f);

        TMP_Text crosshair = CreateText(
            centerContent,
            "+",
            26f,
            Accent,
            TextAlignmentOptions.Center
        );

        AnchorCenter(crosshair.rectTransform, 60f, 60f, 0f, 10f);

        TMP_Text target = CreateText(
            centerContent,
            "Target / Beacon",
            14f,
            Text,
            TextAlignmentOptions.Center
        );

        AnchorCenter(target.rectTransform, 250f, 35f, 0f, -40f);

        TMP_Text note = CreateText(
            centerContent,
            "LIVE COARSE-ALIGNMENT VIEW",
            11f,
            DimText,
            TextAlignmentOptions.Center
        );

        AnchorCenter(note.rectTransform, 300f, 25f, 0f, -95f);
    }

    // ============================================================
    // SCENARIO PAGE
    // ============================================================

    private void BuildScenarioPage()
    {
        CreateCenterTitle("SCENARIO");

        CreateCenterText(
            "TARGET MOTION",
            65f,
            Accent
        );

        string[] modes =
        {
            "STRAIGHT LINE",
            "CIRCULAR",
            "FIGURE-8",
            "RANDOM"
        };

        float x = -210f;

        for (int i = 0; i < modes.Length; i++)
        {
            int index = i;
            Button button = CreateCenterButton(
                modes[index],
                x + i * 140f,
                -20f,
                125f,
                38f
            );

            button.onClick.AddListener(
                () => SetMotion(index)
            );
        }

        CreateCenterText(
            "CAMERA / PAT\n\n" +
            "Default FOV       4° × 3°\n" +
            "Camera update     30 Hz\n" +
            "Pan / Tilt         5–10°/s\n\n" +
            "Acquisition        ≤ 2 s\n" +
            "Tracking error     ≤ 10 px\n" +
            "Target loss        < 5%\n" +
            "Re-acquisition     ≤ 1 s\n" +
            "Processing         ≥ 20 FPS",
            -140f,
            Text
        );
    }

    private void SetMotion(int index)
    {
        if (disturbances == null || disturbances.settings == null)
            return;

        switch (index)
        {
            case 0:
                disturbances.settings.platform.type =
                    PlatformMotionType.Linear;
                break;

            case 1:
                disturbances.settings.platform.type =
                    PlatformMotionType.Circular;
                break;

            case 2:
                disturbances.settings.platform.type =
                    PlatformMotionType.Figure8;
                break;

            case 3:
                disturbances.settings.platform.type =
                    PlatformMotionType.Random;
                break;
        }
    }

    // ============================================================
    // DISTURBANCE PAGE
    // ============================================================

    private void BuildDisturbancePage()
    {
        CreateCenterTitle("DISTURBANCES");

        string text = disturbances == null
            ? "DISTURBANCE MANAGER NOT FOUND"
            : BuildDisturbanceDescription();

        CreateCenterText(text, 65f, Text);

        CreateCenterButton(
            disturbances != null && disturbances.disturbancesEnabled
                ? "DISABLE DISTURBANCES"
                : "ENABLE DISTURBANCES",
            0f,
            -260f,
            220f,
            42f
        ).onClick.AddListener(ToggleDisturbances);

        CreateCenterButton(
            "RESET",
            0f,
            -315f,
            120f,
            42f
        ).onClick.AddListener(ResetDisturbances);
    }

    private string BuildDisturbanceDescription()
    {
        DisturbanceSettings s = disturbances.settings;

        return
            "IMAGE DISTURBANCES\n\n" +
            "Gaussian       " + (s.gaussianEnabled ? "ON" : "OFF") + "\n" +
            "Salt & Pepper  " + (s.saltPepperEnabled ? "ON" : "OFF") + "\n" +
            "Poisson        " + (s.poissonEnabled ? "ON" : "OFF") + "\n" +
            "Camera Jitter  " + (s.jitterEnabled ? s.jitterPx.ToString("F1") + " px" : "OFF") + "\n\n" +
            "ATMOSPHERE\n\n" +
            "Weather        " + s.weather + "\n" +
            "Environment    " + s.environment + "\n" +
            "Altitude       " + disturbances.CurrentAltitudeKm.ToString("F1") + " km\n\n" +
            "PLATFORM\n\n" +
            "Motion         " + s.platform.type;
    }

    private void ToggleDisturbances()
    {
        if (disturbances != null)
            disturbances.disturbancesEnabled =
                !disturbances.disturbancesEnabled;

        ShowPage(Page.Disturbances);
    }

    private void ResetDisturbances()
    {
        if (disturbances != null)
            disturbances.ResetRuntimeState();

        ShowPage(Page.Disturbances);
    }

    // ============================================================
    // PERFORMANCE PAGE
    // ============================================================

    private void BuildPerformancePage()
    {
        CreateCenterTitle("PERFORMANCE");

        CreateCenterText(
            "LIVE PERFORMANCE\n\n" +
            "Centroid error        " + ValueFromTelemetry(errorText, "--") + "\n" +
            "RMSE                  " + ValueFromTelemetry(rmseText, "--") + "\n" +
            "Maximum error         " + ValueFromTelemetry(maxErrorText, "--") + "\n" +
            "Acquisition time      " + ValueFromTelemetry(acquisitionText, "--") + "\n" +
            "Re-acquisition time   " + ValueFromTelemetry(reacquisitionText, "--") + "\n" +
            "Lock retention        " + ValueFromTelemetry(lockText, "--") + "\n" +
            "Processing / FPS      " + ValueFromTelemetry(inferenceText, "--") +
            "",
            70f,
            Text
        );
    }

    // ============================================================
    // VIDEO PAGE
    // ============================================================

    private void BuildVideoPage()
    {
        CreateCenterTitle("VIDEO");

        CreateCenterText(
            "VIDEO BENCHMARK\n\n" +
            "Expected benchmark input: 30 FPS MP4\n\n" +
            "The benchmark should use the same detection →\n" +
            "tracking → PAT pipeline used by the live camera.\n\n" +
            "Required outputs:\n" +
            "• centroiding error\n" +
            "• acquisition time\n" +
            "• re-acquisition time\n" +
            "• lock retention\n" +
            "• processing time / FPS",
            70f,
            Text
        );
    }

    // ============================================================
    // LOG PAGE
    // ============================================================

    private void BuildLogsPage()
    {
        CreateCenterTitle("LOGS");

        string log = GetRecorderLog();

        CreateCenterText(
            string.IsNullOrEmpty(log)
                ? "NO PERFORMANCE LOG DATA AVAILABLE."
                : log,
            70f,
            Text
        );

        CreateCenterButton(
            "EXPORT CSV",
            0f,
            -315f,
            160f,
            42f
        ).onClick.AddListener(ExportLogs);
    }

    private string GetRecorderLog()
    {
        if (performanceRecorder == null)
            return "PERFORMANCE RECORDER NOT FOUND.\n\n" +
                   "The live dashboard remains functional.\n" +
                   "Attach FSOCPerformanceRecorder when logging is required.";

        object history = GetMember(
            performanceRecorder,
            "History"
        );

        if (history == null)
        {
            return "PERFORMANCE RECORDER CONNECTED.\n\n" +
                   "No history property is currently exposed.";
        }

        return history.ToString();
    }

    private void ExportLogs()
    {
        if (performanceRecorder == null)
            return;

        InvokeMethod(
            performanceRecorder,
            "ExportHistoryCsv"
        );

        ShowPage(Page.Logs);
    }

    // ============================================================
    // LIVE TELEMETRY
    // ============================================================

    private void RefreshLiveTelemetry()
    {
        if (tracker == null)
        {
            SetSystemOffline();
            return;
        }

        bool locked = tracker.targetDetected;

        if (headerStatus != null)
        {
            headerStatus.text = locked
                ? "●  TARGET LOCKED"
                : "●  SYSTEM READY";

            headerStatus.color = locked
                ? Good
                : Warning;
        }

        if (statusText != null)
        {
            statusText.text = locked
                ? "● TARGET LOCKED"
                : "● ACQUIRING";

            statusText.color = locked
                ? Good
                : Warning;
        }

        // --------------------------------------------------------
        // PIPELINE
        // --------------------------------------------------------

        if (pipelineText != null)
        {
            pipelineText.text =
                "01  CAMERA\n" +
                "     • " + State(mainCamera != null) + "\n\n" +
                "02  IMAGE INPUT\n" +
                "     • " + State(detector != null) + "\n\n" +
                "03  AI DETECTOR\n" +
                "     • " + State(detector != null) + "\n\n" +
                "04  CENTROID\n" +
                "     • " + State(locked) + "\n\n" +
                "05  TRACKER\n" +
                "     • " + State(tracker != null) + "\n\n" +
                "06  PAT\n" +
                "     • " + State(pat != null) + "\n\n" +
                "07  GIMBAL\n" +
                "     • " + State(pat != null);
        }

        // --------------------------------------------------------
        // CONFIDENCE
        // --------------------------------------------------------

        float confidence = GetFloat(
            detector,
            "LastConfidence",
            -1f
        );

        SetMetric(
            confidenceText,
            "Confidence",
            confidence >= 0f
                ? confidence.ToString("P0")
                : "--"
        );

        // --------------------------------------------------------
        // CENTROID ERROR
        // --------------------------------------------------------

        if (locked)
        {
            float dx =
                (tracker.detection.x - 0.5f) * 640f;

            float dy =
                (tracker.detection.y - 0.5f) * 480f;

            float error =
                new Vector2(dx, dy).magnitude;

            SetMetric(
                errorText,
                "Error",
                error.ToString("F1") + " px"
            );
        }
        else
        {
            SetMetric(errorText, "Error", "--");
        }

        // --------------------------------------------------------
        // PERFORMANCE RECORDER
        // --------------------------------------------------------

        float acquisition = GetFloat(
            performanceRecorder,
            "AcquisitionTime",
            -1f
        );

        float reacquisition = GetFloat(
            performanceRecorder,
            "ReacquisitionTime",
            -1f
        );

        float lockRetention = GetFloat(
            performanceRecorder,
            "LockRetentionPct",
            -1f
        );

        float rmse = GetFloat(
            performanceRecorder,
            "RMSEPx",
            -1f
        );

        float maxError = GetFloat(
            performanceRecorder,
            "MaxErrorPx",
            -1f
        );

        float fps = GetFloat(
            performanceRecorder,
            "AverageFps",
            -1f
        );

        float inference = GetFloat(
            detector,
            "LastInferenceMs",
            -1f
        );

        if (fps < 0f)
            fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

        SetMetric(
            fpsText,
            "FPS",
            fps.ToString("F1")
        );

        SetMetric(
            acquisitionText,
            "Acquisition",
            acquisition >= 0f
                ? acquisition.ToString("F2") + " s"
                : "--"
        );

        SetMetric(
            reacquisitionText,
            "Re-acq",
            reacquisition >= 0f
                ? reacquisition.ToString("F2") + " s"
                : "--"
        );

        SetMetric(
            lockText,
            "Lock",
            lockRetention >= 0f
                ? lockRetention.ToString("F1") + " %"
                : "--"
        );

        SetMetric(
            rmseText,
            "RMSE",
            rmse >= 0f
                ? rmse.ToString("F1") + " px"
                : "--"
        );

        SetMetric(
            maxErrorText,
            "Max Error",
            maxError >= 0f
                ? maxError.ToString("F1") + " px"
                : "--"
        );

        SetMetric(
            inferenceText,
            "Inference",
            inference >= 0f
                ? inference.ToString("F1") + " ms"
                : "--"
        );

        // --------------------------------------------------------
        // PAT MODE
        // --------------------------------------------------------

        string patMode = GetString(
            pat,
            "CurrentModeName",
            ""
        );

        if (string.IsNullOrEmpty(patMode))
            patMode = locked ? "PAT" : "SEARCH";

        SetMetric(
            modeText,
            "Mode",
            patMode
        );

        // --------------------------------------------------------
        // DISTURBANCE
        // --------------------------------------------------------

        string disturbanceState = "--";

        if (disturbances != null)
        {
            disturbanceState = disturbances.disturbancesEnabled
                ? "ACTIVE"
                : "OFF";
        }

        SetMetric(
            disturbanceText,
            "Disturbance",
            disturbanceState
        );
    }

    private void SetSystemOffline()
    {
        if (headerStatus != null)
        {
            headerStatus.text = "●  SYSTEM OFFLINE";
            headerStatus.color = Bad;
        }

        if (statusText != null)
        {
            statusText.text = "● SYSTEM OFFLINE";
            statusText.color = Bad;
        }
    }

    private string State(bool online)
    {
        return online ? "ONLINE" : "OFFLINE";
    }

    private void SetMetric(
        TMP_Text text,
        string label,
        string value
    )
    {
        if (text == null)
            return;

        text.text =
            label + "\n" + value;
    }

    private string ValueFromTelemetry(
        TMP_Text text,
        string fallback
    )
    {
        if (text == null || string.IsNullOrEmpty(text.text))
            return fallback;

        string[] lines = text.text.Split('\n');

        return lines.Length > 1
            ? lines[1]
            : fallback;
    }

    // ============================================================
    // REFLECTION HELPERS
    // ============================================================

    private object GetMember(
        object target,
        string memberName
    )
    {
        if (target == null)
            return null;

        Type type = target.GetType();

        FieldInfo field = type.GetField(
            memberName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic
        );

        if (field != null)
            return field.GetValue(target);

        PropertyInfo property = type.GetProperty(
            memberName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic
        );

        if (property != null && property.CanRead)
            return property.GetValue(target);

        return null;
    }

    private float GetFloat(
        object target,
        string memberName,
        float fallback
    )
    {
        object value = GetMember(target, memberName);

        if (value == null)
            return fallback;

        try
        {
            return Convert.ToSingle(value);
        }
        catch
        {
            return fallback;
        }
    }

    private string GetString(
        object target,
        string memberName,
        string fallback
    )
    {
        object value = GetMember(target, memberName);

        return value == null
            ? fallback
            : value.ToString();
    }

    private void InvokeMethod(
        object target,
        string methodName
    )
    {
        if (target == null)
            return;

        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null
        );

        method?.Invoke(target, null);
    }

    // ============================================================
    // CENTER UI HELPERS
    // ============================================================

    private TMP_Text CreateCenterTitle(string value)
    {
        TMP_Text text = CreateText(
            centerContent,
            value,
            16f,
            Text,
            TextAlignmentOptions.Center
        );

        AnchorCenter(text.rectTransform, 300f, 30f, 0f, 25f);

        return text;
    }

    private TMP_Text CreateCenterText(
        string value,
        float y,
        Color color
    )
    {
        TMP_Text text = CreateText(
            centerContent,
            value,
            13f,
            color,
            TextAlignmentOptions.Left
        );

        AnchorCenter(text.rectTransform, 600f, 330f, 0f, -y);

        return text;
    }

    private Button CreateCenterButton(
        string label,
        float x,
        float y,
        float width,
        float height
    )
    {
        GameObject go = new GameObject(
            "CenterButton_" + label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );

        go.transform.SetParent(centerContent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);

        Image image = go.GetComponent<Image>();
        image.color = new Color(0.08f, 0.09f, 0.10f, 0.9f);

        Button button = go.GetComponent<Button>();

        TMP_Text text = CreateText(
            go.transform,
            label,
            11f,
            Text,
            TextAlignmentOptions.Center
        );

        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        return button;
    }

    // ============================================================
    // GENERAL UI CREATION
    // ============================================================

    private GameObject CreatePanel(
        Transform parent,
        string name,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        bool outline
    )
    {
        GameObject go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Image)
        );

        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;

        if (outline)
        {
            Outline border = go.AddComponent<Outline>();
            border.effectColor = Border;
            border.effectDistance = new Vector2(1f, 1f);
            border.useGraphicAlpha = true;
        }

        return go;
    }

    private TMP_Text CreateText(
        Transform parent,
        string value,
        float size,
        Color color,
        TextAlignmentOptions alignment
    )
    {
        GameObject go = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(TextMeshProUGUI)
        );

        go.transform.SetParent(parent, false);

        TextMeshProUGUI text =
            go.GetComponent<TextMeshProUGUI>();

        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    // ============================================================
    // RECT TRANSFORM HELPERS
    // ============================================================

    private void SetTopLeft(
        RectTransform rect,
        float left,
        float top,
        float width,
        float height
    )
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void SetTopRight(
        RectTransform rect,
        float right,
        float top,
        float width,
        float height
    )
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void AnchorCenter(
        RectTransform rect,
        float width,
        float height,
        float x,
        float y
    )
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
