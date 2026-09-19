using UnityEngine;

public enum ScreenCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Runtime control panel (IMGUI, no dependencies, works in built .exe).
/// Every disturbance can be enabled and configured here.
/// If you'd rather use uGUI/TMP, bind your widgets to the same public fields on
/// DisturbanceManager.settings.
/// </summary>
public class DisturbancePanel : MonoBehaviour
{
    public DisturbanceManager manager;
    public bool startOpen = false;

    [Header("Layout")]
    public ScreenCorner buttonCorner = ScreenCorner.TopLeft;
    public Vector2 buttonOffset = new Vector2(16, 60);   // 60 puts it below "Open Tutorial"
    public Vector2 buttonSize = new Vector2(150, 26);

    private bool open;
    private Rect win = new Rect(16, 44, 390, 660);
    private Vector2 scroll;

    private static readonly string[] EnvNames = { "Earth Equivalent (default)", "Altitude Controlled" };
    private static readonly string[] PlaneNames = { "XZ", "XY", "YZ" };
    private static readonly string[] MotionNames = System.Enum.GetNames(typeof(PlatformMotionType));

    public static Rect Anchor(ScreenCorner corner, Vector2 size, Vector2 offset)
    {
        bool left = corner == ScreenCorner.TopLeft || corner == ScreenCorner.BottomLeft;
        bool top = corner == ScreenCorner.TopLeft || corner == ScreenCorner.TopRight;

        float x = left ? offset.x : Screen.width - size.x - offset.x;
        float y = top ? offset.y : Screen.height - size.y - offset.y;
        return new Rect(x, y, size.x, size.y);
    }

    private void Start()
    {
        open = startOpen;
        if (manager == null) manager = DisturbanceManager.Instance;
    }

    private void OnGUI()
    {
        if (manager == null)
        {
            manager = DisturbanceManager.Instance;
            if (manager == null) return;
        }

        if (GUI.Button(Anchor(buttonCorner, buttonSize, buttonOffset),
               open ? "Hide Disturbances" : "Disturbances"))
    	open = !open;

        if (open)
            win = GUI.Window(0x5A17, win, Draw, "Disturbance Control");
    }

