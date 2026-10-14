namespace HoloWin.Core;

/// <summary>
/// A low-allocation streaming onset detector. Adapts to local noise floor,
/// retains a short pre-roll, and emits a fixed-length analysis window when
/// an impulse is detected.
/// </summary>
public sealed class StreamingTapDetector
{
    public double SampleRate { get; }
    public int ChannelCount { get; }
    public int AnalysisWindowSamples { get; }
    public int PreRollSamples { get; }
    public int WarmUpSamples { get; }
    public double NoiseFloorRMS { get; private set; }
    public long TotalSamples { get; private set; }

    private readonly double _initialNoiseFloorRMS;
    private readonly float[][] _preRoll;
    private float[][]? _capture;
    private int _captureOnsetOffset;
    private long _captureStreamIndex;
    private double _captureNoiseFloor;
    private int _refractorySamplesRemaining;
    private bool _adaptNoiseDuringRefractory;
    private int _warmUpSamplesRemaining;
    private readonly float[] _onsetFilterState;

    public StreamingTapDetector(
        double sampleRate,
        int channelCount,
        double analysisDuration = 0.090,
        double preRollDuration   = 0.012,
        double warmUpDuration    = 0.75,
        double initialNoiseFloorRMS = 0.0005)
    {
        SampleRate = sampleRate;
        ChannelCount = Math.Max(channelCount, 1);
        AnalysisWindowSamples = Math.Max((int)(sampleRate * analysisDuration), 1024);
        PreRollSamples        = Math.Max((int)(sampleRate * preRollDuration), 128);
        WarmUpSamples         = Math.Max((int)(sampleRate * warmUpDuration), 0);
        _initialNoiseFloorRMS = Math.Max(initialNoiseFloorRMS, 0.000_01);
        NoiseFloorRMS         = _initialNoiseFloorRMS;
        _warmUpSamplesRemaining = WarmUpSamples;
        _preRoll = new float[ChannelCount][];
        for (int i = 0; i < ChannelCount; i++) _preRoll[i] = Array.Empty<float>();
        _onsetFilterState = new float[4];
    }

    public void Reset()
    {
        TotalSamples = 0;
        NoiseFloorRMS = _initialNoiseFloorRMS;
        for (int i = 0; i < ChannelCount; i++) _preRoll[i] = Array.Empty<float>();
        _capture = null;
        _refractorySamplesRemaining = 0;
        _adaptNoiseDuringRefractory = false;
        _warmUpSamplesRemaining = WarmUpSamples;
        Array.Clear(_onsetFilterState);
    }

    public IReadOnlyList<DetectedTap> Process(float[][] incoming)
    {
        if (incoming.Length == 0) return Array.Empty<DetectedTap>();
        int frameCount = incoming.Min(c => c.Length);
        if (frameCount <= 0) return Array.Empty<DetectedTap>();

        var channels = NormalizeChannels(incoming, frameCount);
        var mono = MixDown(channels);
        var onsetSignal = LowPassForOnset(mono);
        var prevTotal = TotalSamples;
        TotalSamples += frameCount;

        var rms  = RootMeanSquare(onsetSignal);
        var peak = onsetSignal.Length == 0 ? 0 : onsetSignal.Max(s => Math.Abs((double)s));

        if (_warmUpSamplesRemaining > 0)
        {
            AdaptNoiseFloor(rms, isWarmUp: true);
            _warmUpSamplesRemaining = Math.Max(0, _warmUpSamplesRemaining - frameCount);
            AppendToPreRoll(channels);
            return Array.Empty<DetectedTap>();
        }

        if (_refractorySamplesRemaining > 0)
        {
            if (_adaptNoiseDuringRefractory)
                AdaptNoiseFloor(rms, isWarmUp: false);
            _refractorySamplesRemaining = Math.Max(0, _refractorySamplesRemaining - frameCount);
            if (_refractorySamplesRemaining == 0)
                _adaptNoiseDuringRefractory = false;
            AppendToPreRoll(channels);
            return Array.Empty<DetectedTap>();
        }

        if (_capture is not null)
        {
            AppendToCapture(channels);
            var completed = CompleteCaptureIfReady();
            if (completed is not null) return new[] { completed };
            return Array.Empty<DetectedTap>();
        }

        var rmsThreshold  = Math.Max(NoiseFloorRMS * 1.18, 0.0008);
        var peakThreshold = Math.Max(NoiseFloorRMS * 4.0, 0.007);
        var crest = peak / Math.Max(rms, 0.000_001);
        var strongSampleThreshold = Math.Max(peakThreshold, peak * 0.55);
        int strongSamples = 0;
        for (int i = 0; i < onsetSignal.Length; i++)
        {
            if (Math.Abs((double)onsetSignal[i]) >= strongSampleThreshold) strongSamples++;
        }
        var strongFraction = (double)strongSamples / Math.Max(frameCount, 1);
        var isImpulse = rms > rmsThreshold && peak > peakThreshold && crest > 2.0 && strongFraction < 0.20;

        if (isImpulse)
        {
            int crossing = 0;
            for (int i = 0; i < onsetSignal.Length; i++)
            {
                if (Math.Abs((double)onsetSignal[i]) >= peakThreshold) { crossing = i; break; }
            }
            _capture = ClonePreRoll();
            _captureOnsetOffset = (_preRoll.Length > 0 ? _preRoll[0].Length : 0) + crossing;
            _captureStreamIndex = prevTotal + crossing;
            _captureNoiseFloor  = NoiseFloorRMS;
            AppendToCapture(channels);
            var completed = CompleteCaptureIfReady();
            if (completed is not null) return new[] { completed };
        }
        else
        {
            AdaptNoiseFloor(rms, isWarmUp: false);
            AppendToPreRoll(channels);
        }

        return Array.Empty<DetectedTap>();
    }

