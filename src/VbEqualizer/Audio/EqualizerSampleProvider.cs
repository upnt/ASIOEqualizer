using System;
using NAudio.Wave;

namespace VbEqualizer.Audio;

/// <summary>
/// Wraps a source ISampleProvider with: high-pass -> noise suppression (Speex or RNNoise) ->
/// noise gate -> expander (all shared settings, but per-channel filter/envelope state) ->
/// per-band peaking EQ -> per-channel EQ-enable/volume/mute.
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider, IDisposable
{
    private const double Q = 1.0;
    private const double HighPassQ = 0.707; // Butterworth-ish, maximally flat passband

    private readonly ISampleProvider _source;
    private readonly EqualizerBand[] _bands;
    private readonly ChannelSettings[] _channelSettings;
    private readonly BiquadFilter[,] _filters; // [channel, band]
    private readonly BiquadFilter[] _highPassFilters; // [channel]
    private readonly NoiseSuppressor[] _noiseSuppressors; // [channel] -- Speex
    private readonly RNNoiseSuppressor[] _rnNoiseSuppressors; // [channel] -- RNNoise
    private readonly NoiseGate[] _noiseGates; // [channel]
    private readonly Expander[] _expanders; // [channel]
    private readonly object _lock = new();
    private readonly int _channels;
    private readonly double _sampleRate;
    private double _highPassFrequency = 100;
    private NoiseSuppressionEngine _noiseSuppressionEngine = NoiseSuppressionEngine.Speex;

    public bool HighPassEnabled { get; set; }

    public double HighPassFrequency
    {
        get => _highPassFrequency;
        set
        {
            _highPassFrequency = value;
            lock (_lock)
            {
                foreach (var f in _highPassFilters)
                    f.SetHighPass(_sampleRate, _highPassFrequency, HighPassQ);
            }
        }
    }

    public bool NoiseSuppressionEnabled { get; set; }

    /// <summary>Attenuation applied to estimated stationary noise, in dB (negative; 0 = off).
    /// Speex-only -- RNNoise has no equivalent knob.</summary>
    public int NoiseSuppressionDb
    {
        get => _noiseSuppressors[0].SuppressDb;
        set { foreach (var n in _noiseSuppressors) n.SuppressDb = value; }
    }

    public NoiseSuppressionEngine NoiseSuppressionEngine
    {
        get => _noiseSuppressionEngine;
        set => _noiseSuppressionEngine = value;
    }

    /// <summary>True if RNNoise can actually run at this engine's sample rate (it's fixed to 48kHz).</summary>
    public bool IsRNNoiseSupported => RNNoiseSuppressor.IsSupported(_sampleRate);

    public bool NoiseGateEnabled
    {
        get => _noiseGates[0].IsEnabled;
        set { foreach (var g in _noiseGates) g.IsEnabled = value; }
    }

    public double NoiseGateThresholdDb
    {
        get => _noiseGates[0].ThresholdDb;
        set { foreach (var g in _noiseGates) g.ThresholdDb = value; }
    }

    public bool ExpanderEnabled
    {
        get => _expanders[0].IsEnabled;
        set { foreach (var e in _expanders) e.IsEnabled = value; }
    }

    public double ExpanderThresholdDb
    {
        get => _expanders[0].ThresholdDb;
        set { foreach (var e in _expanders) e.ThresholdDb = value; }
    }

    public double ExpanderRatio
    {
        get => _expanders[0].Ratio;
        set { foreach (var e in _expanders) e.Ratio = value; }
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>channelSettings.Length must equal source.WaveFormat.Channels.</summary>
    public EqualizerSampleProvider(ISampleProvider source, EqualizerBand[] bands, ChannelSettings[] channelSettings)
    {
        _source = source;
        _bands = bands;
        _channelSettings = channelSettings;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;

        if (_channelSettings.Length != _channels)
            throw new ArgumentException(
                $"channelSettings has {_channelSettings.Length} entries but the source has {_channels} channels.");

        _filters = new BiquadFilter[_channels, _bands.Length];
        for (int c = 0; c < _channels; c++)
            for (int b = 0; b < _bands.Length; b++)
                _filters[c, b] = new BiquadFilter();

        _highPassFilters = new BiquadFilter[_channels];
        _noiseSuppressors = new NoiseSuppressor[_channels];
        _rnNoiseSuppressors = new RNNoiseSuppressor[_channels];
        _noiseGates = new NoiseGate[_channels];
        _expanders = new Expander[_channels];
        for (int c = 0; c < _channels; c++)
        {
            _highPassFilters[c] = new BiquadFilter();
            _highPassFilters[c].SetHighPass(_sampleRate, _highPassFrequency, HighPassQ);
            // Gating which engine runs is done in Read() via NoiseSuppressionEnabled/Engine,
            // so both are simply always "armed" here.
            _noiseSuppressors[c] = new NoiseSuppressor { IsEnabled = true };
            _rnNoiseSuppressors[c] = new RNNoiseSuppressor { IsEnabled = true };
            _noiseGates[c] = new NoiseGate();
            _expanders[c] = new Expander();
        }

        RecalculateAll();

        foreach (var band in _bands)
        {
            band.GainChanged += (_, _) => RecalculateBand(band);
            band.FrequencyChanged += (_, _) => RecalculateBand(band);
        }
    }

    private void RecalculateAll()
    {
        lock (_lock)
        {
            for (int b = 0; b < _bands.Length; b++)
                for (int c = 0; c < _channels; c++)
                    _filters[c, b].SetPeakingEq(_sampleRate, _bands[b].Frequency, Q, _bands[b].GainDb);
        }
    }

    private void RecalculateBand(EqualizerBand band)
    {
        int index = Array.IndexOf(_bands, band);
        if (index < 0) return;

        lock (_lock)
        {
            for (int c = 0; c < _channels; c++)
                _filters[c, index].SetPeakingEq(_sampleRate, band.Frequency, Q, band.GainDb);
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = _source.Read(buffer, offset, count);

        lock (_lock)
        {
            for (int i = 0; i < samplesRead; i++)
            {
                int channel = i % _channels;
                var settings = _channelSettings[channel];
                float sample = buffer[offset + i];

                if (HighPassEnabled)
                    sample = _highPassFilters[channel].Process(sample);

                if (NoiseSuppressionEnabled)
                {
                    // RNNoise only works at exactly 48kHz -- silently fall back to Speex
                    // (which adapts to any rate) rather than passing audio through unprocessed.
                    bool useRNNoise = _noiseSuppressionEngine == NoiseSuppressionEngine.RNNoise &&
                                       RNNoiseSuppressor.IsSupported(_sampleRate);
                    sample = useRNNoise
                        ? _rnNoiseSuppressors[channel].Process(sample, _sampleRate)
                        : _noiseSuppressors[channel].Process(sample, _sampleRate);
                }

                sample = _noiseGates[channel].Process(sample, _sampleRate);
                sample = _expanders[channel].Process(sample, _sampleRate);

                if (settings.EqEnabled)
                {
                    for (int b = 0; b < _bands.Length; b++)
                        sample = _filters[channel, b].Process(sample);
                }

                float gain = settings.IsMuted ? 0f : (float)(settings.VolumePercent / 100.0);
                buffer[offset + i] = sample * gain;
            }
        }

        return samplesRead;
    }

    public void Dispose()
    {
        foreach (var n in _noiseSuppressors)
            n.Dispose();
        foreach (var n in _rnNoiseSuppressors)
            n.Dispose();
    }
}
