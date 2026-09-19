using System;

namespace VbEqualizer.Audio;

/// <summary>
/// Downward expander: below the threshold, gain is reduced proportionally to how far
/// below (scaled by Ratio) rather than snapping fully shut like NoiseGate. Smoothed with
/// attack/release so the reduction fades in/out. One instance holds state for a single channel.
/// </summary>
public sealed class Expander
{
    private double _gain = 1.0;
    private double _cachedSampleRate = -1;
    private double _attackMs = 5;
    private double _releaseMs = 150;
    private double _attackCoeff;
    private double _releaseCoeff;

    public bool IsEnabled { get; set; }
    public double ThresholdDb { get; set; } = -40;

    /// <summary>1 = no effect. 2 = every 2dB below threshold becomes 1dB quieter. Higher = more aggressive.</summary>
    public double Ratio { get; set; } = 2.0;

    public double AttackMs
    {
        get => _attackMs;
        set { _attackMs = value; _cachedSampleRate = -1; }
    }

    public double ReleaseMs
    {
        get => _releaseMs;
        set { _releaseMs = value; _cachedSampleRate = -1; }
    }

    public float Process(float input, double sampleRate)
    {
        if (!IsEnabled) return input;

        if (sampleRate != _cachedSampleRate)
        {
            _attackCoeff = Math.Exp(-1.0 / (sampleRate * (_attackMs / 1000.0)));
            _releaseCoeff = Math.Exp(-1.0 / (sampleRate * (_releaseMs / 1000.0)));
            _cachedSampleRate = sampleRate;
        }

        double level = Math.Abs(input);
        double levelDb = level > 1e-8 ? 20 * Math.Log10(level) : -160;

        double targetGainDb = levelDb < ThresholdDb
            ? -(ThresholdDb - levelDb) * (1 - 1.0 / Ratio)
            : 0;
        double targetGain = Math.Pow(10, targetGainDb / 20);

        double coeff = targetGain < _gain ? _attackCoeff : _releaseCoeff;
        _gain = targetGain + (_gain - targetGain) * coeff;

        return (float)(input * _gain);
    }
}