    private float[][] ClonePreRoll()
    {
        var clone = new float[ChannelCount][];
        for (int i = 0; i < ChannelCount; i++)
        {
            clone[i] = new float[_preRoll[i].Length];
            Array.Copy(_preRoll[i], clone[i], _preRoll[i].Length);
        }
        return clone;
    }

    private float[][] NormalizeChannels(float[][] incoming, int frameCount)
    {
        var result = new float[ChannelCount][];
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            var src = incoming[Math.Min(ch, incoming.Length - 1)];
            var len = Math.Min(src.Length, frameCount);
            result[ch] = new float[frameCount];
            Array.Copy(src, result[ch], len);
        }
        return result;
    }

    private static float[] MixDown(float[][] channels)
    {
        if (channels.Length == 0) return Array.Empty<float>();
        if (channels.Length == 1) return channels[0];
        int count = channels[0].Length;
        var mono = new float[count];
        var scale = 1f / channels.Length;
        for (int ch = 0; ch < channels.Length; ch++)
            for (int i = 0; i < count; i++)
                mono[i] += channels[ch][i] * scale;
        return mono;
    }

    private static double RootMeanSquare(float[] values)
    {
        if (values.Length == 0) return 0;
        double sum = 0;
        for (int i = 0; i < values.Length; i++) sum += (double)values[i] * values[i];
        return Math.Sqrt(sum / values.Length);
    }

    private void AdaptNoiseFloor(double measuredRMS, bool isWarmUp)
    {
        if (!double.IsFinite(measuredRMS)) return;
        var measured = Math.Max(measuredRMS, 0.000_01);
        if (isWarmUp)
        {
            const double alpha = 0.14;
            NoiseFloorRMS = (1 - alpha) * NoiseFloorRMS + alpha * measured;
            return;
        }
        var capped = Math.Min(measured, Math.Max(NoiseFloorRMS * 3.5, 0.020));
        var a = capped > NoiseFloorRMS ? 0.06 : 0.025;
        NoiseFloorRMS = (1 - a) * NoiseFloorRMS + a * capped;
    }

    private float[] LowPassForOnset(float[] values)
    {
        if (values.Length == 0) return Array.Empty<float>();
        var cutoff = Math.Min(6000.0, SampleRate * 0.20);
        var alpha = (float)(1 - Math.Exp(-2 * Math.PI * cutoff / SampleRate));
        var result = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            var filtered = values[i];
            for (int s = 0; s < _onsetFilterState.Length; s++)
            {
                _onsetFilterState[s] += alpha * (filtered - _onsetFilterState[s]);
                filtered = _onsetFilterState[s];
            }
            result[i] = filtered;
        }
        return result;
    }

    private void AppendToPreRoll(float[][] channels)
    {
        for (int i = 0; i < ChannelCount; i++)
        {
            var src = channels[i];
            var newLen = _preRoll[i].Length + src.Length;
            if (newLen > PreRollSamples)
            {
                var keep = new float[PreRollSamples];
                int lenFromSrc = Math.Min(src.Length, PreRollSamples);
                int lenFromPrevious = PreRollSamples - lenFromSrc;
                Array.Copy(_preRoll[i], _preRoll[i].Length - lenFromPrevious, keep, 0, lenFromPrevious);
                Array.Copy(src, src.Length - lenFromSrc, keep, lenFromPrevious, lenFromSrc);
                _preRoll[i] = keep;
            }
            else
            {
                var combined = new float[newLen];
                Array.Copy(_preRoll[i], combined, _preRoll[i].Length);
                Array.Copy(src, 0, combined, _preRoll[i].Length, src.Length);
                _preRoll[i] = combined;
            }
        }
    }

    private void AppendToCapture(float[][] channels)
    {
        if (_capture is null) return;
        for (int i = 0; i < ChannelCount; i++)
        {
            var existing = _capture[i];
            var combined = new float[existing.Length + channels[i].Length];
            Array.Copy(existing, combined, existing.Length);
            Array.Copy(channels[i], 0, combined, existing.Length, channels[i].Length);
            _capture[i] = combined;
        }
    }

    private DetectedTap? CompleteCaptureIfReady()
    {
        if (_capture is null) return null;
        int len = _capture.Length > 0 ? _capture[0].Length : 0;
        if (len < AnalysisWindowSamples) return null;
        var trimmed = new float[ChannelCount][];
        for (int i = 0; i < ChannelCount; i++)
        {
            trimmed[i] = new float[AnalysisWindowSamples];
            Array.Copy(_capture[i], trimmed[i], AnalysisWindowSamples);
        }
        var ev = new DetectedTap(
            trimmed,
            Math.Min(_captureOnsetOffset, AnalysisWindowSamples - 1),
            _captureStreamIndex,
            _captureNoiseFloor);
        var accepted = ImpactEventGate.Accepts(ev, SampleRate);
        _capture = null;
        for (int i = 0; i < ChannelCount; i++) _preRoll[i] = Array.Empty<float>();
        _refractorySamplesRemaining = (int)(SampleRate * 0.14);
        _adaptNoiseDuringRefractory = !accepted;
        return accepted ? ev : null;
    }
}
