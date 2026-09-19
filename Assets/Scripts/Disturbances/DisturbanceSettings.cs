using System;
using System.Collections.Generic;
using UnityEngine;

// ============================================================
// Enums
// ============================================================

/// <summary>Independent, combinable weather modes. Clear = no flags set.</summary>
[Flags]
public enum WeatherMode
{
    Clear    = 0,
    Haze     = 1 << 0,
    Fog      = 1 << 1,
    Rain     = 1 << 2,
    LowLight = 1 << 3
}

public enum PlatformMotionType { None, Linear, Circular, Random, Spiral, Figure8 }

public enum MotionPlane { XZ, XY, YZ }

public enum NoiseEnvironment
{
    /// <summary>DEFAULT. Uses the sea-level Earth band no matter what altitude the terminal is at.</summary>
    EarthEquivalent,
    /// <summary>Scales atmosphere-dependent effects using the altitude bands in AtmosphereProfile.</summary>
    AltitudeControlled
}

// ============================================================
// Platform motion settings
// ============================================================

[Serializable]
public class PlatformMotionSettings
{
    public PlatformMotionType type = PlatformMotionType.None;
    public MotionPlane plane = MotionPlane.XZ;

    [Header("Linear (mandatory mode)")]
    public Vector3 linearDirection = Vector3.right;
    [Tooltip("Scene units per second.")]
    public float linearSpeed = 1f;
    [Tooltip("0 = unbounded drift. >0 = ping-pong back and forth over this distance.")]
    public float linearRange = 20f;

    [Header("Circular / Figure-8 / Random")]
    [Tooltip("Circle radius, figure-8 half-width, or random-motion amplitude (scene units).")]
    public float amplitude = 5f;
    [Tooltip("Angular rate for Circular / Spiral / Figure-8 (deg/s).")]
    public float angularSpeedDeg = 20f;

    [Header("Spiral")]
    public float spiralMinRadius = 1f;
    public float spiralMaxRadius = 6f;
    [Tooltip("Seconds to go from min radius to max radius (then back).")]
    public float spiralRadialSeconds = 20f;

    [Header("Random")]
    [Tooltip("Base frequency of the smooth, seeded random wander.")]
    public float randomFrequencyHz = 0.3f;
}

// ============================================================
// Main disturbance settings (this is what a preset stores)
// ============================================================

[Serializable]
public class DisturbanceSettings
{
    [Header("Repeatability")]
    [Tooltip("Same seed + same settings + same frame sequence = identical disturbances.")]
    public int seed = 12345;

    [Header("Environment")]
    [Tooltip("EarthEquivalent (default) ignores altitude. AltitudeControlled uses the altitude bands.")]
    public NoiseEnvironment environment = NoiseEnvironment.EarthEquivalent;
    [Tooltip("Use manualAltitudeKm even if an altitude source Transform is assigned.")]
    public bool useManualAltitude = false;
    public float manualAltitudeKm = 0f;

    [Header("Gaussian Noise (sensor read noise)")]
    public bool gaussianEnabled = false;
    [Tooltip("Std-dev as a fraction of full scale (0.03 = ~8/255).")]
    [Range(0f, 0.5f)] public float gaussianSigma = 0.03f;

    [Header("Salt & Pepper Noise")]
    public bool saltPepperEnabled = false;
    [Tooltip("Fraction of pixels corrupted.")]
    [Range(0f, 0.2f)] public float saltPepperAmount = 0.01f;
    [Range(0f, 1f)] public float saltFraction = 0.5f;

    [Header("Poisson Noise (photon shot noise)")]
    public bool poissonEnabled = false;
    [Tooltip("Photons at full scale. LOWER = NOISIER.")]
    [Min(1f)] public float photonFullWell = 400f;

    [Header("Camera Jitter")]
    public bool jitterEnabled = false;
    [Tooltip("Maximum image shift in pixels (at the reference resolution). Reference levels go up to ~20.")]
    [Min(0f)] public float jitterPx = 5f;
    [Tooltip("0 = independent every frame, ->1 = slow drifting wander.")]
    [Range(0f, 0.95f)] public float jitterCorrelation = 0.2f;
    [Tooltip("jitterPx is defined at this image width; scaled if the YOLO input size differs.")]
    public float jitterReferenceResolution = 640f;

    [Header("Platform Motion")]
    public PlatformMotionSettings platform = new PlatformMotionSettings();

    [Header("Weather (combine freely)")]
    public WeatherMode weather = WeatherMode.Clear;
    [Range(0f, 1f)] public float hazeIntensity = 0.5f;
    [Range(0f, 1f)] public float fogIntensity = 0.5f;
    [Range(0f, 1f)] public float rainIntensity = 0.5f;
    [Range(0f, 1f)] public float lowLightIntensity = 0.5f;

    public DisturbanceSettings Clone()
    {
        return JsonUtility.FromJson<DisturbanceSettings>(JsonUtility.ToJson(this));
    }
}

// ============================================================
// Altitude bands
// ============================================================

[Serializable]
public class AltitudeBand
{
    public string name = "Band";

    [Tooltip("Band starts here (km above surface) and extends up to the next band's start. Keep bands sorted.")]
    public float startKm = 0f;

    [Header("Multipliers applied to the user's settings")]
    [Tooltip("Gaussian sigma; Poisson noise gets stronger as this rises.")]
    [Min(0f)] public float noiseScale = 1f;
    [Tooltip("Salt & pepper amount (e.g. cosmic-ray / hot-pixel hits rise with altitude).")]
    [Min(0f)] public float impulseScale = 1f;
    [Tooltip("Camera jitter amplitude (turbulence / angle-of-arrival wander share).")]
    [Min(0f)] public float jitterScale = 1f;
    [Tooltip("Haze / fog / rain density (i.e. how much atmosphere is in the path).")]
    [Min(0f)] public float weatherScale = 1f;

