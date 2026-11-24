using UnityEngine;

namespace OmpaLompa.Audio
{
    /// <summary>
    /// Estimates the pitch (fundamental frequency) of a mono block with a normalized
    /// autocorrelation (McLeod-style). Allocation-free after the first call.
    /// </summary>
    public sealed class PitchDetector
    {
        float[] nsdf = new float[0];

        /// <summary>Returns the pitch in Hz, or 0 when there is no clear pitch.</summary>
        public float Estimate(float[] block, int sampleRate, float minHz, float maxHz,
                              float minConfidence, out float confidence)
        {
            confidence = 0f;
            if (block == null || sampleRate <= 0 || maxHz <= minHz) return 0f;

            int minLag = Mathf.Max(2, Mathf.FloorToInt(sampleRate / maxHz));
            int maxLag = Mathf.CeilToInt(sampleRate / minHz);
            int window = Mathf.Min(1024, block.Length - maxLag - 2);
            if (window < 128 || maxLag <= minLag + 2) return 0f;

            // Use the newest samples: indices offset .. offset + window - 1 + maxLag + 1 = length - 1.
            int offset = block.Length - window - maxLag - 1;
            if (nsdf.Length < maxLag + 2) nsdf = new float[maxLag + 2];

            for (int lag = minLag - 1; lag <= maxLag + 1; lag++)
            {
                double acf = 0, e = 0;
                for (int i = 0; i < window; i++)
                {
                    float a = block[offset + i];
                    float b = block[offset + i + lag];
                    acf += a * b;
                    e += a * a + b * b;
                }
                nsdf[lag] = e > 1e-12 ? (float)(2.0 * acf / e) : 0f;
            }

            // Highest local maximum, then the FIRST peak that reaches 90 % of it
            // (avoids picking an octave too low).
            float globalMax = 0f;
            for (int lag = minLag; lag <= maxLag; lag++)
                if (IsPeak(lag) && nsdf[lag] > globalMax) globalMax = nsdf[lag];
            if (globalMax <= 0f) return 0f;

            int best = -1;
            for (int lag = minLag; lag <= maxLag; lag++)
                if (IsPeak(lag) && nsdf[lag] >= 0.9f * globalMax) { best = lag; break; }
            if (best < 0) return 0f;

            confidence = Mathf.Clamp01(nsdf[best]);
            if (confidence < minConfidence) return 0f;

            // Parabolic interpolation for sub-sample accuracy.
            float a0 = nsdf[best - 1], b0 = nsdf[best], c0 = nsdf[best + 1];
            float denom = a0 - 2f * b0 + c0;
            float shift = Mathf.Abs(denom) > 1e-9f ? 0.5f * (a0 - c0) / denom : 0f;
            float period = best + Mathf.Clamp(shift, -1f, 1f);
            return sampleRate / period;
        }

        bool IsPeak(int lag) => nsdf[lag] > nsdf[lag - 1] && nsdf[lag] >= nsdf[lag + 1];
    }
}
