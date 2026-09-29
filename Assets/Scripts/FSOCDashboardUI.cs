using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// FSOC dashboard with button-driven control popups.
///
/// The 3D camera/scene remains visible behind the UI. The left side contains
/// compact control buttons. Clicking a button opens a modal control window.
/// Closing the window returns to the clean dashboard.
///
/// Existing PAT, Tracker, YOLO and DisturbanceManager systems are not replaced.
/// The UI only reads their telemetry and calls the existing public disturbance
/// controls/presets.
/// </summary>
public class FSOCDashboardUI : MonoBehaviour
{
    private enum Popup
    {
        None,
        Pipeline,
        Scenario,
        Disturbances,
        Performance,
        Video,
        Logs
    }

    // ------------------------------------------------------------
    // SYSTEM REFERENCES
    // ------------------------------------------------------------

    private Tracker tracker;
    private YoloDetection detector;
    private PATController pat;
    private DisturbanceManager disturbances;
    private Camera mainCamera;
    private MonoBehaviour performanceRecorder;

    // ------------------------------------------------------------
    // UI ROOTS
    // ------------------------------------------------------------

    private Canvas canvas;
    private RectTransform popupRoot;
    private RectTransform popupContent;
    private TMP_Text popupTitle;

    // Central disturbed camera feed. This is the exact Texture2D that
    // YoloDetection passes to the detector after DisturbanceManager processing.
    private RawImage disturbedCameraFeed;
    private TMP_Text cameraFeedLabel;

    // ------------------------------------------------------------
    // LIVE TELEMETRY
    // ------------------------------------------------------------

    private TMP_Text headerStatus;
    private TMP_Text pipelineSummary;
    private TMP_Text statusText;
    private TMP_Text confidenceText;
    private TMP_Text errorText;
    private TMP_Text fpsText;
    private TMP_Text acquisitionText;
    private TMP_Text reacquisitionText;
    private TMP_Text lockText;
    private TMP_Text modeText;
    private TMP_Text disturbanceText;

    // ------------------------------------------------------------
    // COLORS
    // ------------------------------------------------------------

    private static readonly Color Transparent = new Color(0f, 0f, 0f, 0f);
    private static readonly Color Background = new Color(0.025f, 0.030f, 0.035f, 0.96f);
    private static readonly Color Panel = new Color(0.045f, 0.055f, 0.065f, 0.90f);
    private static readonly Color PopupColor = new Color(0.035f, 0.040f, 0.048f, 0.98f);
    private static readonly Color Button = new Color(0.075f, 0.085f, 0.100f, 0.96f);
    private static readonly Color ButtonHover = new Color(0.11f, 0.15f, 0.18f, 1f);
    private static readonly Color Border = new Color(0.55f, 0.62f, 0.68f, 0.85f);
    private static readonly Color Text = new Color(0.92f, 0.94f, 0.96f, 1f);
    private static readonly Color DimText = new Color(0.60f, 0.66f, 0.72f, 1f);
    private static readonly Color Accent = new Color(0.25f, 0.72f, 1f, 1f);
    private static readonly Color Good = new Color(0.25f, 1f, 0.55f, 1f);
    private static readonly Color Warning = new Color(1f, 0.72f, 0.22f, 1f);
    private static readonly Color Bad = new Color(1f, 0.30f, 0.30f, 1f);

    // ------------------------------------------------------------
    // UNITY
    // ------------------------------------------------------------

    private void Awake()
    {
        FindSystems();
        ConfigureCameraForSpaceView();
        BuildDashboard();
        ClosePopup();
    }

    private void Update()
    {
        RefreshCameraFeed();
        RefreshLiveTelemetry();

        if (popupRoot != null && popupRoot.gameObject.activeSelf)
            RefreshPopupLiveText();
    }

