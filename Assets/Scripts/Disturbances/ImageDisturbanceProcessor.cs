using UnityEngine;

/// <summary>Small deterministic PRNG (xorshift64) so disturbances are repeatable.</summary>
public struct DetRng
{
    private ulong s;

    public DetRng(ulong seed)
    {
        s = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
        NextU32();
        NextU32();
    }

    /// <summary>Hash (seed, counter) into a well-mixed 64-bit seed.</summary>
    public static ulong Mix(int seed, long counter)
    {
        unchecked
        {
            ulong z = (ulong)seed * 0x9E3779B97F4A7C15UL
                    + (ulong)counter * 0xBF58476D1CE4E5B9UL
                    + 0x94D049BB133111EBUL;
            z ^= z >> 30; z *= 0xBF58476D1CE4E5B9UL;
            z ^= z >> 27; z *= 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return z;
        }
    }

    public uint NextU32()
    {
        s ^= s << 13;
        s ^= s >> 7;
        s ^= s << 17;
        return (uint)(s >> 32);
    }

    /// <summary>[0,1)</summary>
    public float NextFloat() => (NextU32() >> 8) * (1f / 16777216f);

    public float NextGaussian()
    {
        float u1 = Mathf.Max(NextFloat(), 1e-7f);
        float u2 = NextFloat();
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(6.2831853f * u2);
    }
}

/// <summary>Fully-resolved per-frame image effect parameters (built by DisturbanceManager).</summary>
public struct ImageFx
{
    public int shiftX, shiftY;         // camera jitter, in pixels
    public float gain;                 // low-light exposure gain (1 = off)
    public float tau;                  // combined haze/fog/rain optical depth
    public Vector3 airlight;           // 0-1 colour the scene fades toward
    public int rainStreaks;
    public float rainStrength;         // 0-1 blend per streak pixel
    public float poissonFullWell;      // 0 = off
    public float gaussianSigma;        // 0-1 fraction of full scale, 0 = off
    public float impulseAmount;        // fraction of pixels, 0 = off
    public float saltFraction;

    public bool IsIdentity =>
        shiftX == 0 && shiftY == 0 &&
        Mathf.Approximately(gain, 1f) && tau <= 0f && rainStreaks <= 0 &&
        poissonFullWell <= 0f && gaussianSigma <= 0f && impulseAmount <= 0f;
}

/// <summary>
/// Applies disturbances to the CPU-side image that is fed to YOLO.
///
/// Pipeline order (physically motivated):
///   1. Camera jitter shift        (scene moves relative to sensor)
///   2. Low-light gain + haze/fog/rain attenuation with airlight
///   3. Rain streaks
///   4. Poisson shot noise         (depends on signal level)
///   5. Gaussian read noise
///   6. Salt & pepper              (sensor defects / radiation hits)
///
/// It ONLY modifies the Texture2D it is handed. Scene transforms, the
/// tracker, and any ground-truth metric code are never touched.
/// </summary>
public class ImageDisturbanceProcessor
{
    private byte[] src;
    private byte[] work;

