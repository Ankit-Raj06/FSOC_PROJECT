using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central disturbance engine for the FSOC simulator.
///
/// DEFAULT BEHAVIOUR: Earth-equivalent noise (sea-level band), independent of
/// altitude. Switch settings.environment to AltitudeControlled to let the
/// altitude bands in `atmosphere` scale turbulence/weather/noise.
///
/// GROUND TRUTH SAFETY: image effects only modify the Texture2D that is passed
/// to YOLO. Nothing here writes to the target, tracker, or metric code.
/// (Platform motion moves the terminal mount, which is real geometry; ground
/// truth must be computed from world transforms, as it already is.)
/// </summary>
[DefaultExecutionOrder(-200)]
public class DisturbanceManager : MonoBehaviour
{
    public static DisturbanceManager Instance { get; private set; }

    [Header("Master")]
    public bool disturbancesEnabled = true;

    [Header("Disturbance Settings")]
    public DisturbanceSettings settings = new DisturbanceSettings();
    public List<DisturbancePresetAsset> customPresets = new List<DisturbancePresetAsset>();

    [Header("Atmosphere / Altitude (used in AltitudeControlled mode)")]
    public AtmosphereProfile atmosphere = AtmosphereProfile.CreateEarthDefault();

    [Tooltip("Terminal / satellite whose altitude drives the bands. Empty = use manual altitude.")]
    public Transform altitudeSource;

    [Tooltip("Optional planet centre. If set: altitude = distance*unitsToKm - planetRadiusKm. " +
             "If empty: altitude = altitudeSource.position.y * unitsToKm.")]
    public Transform planetCenter;
    public float unitsToKm = 1f;
    public float planetRadiusKm = 6371f;

    // -------- runtime state (read-only, handy for UI / performance log) --------
    public float CurrentAltitudeKm { get; private set; }
    public AtmosphereSample ActiveAtmosphere { get; private set; } = AtmosphereSample.Neutral;
    public Vector2 LastJitterPx { get; private set; }
    public float SimTime { get; private set; }

    /// <summary>Raised when the seeded sequences restart (preset applied / manual restart).</summary>
    public event Action StateReset;

    private readonly ImageDisturbanceProcessor processor = new ImageDisturbanceProcessor();
    private DetRng jitterRng;
    private Vector2 jitterState;
    private long frameCounter;

    // Optical-depth constants at intensity = 1 and weatherScale = 1.
    private const float HazeTau = 0.6f;
    private const float FogTau = 2.5f;
    private const float RainTau = 0.5f;
    private const int RainStreaksAtFull = 900;      // per 640x640 frame
    private const float RainStreakStrength = 0.35f;
    private const float LowLightMinGain = 0.03f;