    private void Draw(int id)
    {
        var s = manager.settings;
        scroll = GUILayout.BeginScrollView(scroll);

        manager.disturbancesEnabled =
            GUILayout.Toggle(manager.disturbancesEnabled, "Disturbances enabled (master)");

        // ---------------- Presets ----------------
        Header("Presets (fixed seeds = repeatable)");
        int p = GUILayout.SelectionGrid(-1, DisturbancePresets.Names, 3);
        if (p >= 0) manager.ApplyPreset((DisturbancePresetId)p);

        if (manager.customPresets != null)
        {
            foreach (var asset in manager.customPresets)
                if (asset != null && GUILayout.Button("Custom: " + asset.name))
                    manager.ApplyPreset(asset);
        }

        s = manager.settings; // may have been replaced by a preset

        // ---------------- Environment ----------------
        Header("Environment");
        s.environment = (NoiseEnvironment)GUILayout.SelectionGrid((int)s.environment, EnvNames, 1);
        GUILayout.Label($"Altitude {manager.CurrentAltitudeKm:F1} km | band: {manager.ActiveAtmosphere.bandName}");
        s.useManualAltitude = GUILayout.Toggle(s.useManualAltitude, "Manual altitude override");
        if (s.useManualAltitude || manager.altitudeSource == null)
            s.manualAltitudeKm = Slider("Altitude (km)", s.manualAltitudeKm, 0f, 1000f, "F1");

        // ---------------- Image noise ----------------
        Header("Gaussian noise");
        s.gaussianEnabled = GUILayout.Toggle(s.gaussianEnabled, "Enabled");
        if (s.gaussianEnabled)
            s.gaussianSigma = Slider("Sigma", s.gaussianSigma, 0f, 0.3f, "F3");

        Header("Salt & pepper");
        s.saltPepperEnabled = GUILayout.Toggle(s.saltPepperEnabled, "Enabled");
        if (s.saltPepperEnabled)
        {
            s.saltPepperAmount = Slider("Amount", s.saltPepperAmount, 0f, 0.1f, "F3");
            s.saltFraction = Slider("Salt fraction", s.saltFraction, 0f, 1f);
        }

        Header("Poisson (shot) noise");
        s.poissonEnabled = GUILayout.Toggle(s.poissonEnabled, "Enabled");
        if (s.poissonEnabled)
            s.photonFullWell = Slider("Full well (low=noisy)", s.photonFullWell, 20f, 3000f, "F0");

        // ---------------- Jitter ----------------
        Header("Camera jitter");
        s.jitterEnabled = GUILayout.Toggle(s.jitterEnabled, "Enabled");
        if (s.jitterEnabled)
        {
            s.jitterPx = Slider("Max shift (px)", s.jitterPx, 0f, 30f, "F1");
            s.jitterCorrelation = Slider("Correlation", s.jitterCorrelation, 0f, 0.95f);
            GUILayout.Label($"Last offset: {manager.LastJitterPx.x:F1}, {manager.LastJitterPx.y:F1} px");
        }

        // ---------------- Platform motion ----------------
        Header("Platform motion");
        var m = s.platform;
        m.type = (PlatformMotionType)GUILayout.SelectionGrid((int)m.type, MotionNames, 3);
        if (m.type != PlatformMotionType.None)
        {
            m.plane = (MotionPlane)GUILayout.SelectionGrid((int)m.plane, PlaneNames, 3);

            switch (m.type)
            {
                case PlatformMotionType.Linear:
                    m.linearSpeed = Slider("Speed (u/s)", m.linearSpeed, 0f, 20f);
                    m.linearRange = Slider("Range (0=drift)", m.linearRange, 0f, 200f, "F0");
                    break;
                case PlatformMotionType.Circular:
                case PlatformMotionType.Figure8:
                    m.amplitude = Slider("Amplitude", m.amplitude, 0f, 50f);
                    m.angularSpeedDeg = Slider("Rate (deg/s)", m.angularSpeedDeg, 0f, 180f, "F0");
                    break;
                case PlatformMotionType.Spiral:
                    m.spiralMinRadius = Slider("Min radius", m.spiralMinRadius, 0f, 50f);
                    m.spiralMaxRadius = Slider("Max radius", m.spiralMaxRadius, 0f, 50f);
                    m.spiralRadialSeconds = Slider("Radial period (s)", m.spiralRadialSeconds, 1f, 120f, "F0");
                    m.angularSpeedDeg = Slider("Rate (deg/s)", m.angularSpeedDeg, 0f, 180f, "F0");
                    break;
                case PlatformMotionType.Random:
                    m.amplitude = Slider("Amplitude", m.amplitude, 0f, 50f);
                    m.randomFrequencyHz = Slider("Frequency (Hz)", m.randomFrequencyHz, 0.02f, 3f);
                    break;
            }
        }

        // ---------------- Weather ----------------
        Header("Weather (combine freely)");
        s.weather = WeatherToggle(s.weather, WeatherMode.Haze, "Haze", ref s.hazeIntensity);
        s.weather = WeatherToggle(s.weather, WeatherMode.Fog, "Fog", ref s.fogIntensity);
        s.weather = WeatherToggle(s.weather, WeatherMode.Rain, "Rain", ref s.rainIntensity);
        s.weather = WeatherToggle(s.weather, WeatherMode.LowLight, "Low light", ref s.lowLightIntensity);

        var blocked = s.weather & ~manager.ActiveAtmosphere.allowedWeather;
        if (blocked != WeatherMode.Clear)
            GUILayout.Label($"Not possible at this altitude (ignored): {blocked}");

        // ---------------- Seed ----------------
        Header("Repeatability");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Seed", GUILayout.Width(50));
        string t = GUILayout.TextField(s.seed.ToString());
        if (int.TryParse(t, out int v)) s.seed = v;
        if (GUILayout.Button("Restart sequence", GUILayout.Width(130)))
            manager.ResetRuntimeState();
        GUILayout.EndHorizontal();

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private static void Header(string text)
    {
        GUILayout.Space(6);
        GUILayout.Label("— " + text + " —");
    }

    private static float Slider(string label, float value, float min, float max, string fmt = "F2")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(140));
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(value.ToString(fmt), GUILayout.Width(52));
        GUILayout.EndHorizontal();
        return value;
    }

    private static WeatherMode WeatherToggle(WeatherMode cur, WeatherMode flag, string label, ref float intensity)
    {
        bool on = (cur & flag) != 0;
        bool now = GUILayout.Toggle(on, label);

        if (now)
            intensity = Slider("  Intensity", intensity, 0f, 1f);

        return now ? (cur | flag) : (cur & ~flag);
    }
}