    // ------------------------------------------------------------
    // FIND SYSTEMS
    // ------------------------------------------------------------

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
            if (component != null && component.GetType().Name == typeName)
                return component;
        }

        return null;
    }

    // ------------------------------------------------------------
    // DASHBOARD BUILD
    // ------------------------------------------------------------

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

        // Completely transparent root: the actual Unity scene remains visible.
        GameObject root = CreatePanel(
            canvas.transform,
            "DashboardRoot",
            Transparent,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero,
            false
        );

        BuildHeader(root.transform);

        // IMPORTANT:
        // The central panel displays the disturbed Texture2D produced by
        // YoloDetection, rather than the clean 3D camera render. This makes
        // the image shown to the user identical to the image seen by YOLO.
        BuildCameraFeed(root.transform);

        BuildLeftControls(root.transform);
        BuildRightTelemetry(root.transform);
        BuildFooter(root.transform);
        BuildPopup(root.transform);
    }

    // ------------------------------------------------------------
    // HEADER
    // ------------------------------------------------------------

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

        TMP_Text title = CreateText(header.transform,
            "FSOC COARSE ALIGNMENT SYSTEM", 25f, Text,
            TextAlignmentOptions.Left);
        SetTopLeft(title.rectTransform, 18f, 13f, 720f, 34f);

        TMP_Text subtitle = CreateText(header.transform,
            "AI CAMERA TRACKING / COARSE PAT", 13f, Accent,
            TextAlignmentOptions.Left);
        SetTopLeft(subtitle.rectTransform, 18f, 45f, 600f, 24f);

        headerStatus = CreateText(header.transform,
            "● SYSTEM READY", 15f, Good,
            TextAlignmentOptions.Right);
        SetTopRight(headerStatus.rectTransform, 18f, 17f, 300f, 28f);
    }

    // ------------------------------------------------------------
    // CENTRAL DISTURBED CAMERA FEED
    // ------------------------------------------------------------

    private void BuildCameraFeed(Transform parent)
    {
        // The feed occupies only the center of the dashboard. Because it is
        // created before the side panels, the left/right UI remains on top.
        GameObject feed = new GameObject(
            "DisturbedCameraFeed",
            typeof(RectTransform),
            typeof(RawImage),
            typeof(AspectRatioFitter)
        );

        feed.transform.SetParent(parent, false);

        RectTransform rect = feed.GetComponent<RectTransform>();

        rect.anchorMin = new Vector2(0.205f, 0.085f);
        rect.anchorMax = new Vector2(0.805f, 0.875f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        disturbedCameraFeed = feed.GetComponent<RawImage>();

        RawImage image = disturbedCameraFeed;

        // Keep the real camera/YOLO image visible while there is no processed
        // frame yet. Once YoloDetection produces a frame, this is replaced
        // immediately by the disturbed feed.
        image.color = Color.white;
        image.raycastTarget = false;
        image.texture = null;

        // YoloDetection currently captures a square 640x640 image.
        // Fit it inside the central camera area without stretching it.
        AspectRatioFitter fitter = feed.GetComponent<AspectRatioFitter>();
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = 1f;

        cameraFeedLabel = CreateText(
            feed.transform,
            "WAITING FOR DISTURBED CAMERA FRAME...",
            12f,
            DimText,
            TextAlignmentOptions.Center
        );

        cameraFeedLabel.rectTransform.anchorMin = new Vector2(0.5f, 1f);
        cameraFeedLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        cameraFeedLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
        cameraFeedLabel.rectTransform.anchoredPosition = new Vector2(0f, -10f);
        cameraFeedLabel.rectTransform.sizeDelta = new Vector2(700f, 28f);

        // A thin frame around the camera feed.
        Outline outline = feed.AddComponent<Outline>();
        outline.effectColor = new Color(Border.r, Border.g, Border.b, 0.65f);
        outline.effectDistance = new Vector2(1f, 1f);
        outline.useGraphicAlpha = true;
    }

    private void RefreshCameraFeed()
    {
        if (disturbedCameraFeed == null)
            return;

        if (detector == null)
        {
            if (cameraFeedLabel != null)
                cameraFeedLabel.text = "YOLO DETECTOR NOT FOUND";

            disturbedCameraFeed.texture = null;
            return;
        }

        Texture2D frame = detector.DisturbedCameraFrame;

        if (frame == null)
        {
            if (cameraFeedLabel != null)
                cameraFeedLabel.text = "WAITING FOR DISTURBED CAMERA FRAME...";

            disturbedCameraFeed.texture = null;
            return;
        }

        if (disturbedCameraFeed.texture != frame)
            disturbedCameraFeed.texture = frame;

        if (cameraFeedLabel != null)
            cameraFeedLabel.text =
                "DISTURBED CAMERA FEED  /  YOLO INPUT";
    }

    // ------------------------------------------------------------
    // LEFT CONTROL BUTTONS
    // ------------------------------------------------------------

    private void BuildLeftControls(Transform parent)
    {
        GameObject left = CreatePanel(
            parent,
            "ControlPanel",
            Panel,
            new Vector2(0f, 0.085f),
            new Vector2(0.205f, 0.875f),
            new Vector2(18f, 0f),
            new Vector2(-6f, 0f),
            true
        );

        TMP_Text title = CreateText(left.transform,
            "SYSTEM\nCONTROLS", 17f, Text,
            TextAlignmentOptions.Left);
        SetTopLeft(title.rectTransform, 16f, 28f, 250f, 58f);

        TMP_Text hint = CreateText(left.transform,
            "Select a control panel", 11f, DimText,
            TextAlignmentOptions.Left);
        SetTopLeft(hint.rectTransform, 16f, 82f, 250f, 22f);

        CreateMenuButton(left.transform, "SYSTEM PIPELINE", 120f,
            () => OpenPopup(Popup.Pipeline));

        CreateMenuButton(left.transform, "SCENARIO", 178f,
            () => OpenPopup(Popup.Scenario));

        CreateMenuButton(left.transform, "DISTURBANCE CONTROL", 236f,
            () => OpenPopup(Popup.Disturbances));

        CreateMenuButton(left.transform, "PERFORMANCE", 294f,
            () => OpenPopup(Popup.Performance));

        CreateMenuButton(left.transform, "VIDEO / BENCHMARK", 352f,
            () => OpenPopup(Popup.Video));

        CreateMenuButton(left.transform, "LOGS / EXPORT", 410f,
            () => OpenPopup(Popup.Logs));

        // Compact always-visible pipeline state.
        pipelineSummary = CreateText(left.transform, "", 11f, DimText,
            TextAlignmentOptions.Left);
        SetTopLeft(pipelineSummary.rectTransform, 16f, 485f, 280f, 210f);
    }

    private Button CreateMenuButton(Transform parent, string label, float top, UnityAction action)
    {
        // Center the buttons inside the left panel. The previous version used
        // mismatched anchorMin/anchorMax values, which pushed the buttons toward
        // the panel edge and caused multi-line labels to look cramped.
        return CreateButton(
            parent,
            "Menu_" + label,
            label,
            13f,
            Text,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -top),
            new Vector2(0.5f, 1f),
            new Vector2(300f, 50f),
            action
        );
    }

    // ------------------------------------------------------------
    // RIGHT TELEMETRY
    // ------------------------------------------------------------

    private void BuildRightTelemetry(Transform parent)
    {
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

        TMP_Text heading = CreateText(right.transform,
            "LIVE\nTELEMETRY", 17f, Text,
            TextAlignmentOptions.Left);
        SetTopLeft(heading.rectTransform, 16f, 28f, 230f, 58f);

        statusText = CreateText(right.transform,
            "● ACQUIRING", 13f, Warning,
            TextAlignmentOptions.Left);
        SetTopLeft(statusText.rectTransform, 16f, 88f, 270f, 25f);

        confidenceText = Metric(right.transform, "Confidence", 130f);
        errorText = Metric(right.transform, "Tracking Error", 190f);
        fpsText = Metric(right.transform, "FPS", 250f);
        acquisitionText = Metric(right.transform, "Acquisition", 310f);
        reacquisitionText = Metric(right.transform, "Re-acquisition", 370f);
        lockText = Metric(right.transform, "Lock", 430f);
        modeText = Metric(right.transform, "PAT Mode", 490f);
        disturbanceText = Metric(right.transform, "Disturbance", 550f);
    }

    private TMP_Text Metric(Transform parent, string label, float y)
    {
        TMP_Text text = CreateText(parent, label + "\n--", 13f, Text,
            TextAlignmentOptions.Left);
        SetTopLeft(text.rectTransform, 16f, y, 270f, 48f);
        return text;
    }

    // ------------------------------------------------------------
    // FOOTER
    // ------------------------------------------------------------

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

        TMP_Text text = CreateText(footer.transform,
            "SELECT A CONTROL PANEL FROM THE LEFT   |   DISTURBED CAMERA FEED = YOLO INPUT",
            11f, DimText, TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
    }

    // ------------------------------------------------------------
    // POPUP FRAME
    // ------------------------------------------------------------

    private void BuildPopup(Transform parent)
    {
        // Dim layer only exists while a popup is open.
        GameObject dim = CreatePanel(
            parent,
            "PopupDim",
            new Color(0f, 0f, 0f, 0.55f),
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero,
            false
        );

        dim.transform.SetAsLastSibling();

        popupRoot = CreatePanel(
            dim.transform,
            "PopupWindow",
            PopupColor,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(-470f, -390f),
            new Vector2(470f, 390f),
            true
        ).GetComponent<RectTransform>();

        popupRoot.SetAsLastSibling();

        GameObject titleBar = CreatePanel(
            popupRoot,
            "PopupTitleBar",
            Background,
            new Vector2(0f, 0.91f),
            new Vector2(1f, 1f),
            Vector2.zero,
            Vector2.zero,
            false
        );

        popupTitle = CreateText(titleBar.transform, "CONTROL", 17f, Text,
            TextAlignmentOptions.Left);
        SetTopLeft(popupTitle.rectTransform, 18f, 12f, 620f, 30f);

        Button close = CreateButton(titleBar.transform, "Close", "X", 14f,
            Text, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(-28f, 0f), new Vector2(1f, 0.5f),
            new Vector2(42f, 34f), ClosePopup);
        close.GetComponent<Image>().color = new Color(0.18f, 0.06f, 0.07f, 1f);

        // Scroll view.
        GameObject scroll = new GameObject("PopupScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
        scroll.transform.SetParent(popupRoot, false);

        RectTransform scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0f);
        scrollRect.anchorMax = new Vector2(1f, 0.91f);
        scrollRect.offsetMin = new Vector2(12f, 12f);
        scrollRect.offsetMax = new Vector2(-12f, -6f);

        Image scrollImage = scroll.GetComponent<Image>();
        scrollImage.color = new Color(0f, 0f, 0f, 0.10f);
        scrollImage.raycastTarget = true;

        Mask mask = scroll.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        popupContent = new GameObject("PopupContent", typeof(RectTransform)).GetComponent<RectTransform>();
        popupContent.SetParent(scroll.transform, false);
        popupContent.anchorMin = new Vector2(0f, 1f);
        popupContent.anchorMax = new Vector2(1f, 1f);
        popupContent.pivot = new Vector2(0.5f, 1f);
        popupContent.anchoredPosition = Vector2.zero;
        popupContent.sizeDelta = new Vector2(0f, 1650f);

        ScrollRect scrollController = scroll.GetComponent<ScrollRect>();
        scrollController.content = popupContent;
        scrollController.horizontal = false;
        scrollController.vertical = true;
        scrollController.movementType = ScrollRect.MovementType.Clamped;
        scrollController.scrollSensitivity = 35f;

        // Dim is the first child and popup is last; popup remains clickable.
        dim.SetActive(false);
    }

    private void OpenPopup(Popup popup)
    {
        if (popupRoot == null)
            return;

        popupRoot.transform.parent.gameObject.SetActive(true);
        ClearPopupContent();

        switch (popup)
        {
            case Popup.Pipeline:
                popupTitle.text = "SYSTEM PIPELINE";
                BuildPipelinePopup();
                break;
            case Popup.Scenario:
                popupTitle.text = "SCENARIO CONTROL";
                BuildScenarioPopup();
                break;
            case Popup.Disturbances:
                popupTitle.text = "DISTURBANCE CONTROL";
                BuildDisturbancePopup();
                break;
            case Popup.Performance:
                popupTitle.text = "PERFORMANCE";
                BuildPerformancePopup();
                break;
            case Popup.Video:
                popupTitle.text = "VIDEO / BENCHMARK";
                BuildVideoPopup();
                break;
            case Popup.Logs:
                popupTitle.text = "LOGS / EXPORT";
                BuildLogsPopup();
                break;
        }

        LayoutPopupContent();
    }

    private void ClosePopup()
    {
        if (popupRoot == null)
            return;

        popupRoot.transform.parent.gameObject.SetActive(false);
    }

    private void ClearPopupContent()
    {
        for (int i = popupContent.childCount - 1; i >= 0; i--)
            Destroy(popupContent.GetChild(i).gameObject);
    }

    private void LayoutPopupContent()
    {
        Canvas.ForceUpdateCanvases();
    }

    // ------------------------------------------------------------
    // PIPELINE POPUP
    // ------------------------------------------------------------

    private void BuildPipelinePopup()
    {
        PopupHeading("LIVE SYSTEM PIPELINE", 20f);

        PopupRow("01  CAMERA", mainCamera != null, 70f);
        PopupRow("02  IMAGE INPUT", detector != null, 125f);
        PopupRow("03  AI DETECTOR", detector != null, 180f);
        PopupRow("04  CENTROID", tracker != null && tracker.targetDetected, 235f);
        PopupRow("05  TRACKER", tracker != null, 290f);
        PopupRow("06  PAT", pat != null, 345f);
        PopupRow("07  GIMBAL", pat != null, 400f);

        CreatePopupText("The pipeline status is read from the existing Unity components.\n\n"
            + "TARGET DETECTED and TARGET LOCKED are intentionally different states.\n"
            + "PAT lock is based on the PATController pixel tolerance.", 455f, 520f, DimText);
    }

    private void PopupRow(string label, bool online, float y)
    {
        TMP_Text t = CreatePopupText(
            label + "\n     " + (online ? "● ONLINE" : "○ OFFLINE"),
            y, 46f, online ? Good : Bad);
    }

    // ------------------------------------------------------------
    // SCENARIO POPUP
    // ------------------------------------------------------------

    private void BuildScenarioPopup()
    {
        PopupHeading("PLATFORM MOTION", 20f);
        CreatePopupText("Select the motion pattern used by the existing DisturbanceManager.", 55f, 35f, DimText);

        string[] names = { "NONE", "LINEAR", "CIRCULAR", "RANDOM", "SPIRAL", "FIGURE-8" };
        PlatformMotionType[] types =
        {
            PlatformMotionType.None,
            PlatformMotionType.Linear,
            PlatformMotionType.Circular,
            PlatformMotionType.Random,
            PlatformMotionType.Spiral,
            PlatformMotionType.Figure8
        };

        for (int i = 0; i < names.Length; i++)
        {
            int index = i;
            CreatePopupButton(names[i], 110f + (i / 3) * 65f,
                150f, 44f, () => SetMotion(types[index]),
                -250f + (i % 3) * 250f);
        }

        CreatePopupText(
            "PAT CONTROL\n\n" +
            "Rotation speed: " + (pat != null ? pat.rotationSpeed.ToString("F1") : "--") + " °/s\n" +
            "Lock tolerance: " + (pat != null ? pat.lockTolerancePixels.ToString("F1") : "--") + " px\n" +
            "Re-acquisition hold: " + (pat != null ? pat.reacquisitionHoldTime.ToString("F2") : "--") + " s\n" +
            "Horizontal limit: " + (pat != null ? pat.horizontalLimit.ToString("F0") : "--") + "°\n" +
            "Vertical limit: " + (pat != null ? pat.verticalLimit.ToString("F0") : "--") + "°",
            340f, 250f, Text);
    }

    private void SetMotion(PlatformMotionType type)
    {
        if (disturbances == null || disturbances.settings == null)
            return;

        disturbances.settings.platform.type = type;
        disturbances.ResetRuntimeState();
        OpenPopup(Popup.Scenario);
    }

    // ------------------------------------------------------------
    // DISTURBANCE POPUP
    // ------------------------------------------------------------

    private void BuildDisturbancePopup()
    {
        if (disturbances == null || disturbances.settings == null)
        {
            CreatePopupText("DISTURBANCE MANAGER NOT FOUND", 70f, 40f, Bad);
            return;
        }

        DisturbanceSettings s = disturbances.settings;

        // Top controls
        CreatePopupButton(
            disturbances.disturbancesEnabled ? "DISTURBANCES: ON" : "DISTURBANCES: OFF",
            62f, 260f, 48f, ToggleDisturbances, -145f);

        CreatePopupButton(
            "RETURN TO ORIGINAL",
            62f, 260f, 48f, ResetToOriginal, 145f);

        CreatePopupText(
            "Choose one preset or adjust individual disturbance sources below.",
            125f, 34f, DimText);

        // Preset grid: large, evenly spaced, no overlapping explanatory text.
        CreatePopupText("PRESETS", 150f, 30f, Accent);

        DisturbancePresetId[] presets =
        {
            DisturbancePresetId.Baseline,
            DisturbancePresetId.Mild,
            DisturbancePresetId.Moderate,
            DisturbancePresetId.Severe,
            DisturbancePresetId.VibrationStress,
            DisturbancePresetId.HazyDay,
            DisturbancePresetId.DenseFog,
            DisturbancePresetId.HeavyRain,
            DisturbancePresetId.NightLowLight,
            DisturbancePresetId.FullStorm
        };

        string[] labels =
        {
            "Baseline", "Mild", "Moderate", "Severe",
            "Vibration Stress", "Hazy Day", "Dense Fog", "Heavy Rain",
            "Night / Low Light", "Full Storm"
        };

        const float firstY = 190f;
        const float rowStep = 60f;
        const float leftX = -145f;
        const float rightX = 145f;

        for (int i = 0; i < presets.Length; i++)
        {
            int index = i;
            int row = i / 2;
            int col = i % 2;

            CreatePopupButton(
                labels[i],
                firstY + row * rowStep,
                290f,
                50f,
                () => ApplyPreset(presets[index]),
                col == 0 ? leftX : rightX);
        }

        // Sensor noise
        const float sensorY = 555f;
        CreatePopupText("SENSOR NOISE", sensorY, 30f, Accent);

        CreateToggleButton(
            "Gaussian", s.gaussianEnabled, sensorY + 45f,
            () =>
            {
                s.gaussianEnabled = !s.gaussianEnabled;
                disturbances.ResetRuntimeState();
                OpenPopup(Popup.Disturbances);
            });

        CreateToggleButton(
            "Salt & Pepper", s.saltPepperEnabled, sensorY + 95f,
            () =>
            {
                s.saltPepperEnabled = !s.saltPepperEnabled;
                disturbances.ResetRuntimeState();
                OpenPopup(Popup.Disturbances);
            });

        CreateToggleButton(
            "Poisson / Shot", s.poissonEnabled, sensorY + 145f,
            () =>
            {
                s.poissonEnabled = !s.poissonEnabled;
                disturbances.ResetRuntimeState();
                OpenPopup(Popup.Disturbances);
            });

        CreateToggleButton(
            "Camera Jitter", s.jitterEnabled, sensorY + 195f,
            () =>
            {
                s.jitterEnabled = !s.jitterEnabled;
                disturbances.ResetRuntimeState();
                OpenPopup(Popup.Disturbances);
            });

        // Weather
        const float weatherY = 790f;
        CreatePopupText("WEATHER", weatherY, 30f, Accent);

        CreateWeatherButton("Haze", WeatherMode.Haze, weatherY + 45f);
        CreateWeatherButton("Fog", WeatherMode.Fog, weatherY + 95f);
        CreateWeatherButton("Rain", WeatherMode.Rain, weatherY + 145f);
        CreateWeatherButton("Low Light", WeatherMode.LowLight, weatherY + 195f);

        CreatePopupButton(
            "CLEAR WEATHER",
            weatherY + 250f,
            300f,
            48f,
            () =>
            {
                s.weather = WeatherMode.Clear;
                disturbances.ResetRuntimeState();
                OpenPopup(Popup.Disturbances);
            });

        CreatePopupText(
            "CURRENT SETTINGS\n" +
            "Environment: " + s.environment + "\n" +
            "Altitude: " + disturbances.CurrentAltitudeKm.ToString("F1") + " km\n" +
            "Gaussian: " + (s.gaussianEnabled ? s.gaussianSigma.ToString("F3") : "OFF") + "\n" +
            "Salt & Pepper: " + (s.saltPepperEnabled ? s.saltPepperAmount.ToString("F3") : "OFF") + "\n" +
            "Poisson: " + (s.poissonEnabled ? s.photonFullWell.ToString("F0") : "OFF") + "\n" +
            "Jitter: " + (s.jitterEnabled ? s.jitterPx.ToString("F1") + " px" : "OFF") + "\n" +
            "Motion: " + s.platform.type + "\n" +
            "Weather: " + s.weather,
            1090f, 320f, Text);
    }

    private void ApplyPreset(DisturbancePresetId id)
    {
        if (disturbances == null)
            return;

        // Selecting a preset replaces the previous disturbance settings.
        disturbances.ApplyPreset(id, true);

        // A non-baseline preset should immediately be visible in the feed.
        if (id != DisturbancePresetId.Baseline)
            disturbances.disturbancesEnabled = true;

        // Baseline is a clean/default disturbance configuration, but keep
        // the master switch on so the system can be returned to the exact
        // baseline processing path without another click.
        OpenPopup(Popup.Disturbances);
    }

    private void ResetToOriginal()
    {
        if (disturbances == null)
            return;

        // Restore the original clean state: baseline settings + no active
        // image disturbance. The physical scene, PAT and laser are untouched.
        disturbances.ApplyPreset(DisturbancePresetId.Baseline, true);
        disturbances.disturbancesEnabled = false;
        disturbances.ResetRuntimeState();

        OpenPopup(Popup.Disturbances);
    }

    private void ToggleDisturbances()
    {
        if (disturbances == null)
            return;

        disturbances.disturbancesEnabled = !disturbances.disturbancesEnabled;
        OpenPopup(Popup.Disturbances);
    }

    private void CreateToggleButton(string label, bool active, float y, UnityAction action)
    {
        CreatePopupButton((active ? "● " : "○ ") + label, y, 320f, 48f, action);
    }

    private void CreateWeatherButton(string label, WeatherMode mode, float y)
    {
        bool active = disturbances != null &&
                      disturbances.settings != null &&
                      (disturbances.settings.weather & mode) != 0;

        CreateToggleButton(label, active, y, () =>
        {
            if (disturbances == null || disturbances.settings == null)
                return;

            WeatherMode current = disturbances.settings.weather;
            if ((current & mode) != 0)
                current &= ~mode;
            else
                current |= mode;

            disturbances.settings.weather = current;
            disturbances.ResetRuntimeState();
            OpenPopup(Popup.Disturbances);
        });
    }

    // ------------------------------------------------------------
    // PERFORMANCE POPUP
    // ------------------------------------------------------------

    private void BuildPerformancePopup()
    {
        PopupHeading("LIVE PERFORMANCE", 20f);

        CreatePopupText(
            "Tracking error\n" + GetPatError() + "\n\n" +
            "Lock status\n" + GetLockState() + "\n\n" +
            "FPS\n" + GetFps() + "\n\n" +
            "Inference\n" + GetInference() + "\n\n" +
            "Acquisition\n" + GetRecorderValue("AcquisitionTime", "--") + "\n\n" +
            "Re-acquisition\n" + GetRecorderValue("ReacquisitionTime", "--") + "\n\n" +
            "RMSE\n" + GetRecorderValue("RMSEPx", "--") + "\n\n" +
            "Maximum error\n" + GetRecorderValue("MaxErrorPx", "--") + "\n\n" +
            "Lock retention\n" + GetRecorderValue("LockRetentionPct", "--"),
            65f, 300f, Text);
    }

    // ------------------------------------------------------------
    // VIDEO POPUP
    // ------------------------------------------------------------

    private void BuildVideoPopup()
    {
        PopupHeading("VIDEO / BENCHMARK", 20f);

        CreatePopupText(
            "BENCHMARK INPUT\n\n" +
            "Expected input: 30 FPS MP4\n\n" +
            "The benchmark should use the same detection → tracking → PAT pipeline as the live camera.\n\n" +
            "Required outputs\n" +
            "• centroid / tracking error\n" +
            "• acquisition time\n" +
            "• re-acquisition time\n" +
            "• lock retention\n" +
            "• processing time / FPS\n\n" +
            "This dashboard does not invent a video file path. Connect the existing benchmark/video system when available.",
            65f, 300f, Text);
    }

    // ------------------------------------------------------------
    // LOGS POPUP
    // ------------------------------------------------------------

    private void BuildLogsPopup()
    {
        PopupHeading("LOGS / EXPORT", 20f);

        string log = GetRecorderLog();
        CreatePopupText(string.IsNullOrEmpty(log) ? "NO LOG DATA AVAILABLE." : log,
            65f, 300f, Text);

        CreatePopupButton("EXPORT CSV", 620f, 180f, 42f, ExportLogs);
    }

    private string GetRecorderLog()
    {
        if (performanceRecorder == null)
            return "PERFORMANCE RECORDER NOT FOUND.\n\nLive telemetry remains available.";

        object history = GetMember(performanceRecorder, "History");
        return history == null
            ? "PERFORMANCE RECORDER CONNECTED.\n\nNo History property is currently exposed."
            : history.ToString();
    }

    private void ExportLogs()
    {
        if (performanceRecorder == null)
            return;

        InvokeMethod(performanceRecorder, "ExportHistoryCsv");
        OpenPopup(Popup.Logs);
    }

    // ------------------------------------------------------------
    // POPUP HELPERS
    // ------------------------------------------------------------

    private void PopupHeading(string text, float y)
    {
        CreatePopupText(text, y, 500f, Accent);
    }

    private TMP_Text CreatePopupText(string value, float y, float height, Color color)
    {
        GameObject go = new GameObject("PopupText", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(popupContent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -y);
        rect.sizeDelta = new Vector2(760f, height);

        TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
        t.text = value;
        t.fontSize = 13f;
        t.color = color;
        t.alignment = TextAlignmentOptions.Left;
        t.enableWordWrapping = true;
        t.overflowMode = TextOverflowModes.Overflow;
        t.raycastTarget = false;

        return t;
    }

    private Button CreatePopupButton(string label, float y, float width, float height, UnityAction action)
    {
        return CreatePopupButton(label, y, width, height, action, 0f);
    }

    private Button CreatePopupButton(string label, float y, float width, float height, UnityAction action, float x)
    {
        GameObject go = new GameObject(
            "PopupButton_" + label,
            typeof(RectTransform),
            typeof(Image),
            typeof(Button)
        );
        go.transform.SetParent(popupContent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);

        Image image = go.GetComponent<Image>();
        image.color = Button;
        image.raycastTarget = true;

        Button b = go.GetComponent<Button>();
        b.onClick.AddListener(action);

        ColorBlock colors = b.colors;
        colors.normalColor = Button;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = Accent;
        colors.selectedColor = ButtonHover;
        b.colors = colors;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(Border.r, Border.g, Border.b, 0.5f);
        outline.effectDistance = new Vector2(1f, 1f);

        TMP_Text t = CreateText(go.transform, label, 11f, Text,
            TextAlignmentOptions.Center);
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = new Vector2(4f, 2f);
        t.rectTransform.offsetMax = new Vector2(-4f, -2f);

        return b;
    }

    // Used only to spread the preset grid horizontally after creation.
    private void SetPopupButtonX(string objectName, float x)
    {
        Transform found = popupContent.Find(objectName);
        if (found == null)
            return;

        RectTransform rect = found.GetComponent<RectTransform>();
        if (rect != null)
            rect.anchoredPosition = new Vector2(x, rect.anchoredPosition.y);
    }

    // ------------------------------------------------------------
    // LIVE TELEMETRY
    // ------------------------------------------------------------

    private void RefreshLiveTelemetry()
    {
        if (tracker == null)
        {
            SetSystemOffline();
            return;
        }

        bool detected = tracker.targetDetected;
        bool locked = pat != null && pat.IsTargetLocked;

        string status;
        Color statusColor;

        if (locked)
        {
            status = "● TARGET LOCKED";
            statusColor = Good;
        }
        else if (detected)
        {
            status = "● TARGET DETECTED / TRACKING";
            statusColor = Warning;
        }
        else if (pat != null && pat.CurrentModeName == "Search")
        {
            status = "● SEARCHING";
            statusColor = Warning;
        }
        else
        {
            status = "● SYSTEM READY";
            statusColor = Warning;
        }

        if (headerStatus != null)
        {
            headerStatus.text = status;
            headerStatus.color = statusColor;
        }

        if (statusText != null)
        {
            statusText.text = status;
            statusText.color = statusColor;
        }

        float confidence = GetFloat(detector, "LastConfidence", -1f);
        SetMetric(confidenceText, "Confidence", confidence >= 0f ? confidence.ToString("P0") : "--");

        float error = -1f;
        if (pat != null)
        {
            error = pat.CurrentAlignmentErrorPixels;
        }
        else if (detected)
        {
            float dx = (tracker.detection.x - 0.5f) * 640f;
            float dy = (tracker.detection.y - 0.5f) * 480f;
            error = new Vector2(dx, dy).magnitude;
        }

        SetMetric(errorText, "Tracking Error", error >= 0f ? error.ToString("F1") + " px" : "--");

        float fps = GetFloat(performanceRecorder, "AverageFps", -1f);
        if (fps < 0f)
            fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        SetMetric(fpsText, "FPS", fps.ToString("F1"));

        float acquisition = GetFloat(performanceRecorder, "AcquisitionTime", -1f);
        float reacquisition = GetFloat(performanceRecorder, "ReacquisitionTime", -1f);
        float lockRetention = GetFloat(performanceRecorder, "LockRetentionPct", -1f);

        SetMetric(acquisitionText, "Acquisition", acquisition >= 0f ? acquisition.ToString("F2") + " s" : "--");
        SetMetric(reacquisitionText, "Re-acquisition", reacquisition >= 0f ? reacquisition.ToString("F2") + " s" : "--");
        SetMetric(lockText, "Lock", locked ? "LOCKED" : (detected ? "TRACKING" : "--"));

        string mode = pat != null ? pat.CurrentModeName : "--";
        SetMetric(modeText, "PAT Mode", mode);

        string disturbance = disturbances == null
            ? "--"
            : (disturbances.disturbancesEnabled ? "ACTIVE" : "OFF");
        SetMetric(disturbanceText, "Disturbance", disturbance);

        if (pipelineSummary != null)
        {
            pipelineSummary.text =
                "PIPELINE STATUS\n\n" +
                "01 CAMERA          " + State(mainCamera != null) + "\n" +
                "02 IMAGE INPUT     " + State(detector != null) + "\n" +
                "03 AI DETECTOR     " + State(detector != null) + "\n" +
                "04 CENTROID        " + State(detected) + "\n" +
                "05 TRACKER         " + State(tracker != null) + "\n" +
                "06 PAT             " + State(pat != null) + "\n" +
                "07 GIMBAL          " + State(pat != null);
        }
    }

    private void RefreshPopupLiveText()
    {
        // Popup contents are intentionally rebuilt only by user actions.
        // This method exists so the dashboard can be extended later without
        // forcing layout rebuilds every frame.
    }

    private void SetSystemOffline()
    {
        if (headerStatus != null)
        {
            headerStatus.text = "● SYSTEM OFFLINE";
            headerStatus.color = Bad;
        }

        if (statusText != null)
        {
            statusText.text = "● SYSTEM OFFLINE";
            statusText.color = Bad;
        }
    }

    private string State(bool value)
    {
        return value ? "ONLINE" : "OFFLINE";
    }

    private void SetMetric(TMP_Text text, string label, string value)
    {
        if (text != null)
            text.text = label + "\n" + value;
    }

    // ------------------------------------------------------------
    // PERFORMANCE VALUES
    // ------------------------------------------------------------

    private string GetPatError()
    {
        return pat == null ? "--" : pat.CurrentAlignmentErrorPixels.ToString("F1") + " px";
    }

    private string GetLockState()
    {
        if (pat == null)
            return "--";
        return pat.IsTargetLocked ? "LOCKED" : "NOT LOCKED";
    }

    private string GetFps()
    {
        float fps = GetFloat(performanceRecorder, "AverageFps", -1f);
        if (fps < 0f)
            fps = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        return fps.ToString("F1");
    }

    private string GetInference()
    {
        float value = GetFloat(detector, "LastInferenceMs", -1f);
        return value >= 0f ? value.ToString("F1") + " ms" : "--";
    }

    private string GetRecorderValue(string member, string fallback)
    {
        float value = GetFloat(performanceRecorder, member, -1f);
        return value >= 0f ? value.ToString("F2") : fallback;
    }

    // ------------------------------------------------------------
    // REFLECTION HELPERS
    // ------------------------------------------------------------

    private object GetMember(object target, string memberName)
    {
        if (target == null)
            return null;

        Type type = target.GetType();

        FieldInfo field = type.GetField(memberName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            return field.GetValue(target);

        PropertyInfo property = type.GetProperty(memberName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead)
            return property.GetValue(target);

        return null;
    }

    private float GetFloat(object target, string memberName, float fallback)
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

    private void InvokeMethod(object target, string methodName)
    {
        if (target == null)
            return;

        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        method?.Invoke(target, null);
    }

    // ------------------------------------------------------------
    // GENERAL UI CREATION
    // ------------------------------------------------------------

    private GameObject CreatePanel(
        Transform parent,
        string name,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax,
        bool outline)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = color.a > 0.001f;

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
        TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        text.overflowMode = TextOverflowModes.Overflow;

        return text;
    }

    private Button CreateButton(
        Transform parent,
        string name,
        string label,
        float fontSize,
        Color textColor,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 pivot,
        Vector2 size,
        UnityAction action)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = go.GetComponent<Image>();
        image.color = Button;
        image.raycastTarget = true;

        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(action);

        ColorBlock colors = button.colors;
        colors.normalColor = Button;
        colors.highlightedColor = ButtonHover;
        colors.pressedColor = Accent;
        colors.selectedColor = ButtonHover;
        button.colors = colors;

        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(Border.r, Border.g, Border.b, 0.5f);
        outline.effectDistance = new Vector2(1f, 1f);

        TMP_Text text = CreateText(go.transform, label, fontSize, textColor,
            TextAlignmentOptions.Center);
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;

        return button;
    }

    private void SetTopLeft(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void SetTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }
}