    private static readonly Vector3 HazeAirlight = new Vector3(0.72f, 0.75f, 0.80f);
    private static readonly Vector3 FogAirlight = new Vector3(0.80f, 0.80f, 0.82f);
    private static readonly Vector3 RainAirlight = new Vector3(0.60f, 0.62f, 0.66f);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResetRuntimeState();
        RefreshEnvironment();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        SimTime += Time.deltaTime;
        RefreshEnvironment();
    }

    // ============================================================
    // ENVIRONMENT
    // ============================================================

    public float ComputeAltitudeKm()
    {
        if (settings.useManualAltitude || altitudeSource == null)
            return settings.manualAltitudeKm;

        if (planetCenter != null)
        {
            float distKm =
                Vector3.Distance(altitudeSource.position, planetCenter.position) * unitsToKm;
            return distKm - planetRadiusKm;
        }

        return altitudeSource.position.y * unitsToKm;
    }

    public void RefreshEnvironment()
    {
        CurrentAltitudeKm = ComputeAltitudeKm();

        ActiveAtmosphere =
            settings.environment == NoiseEnvironment.EarthEquivalent
                ? atmosphere.EarthReference
                : atmosphere.Evaluate(CurrentAltitudeKm);
    }

    // ============================================================
    // PRESETS / STATE
    // ============================================================

    public void ApplyPreset(DisturbancePresetId id, bool keepEnvironment = true)
    {
        ApplySettings(DisturbancePresets.Create(id), keepEnvironment);
    }

    public void ApplyPreset(DisturbancePresetAsset asset, bool keepEnvironment = true)
    {
        if (asset != null)
            ApplySettings(asset.settings, keepEnvironment);
    }

    public void ApplySettings(DisturbanceSettings source, bool keepEnvironment = true)
    {
        var env = settings.environment;
        bool useManual = settings.useManualAltitude;
        float manualAlt = settings.manualAltitudeKm;

        settings = source.Clone();

        if (keepEnvironment)
        {
            settings.environment = env;
            settings.useManualAltitude = useManual;
            settings.manualAltitudeKm = manualAlt;
        }

        ResetRuntimeState();
        RefreshEnvironment();
    }

    /// <summary>Restart every seeded sequence (image noise, jitter, platform motion clock).</summary>
    public void ResetRuntimeState()
    {
        frameCounter = 0;
        SimTime = 0f;
        jitterState = Vector2.zero;
        LastJitterPx = Vector2.zero;
        jitterRng = new DetRng(DetRng.Mix(settings.seed, 0x4A17));
        StateReset?.Invoke();
    }

    // ============================================================
    // IMAGE PIPELINE ENTRY POINT
    // ============================================================

    /// <summary>
    /// Call from YoloDetection right AFTER ReadPixels and BEFORE Apply().
    /// Only the texture passed in is modified.
    /// </summary>
    public void ApplyImageDisturbances(Texture2D tex)
    {
        if (!disturbancesEnabled || tex == null)
            return;

        RefreshEnvironment();

        ImageFx fx = BuildImageFx(tex.width, tex.height);
        processor.Process(tex, fx, DetRng.Mix(settings.seed, frameCounter++));
    }

    private ImageFx BuildImageFx(int w, int h)
    {
        DisturbanceSettings s = settings;
        AtmosphereSample a = ActiveAtmosphere;

        var fx = new ImageFx
        {
            gain = 1f,
            airlight = Vector3.one,
            saltFraction = s.saltFraction
        };

        // ---- camera jitter ----
        if (s.jitterEnabled)
        {
            Vector2 px = StepJitter(w, h);
            fx.shiftX = Mathf.RoundToInt(px.x);
            fx.shiftY = Mathf.RoundToInt(px.y);
        }

        // ---- sensor noise ----
        if (s.gaussianEnabled)
            fx.gaussianSigma = s.gaussianSigma * a.noiseScale;

        if (s.poissonEnabled)
            fx.poissonFullWell = Mathf.Max(1f, s.photonFullWell / Mathf.Max(a.noiseScale, 0.01f));

        if (s.saltPepperEnabled)
            fx.impulseAmount = Mathf.Clamp(s.saltPepperAmount * a.impulseScale, 0f, 0.5f);

        // ---- weather (only modes physically allowed in this band) ----
        WeatherMode wx = s.weather & a.allowedWeather;
        float scale = a.weatherScale;

        float tauH = (wx & WeatherMode.Haze) != 0 ? HazeTau * s.hazeIntensity * scale : 0f;
        float tauF = (wx & WeatherMode.Fog) != 0 ? FogTau * s.fogIntensity * scale : 0f;
        float tauR = (wx & WeatherMode.Rain) != 0 ? RainTau * s.rainIntensity * scale : 0f;

        float tau = tauH + tauF + tauR;
        if (tau > 0f)
        {
            fx.tau = tau;
            fx.airlight = (HazeAirlight * tauH + FogAirlight * tauF + RainAirlight * tauR) / tau;
        }

        if ((wx & WeatherMode.Rain) != 0)
        {
            float areaScale = (w * (float)h) / (640f * 640f);
            fx.rainStreaks = Mathf.RoundToInt(RainStreaksAtFull * s.rainIntensity * scale * areaScale);
            fx.rainStrength = RainStreakStrength;
        }

        if ((wx & WeatherMode.LowLight) != 0)
            fx.gain = Mathf.Lerp(1f, LowLightMinGain, s.lowLightIntensity);

        return fx;
    }

    // ============================================================
    // CAMERA JITTER (shared by YOLO path and SimulatedDetection path)
    // ============================================================

    /// <summary>Advances the jitter process one frame and returns the pixel offset.</summary>
    public Vector2 StepJitter(int imageWidth, int imageHeight)
    {
        if (!disturbancesEnabled || !settings.jitterEnabled)
        {
            LastJitterPx = Vector2.zero;
            return Vector2.zero;
        }

        float c = Mathf.Clamp(settings.jitterCorrelation, 0f, 0.95f);
        float k = Mathf.Sqrt(1f - c * c);

        jitterState.x = c * jitterState.x + k * jitterRng.NextGaussian();
        jitterState.y = c * jitterState.y + k * jitterRng.NextGaussian();

        float refRes = Mathf.Max(1f, settings.jitterReferenceResolution);
        float amp = settings.jitterPx * ActiveAtmosphere.jitterScale * (imageWidth / refRes);

        // 0.4 * N(0,1) -> ~99% inside +-1, then hard-bounded to +-amp.
        LastJitterPx = new Vector2(
            Mathf.Clamp(jitterState.x * 0.4f, -1f, 1f) * amp,
            Mathf.Clamp(jitterState.y * 0.4f, -1f, 1f) * amp);

        return LastJitterPx;
    }

    /// <summary>
    /// For SimulatedDetection (geometric, no image): shifts the detected
    /// viewport position by the jitter offset. Other effects are image-domain
    /// and only apply on the YOLO path.
    /// </summary>
    public Vector2 ApplyJitterToViewport(Vector2 viewport, int referenceResolution = 640)
    {
        if (!disturbancesEnabled || !settings.jitterEnabled)
            return viewport;

        Vector2 px = StepJitter(referenceResolution, referenceResolution);
        return viewport + px / referenceResolution;
    }

    // ============================================================
    // LOGGING HELPER
    // ============================================================

    /// <summary>One-line description of the active disturbance state for the performance log.</summary>
    public string DescribeState()
    {
        var s = settings;
        return
            $"enabled={disturbancesEnabled} env={s.environment} alt={CurrentAltitudeKm:F1}km " +
            $"band='{ActiveAtmosphere.bandName}' seed={s.seed} " +
            $"gauss={(s.gaussianEnabled ? s.gaussianSigma.ToString("F3") : "off")} " +
            $"s&p={(s.saltPepperEnabled ? s.saltPepperAmount.ToString("F3") : "off")} " +
            $"poisson={(s.poissonEnabled ? s.photonFullWell.ToString("F0") : "off")} " +
            $"jitter={(s.jitterEnabled ? s.jitterPx.ToString("F1") + "px" : "off")} " +
            $"motion={s.platform.type} weather={s.weather}";
    }
}