    [Tooltip("Weather modes physically possible in this band.")]
    public WeatherMode allowedWeather =
        WeatherMode.Haze | WeatherMode.Fog | WeatherMode.Rain | WeatherMode.LowLight;
}

public struct AtmosphereSample
{
    public string bandName;
    public float noiseScale, impulseScale, jitterScale, weatherScale;
    public WeatherMode allowedWeather;

    public const WeatherMode AllWeather = (WeatherMode)15;

    public static AtmosphereSample Neutral => new AtmosphereSample
    {
        bandName = "Neutral",
        noiseScale = 1f, impulseScale = 1f, jitterScale = 1f, weatherScale = 1f,
        allowedWeather = AllWeather
    };

    public static AtmosphereSample From(AltitudeBand b) => new AtmosphereSample
    {
        bandName = b.name,
        noiseScale = b.noiseScale, impulseScale = b.impulseScale,
        jitterScale = b.jitterScale, weatherScale = b.weatherScale,
        allowedWeather = b.allowedWeather
    };

    public static AtmosphereSample Lerp(AtmosphereSample a, AtmosphereSample b, float w)
    {
        return new AtmosphereSample
        {
            bandName = w < 0.5f ? a.bandName : b.bandName,
            noiseScale = Mathf.Lerp(a.noiseScale, b.noiseScale, w),
            impulseScale = Mathf.Lerp(a.impulseScale, b.impulseScale, w),
            jitterScale = Mathf.Lerp(a.jitterScale, b.jitterScale, w),
            weatherScale = Mathf.Lerp(a.weatherScale, b.weatherScale, w),
            allowedWeather = w < 0.5f ? a.allowedWeather : b.allowedWeather
        };
    }
}

[Serializable]
public class AtmosphereProfile
{
    [Tooltip("Width (km) of the smooth transition centred on each band boundary. 0 = hard steps.")]
    [Min(0f)] public float blendKm = 1f;

    [Tooltip("Sorted by startKm. Band 0 is also the 'Earth equivalent' reference.")]
    public List<AltitudeBand> bands = new List<AltitudeBand>();

    /// <summary>Sea-level Earth conditions = bands[0]. Used by NoiseEnvironment.EarthEquivalent.</summary>
    public AtmosphereSample EarthReference =>
        bands.Count > 0 ? AtmosphereSample.From(bands[0]) : AtmosphereSample.Neutral;

    public AtmosphereSample Evaluate(float altKm)
    {
        if (bands.Count == 0)
            return AtmosphereSample.Neutral;

        int i = 0;
        for (int k = 1; k < bands.Count; k++)
            if (altKm >= bands[k].startKm)
                i = k;

        AtmosphereSample cur = AtmosphereSample.From(bands[i]);

        float half = blendKm * 0.5f;
        if (half <= 0f)
            return cur;

        // Blend with the band below near this band's lower edge.
        if (i > 0)
        {
            float edge = bands[i].startKm;
            if (altKm < edge + half)
            {
                float w = Mathf.SmoothStep(0f, 1f, (altKm - (edge - half)) / blendKm);
                return AtmosphereSample.Lerp(AtmosphereSample.From(bands[i - 1]), cur, w);
            }
        }

        // Blend with the band above near this band's upper edge.
        if (i < bands.Count - 1)
        {
            float edge = bands[i + 1].startKm;
            if (altKm > edge - half)
            {
                float w = Mathf.SmoothStep(0f, 1f, (altKm - (edge - half)) / blendKm);
                return AtmosphereSample.Lerp(cur, AtmosphereSample.From(bands[i + 1]), w);
            }
        }

        return cur;
    }

    /// <summary>
    /// Illustrative Earth defaults. Tune these against whatever link-budget /
    /// reference numbers you cite in the report.
    /// </summary>
    public static AtmosphereProfile CreateEarthDefault()
    {
        var p = new AtmosphereProfile { blendKm = 1f };

        p.bands.Add(new AltitudeBand
        {
            name = "Boundary layer (Earth reference)", startKm = 0f,
            noiseScale = 1f, impulseScale = 1f, jitterScale = 1f, weatherScale = 1f,
            allowedWeather = AtmosphereSample.AllWeather
        });
        p.bands.Add(new AltitudeBand
        {
            name = "Free troposphere", startKm = 2f,
            noiseScale = 1f, impulseScale = 1f, jitterScale = 0.7f, weatherScale = 0.4f,
            allowedWeather = WeatherMode.Haze | WeatherMode.Rain | WeatherMode.LowLight
        });
        p.bands.Add(new AltitudeBand
        {
            name = "Stratosphere", startKm = 12f,
            noiseScale = 1f, impulseScale = 1.5f, jitterScale = 0.3f, weatherScale = 0.05f,
            allowedWeather = WeatherMode.Haze | WeatherMode.LowLight
        });
        p.bands.Add(new AltitudeBand
        {
            name = "Upper atmosphere", startKm = 50f,
            noiseScale = 1f, impulseScale = 2.5f, jitterScale = 0.2f, weatherScale = 0.005f,
            allowedWeather = WeatherMode.LowLight
        });
        p.bands.Add(new AltitudeBand
        {
            name = "Space", startKm = 100f,
            noiseScale = 1f, impulseScale = 4f, jitterScale = 0.2f, weatherScale = 0f,
            allowedWeather = WeatherMode.LowLight
        });

        return p;
    }
}
