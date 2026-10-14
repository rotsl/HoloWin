namespace HoloWin.Core;

/// <summary>
/// Radix-2 Cooley-Tukey FFT.
/// Pure managed code; no external native dependencies. Suitable for CPU
/// inference on commodity hardware. Performance is acceptable for 1024–4096
/// point transforms run on every tap candidate.
/// </summary>
public static class Radix2Fft
{
    public static int NextPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value) result <<= 1;
        return result;
    }

    /// <summary>
    /// Computes the one-sided power spectrum (length N/2 + 1) of <paramref name="input"/>
    /// using a Hann window. Returns normalized power per bin.
    /// </summary>
    public static double[] PowerSpectrum(ReadOnlySpan<double> input, int? requestedSize = null)
    {
        var desired = requestedSize ?? input.Length;
        var size = NextPowerOfTwo(Math.Max(desired, 2));
        var real  = new double[size];
        var imag  = new double[size];
        var copied = Math.Min(input.Length, size);

        if (copied > 1)
        {
            // Hann window
            var denom = (double)(copied - 1);
            for (int i = 0; i < copied; i++)
            {
                var w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / denom);
                real[i] = input[i] * w;
            }
        }

        // In-place bit reversal
        int j = 0;
        for (int i = 1; i < size; i++)
        {
            int bit = size >> 1;
            while ((j & bit) != 0)
            {
                j ^= bit;
                bit >>= 1;
            }
            j ^= bit;
            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        int length = 2;
        while (length <= size)
        {
            var angle = -2.0 * Math.PI / length;
            var baseReal = Math.Cos(angle);
            var baseImag = Math.Sin(angle);
            int start = 0;
            while (start < size)
            {
                var wReal = 1.0;
                var wImag = 0.0;
                int half = length >> 1;
                for (int off = 0; off < half; off++)
                {
                    int even = start + off;
                    int odd  = even + half;
                    var oddReal = real[odd] * wReal - imag[odd] * wImag;
                    var oddImag = real[odd] * wImag + imag[odd] * wReal;
                    real[odd]   = real[even] - oddReal;
                    imag[odd]   = imag[even] - oddImag;
                    real[even] += oddReal;
                    imag[even] += oddImag;

                    var nextReal = wReal * baseReal - wImag * baseImag;
                    wImag  = wReal * baseImag + wImag * baseReal;
                    wReal  = nextReal;
                }
                start += length;
            }
            length <<= 1;
        }

        int outputCount = size / 2 + 1;
        var result = new double[outputCount];
        for (int i = 0; i < outputCount; i++)
        {
            result[i] = (real[i] * real[i] + imag[i] * imag[i]) / size;
        }
        return result;
    }
}