    public void Process(Texture2D tex, in ImageFx fx, ulong seed)
    {
        if (tex == null || fx.IsIdentity)
            return;

        if (tex.format != TextureFormat.RGB24)
        {
            Debug.LogWarning("[Disturbance] Expected RGB24 texture, got " + tex.format + ". Skipping.");
            return;
        }

        int w = tex.width;
        int h = tex.height;
        int n = w * h * 3;

        if (src == null || src.Length != n)
        {
            src = new byte[n];
            work = new byte[n];
        }

        tex.GetRawTextureData<byte>().CopyTo(src);

        var rng = new DetRng(seed);

        // ---------------- Pass 1: shift + exposure + atmosphere ----------------
        float T = Mathf.Exp(-fx.tau);
        float mul = fx.gain * T;
        float k = fx.gain * 255f * (1f - T);
        float addR = k * fx.airlight.x;
        float addG = k * fx.airlight.y;
        float addB = k * fx.airlight.z;

        for (int y = 0; y < h; y++)
        {
            int sy = y - fx.shiftY;
            bool rowOk = sy >= 0 && sy < h;

            for (int x = 0; x < w; x++)
            {
                int sx = x - fx.shiftX;
                int di = (y * w + x) * 3;

                float r = 0f, g = 0f, b = 0f;
                if (rowOk && sx >= 0 && sx < w)
                {
                    int si = (sy * w + sx) * 3;
                    r = src[si];
                    g = src[si + 1];
                    b = src[si + 2];
                }

                work[di]     = ToByte(r * mul + addR);
                work[di + 1] = ToByte(g * mul + addG);
                work[di + 2] = ToByte(b * mul + addB);
            }
        }

        // ---------------- Rain streaks ----------------
        if (fx.rainStreaks > 0)
        {
            float sizeScale = w / 640f;

            for (int i = 0; i < fx.rainStreaks; i++)
            {
                float x0 = rng.NextFloat() * w;
                float y0 = rng.NextFloat() * h;
                int len = Mathf.Max(3, Mathf.RoundToInt((6f + rng.NextFloat() * 18f) * sizeScale));

                const float slantX = 0.2f;
                const float slantY = 1f;
                float inv = 1f / Mathf.Sqrt(slantX * slantX + slantY * slantY);
                float dx = slantX * inv;
                float dy = slantY * inv;

                for (int s2 = 0; s2 < len; s2++)
                {
                    int px = (int)(x0 + dx * s2);
                    int py = (int)(y0 + dy * s2);
                    if (px < 0 || px >= w || py < 0 || py >= h)
                        continue;

                    int idx = (py * w + px) * 3;
                    for (int c = 0; c < 3; c++)
                    {
                        float v = work[idx + c];
                        work[idx + c] = ToByte(v + (255f - v) * fx.rainStrength);
                    }
                }
            }
        }

        // ---------------- Pass 2: Poisson + Gaussian ----------------
        bool poisson = fx.poissonFullWell > 0f;
        bool gauss = fx.gaussianSigma > 0f;

        if (poisson || gauss)
        {
            float fw = fx.poissonFullWell;
            float sigma = fx.gaussianSigma;
            const float inv255 = 1f / 255f;

            for (int i = 0; i < n; i++)
            {
                float v = work[i] * inv255;

                if (poisson)
                    v = SamplePoisson(v * fw, ref rng) / fw;

                if (gauss)
                    v += rng.NextGaussian() * sigma;

                work[i] = ToByte(v * 255f);
            }
        }

        // ---------------- Salt & pepper ----------------
        if (fx.impulseAmount > 0f)
        {
            int pixels = w * h;
            int count = Mathf.RoundToInt(fx.impulseAmount * pixels);

            for (int i = 0; i < count; i++)
            {
                int p = (int)(rng.NextU32() % (uint)pixels) * 3;
                byte val = rng.NextFloat() < fx.saltFraction ? (byte)255 : (byte)0;
                work[p] = val;
                work[p + 1] = val;
                work[p + 2] = val;
            }
        }

        // Caller is responsible for tex.Apply() (YoloDetection already does this).
        tex.SetPixelData(work, 0);
    }

    private static byte ToByte(float v)
    {
        if (v <= 0f) return 0;
        if (v >= 254.5f) return 255;
        return (byte)(v + 0.5f);
    }

    private static float SamplePoisson(float lambda, ref DetRng rng)
    {
        if (lambda <= 0f)
            return 0f;

        if (lambda < 30f)
        {
            // Knuth
            float L = Mathf.Exp(-lambda);
            int k = 0;
            float p = 1f;
            do
            {
                k++;
                p *= rng.NextFloat();
            } while (p > L);
            return k - 1;
        }

        // Normal approximation for larger means
        float g = lambda + Mathf.Sqrt(lambda) * rng.NextGaussian();
        return Mathf.Max(0f, Mathf.Round(g));
    }
}
