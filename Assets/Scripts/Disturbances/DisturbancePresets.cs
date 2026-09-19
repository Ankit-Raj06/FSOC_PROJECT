using UnityEngine;

public enum DisturbancePresetId
{
    Baseline,
    Mild,
    Moderate,
    Severe,
    VibrationStress,
    HazyDay,
    DenseFog,
    HeavyRain,
    NightLowLight,
    FullStorm
}

/// <summary>
/// Built-in presets. Each has a FIXED seed, so running the same preset on the
/// same frame sequence reproduces exactly the same disturbances.
/// Applying a preset keeps the current environment mode / altitude by default.
/// </summary>
public static class DisturbancePresets
{
    public static string[] Names => System.Enum.GetNames(typeof(DisturbancePresetId));

    public static DisturbanceSettings Create(DisturbancePresetId id)
    {
        var s = new DisturbanceSettings();

        switch (id)
        {
            case DisturbancePresetId.Baseline:
                s.seed = 1000;
                break;

            case DisturbancePresetId.Mild:
                s.seed = 1001;
                Noise(s, 0.01f, 0.002f, 1500f);
                Jitter(s, 2f, 0.3f);
                break;

            case DisturbancePresetId.Moderate:
                s.seed = 1002;
                Noise(s, 0.03f, 0.005f, 400f);
                Jitter(s, 5f, 0.3f);
                s.weather = WeatherMode.Haze;
                s.hazeIntensity = 0.4f;
                Motion(s, PlatformMotionType.Linear);
                break;

            case DisturbancePresetId.Severe:
                s.seed = 1003;
                Noise(s, 0.06f, 0.02f, 150f);
                Jitter(s, 12f, 0.5f);
                s.weather = WeatherMode.Haze | WeatherMode.Rain;
                s.hazeIntensity = 0.7f;
                s.rainIntensity = 0.6f;
                Motion(s, PlatformMotionType.Random);
                s.platform.amplitude = 3f;
                s.platform.randomFrequencyHz = 0.4f;
                break;

            case DisturbancePresetId.VibrationStress:
                s.seed = 1004;
                Jitter(s, 20f, 0.1f);
                Motion(s, PlatformMotionType.Circular);
                break;

            case DisturbancePresetId.HazyDay:
                s.seed = 1005;
                Noise(s, 0.01f, 0f, 800f);
                s.weather = WeatherMode.Haze;
                s.hazeIntensity = 0.8f;
                break;

            case DisturbancePresetId.DenseFog:
                s.seed = 1006;
                Noise(s, 0.01f, 0f, 800f);
                s.weather = WeatherMode.Fog;
                s.fogIntensity = 0.8f;
                break;

            case DisturbancePresetId.HeavyRain:
                s.seed = 1007;
                Noise(s, 0.02f, 0.003f, 500f);
                Jitter(s, 3f, 0.3f);
                s.weather = WeatherMode.Rain | WeatherMode.Haze;
                s.rainIntensity = 0.9f;
                s.hazeIntensity = 0.3f;
                break;

            case DisturbancePresetId.NightLowLight:
                s.seed = 1008;
                Noise(s, 0.02f, 0.002f, 300f);
                s.weather = WeatherMode.LowLight;
                s.lowLightIntensity = 0.8f;
                break;

            case DisturbancePresetId.FullStorm:
                s.seed = 1009;
                Noise(s, 0.06f, 0.02f, 150f);
                Jitter(s, 15f, 0.4f);
                s.weather = WeatherMode.Fog | WeatherMode.Rain | WeatherMode.LowLight;
                s.fogIntensity = 0.5f;
                s.rainIntensity = 0.8f;
                s.lowLightIntensity = 0.6f;
                Motion(s, PlatformMotionType.Figure8);
                break;
        }

        return s;
    }

    private static void Noise(DisturbanceSettings s, float sigma, float saltPepper, float fullWell)
    {
        s.gaussianEnabled = sigma > 0f;
        s.gaussianSigma = sigma;

        s.saltPepperEnabled = saltPepper > 0f;
        s.saltPepperAmount = saltPepper;

        s.poissonEnabled = fullWell > 0f;
        s.photonFullWell = fullWell;
    }

    private static void Jitter(DisturbanceSettings s, float px, float corr)
    {
        s.jitterEnabled = px > 0f;
        s.jitterPx = px;
        s.jitterCorrelation = corr;
    }

    private static void Motion(DisturbanceSettings s, PlatformMotionType type)
    {
        s.platform.type = type;
    }
}

/// <summary>Save your own presets as assets: Create > FSOC > Disturbance Preset.</summary>
[CreateAssetMenu(menuName = "FSOC/Disturbance Preset", fileName = "DisturbancePreset")]
public class DisturbancePresetAsset : ScriptableObject
{
    public DisturbanceSettings settings = new DisturbanceSettings();
}
