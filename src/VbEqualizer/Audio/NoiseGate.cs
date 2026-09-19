using System;

namespace VbEqualizer.Audio;

/// <summary>
/// Attenuates the signal toward silence whenever its level drops below a threshold,
/// with separate attack/release smoothing so the gain doesn't click open/closed.
/// One instance holds state for a single audio channel.
/// </summary>
public sealed class NoiseGate
{
    private double _gain;
    private double _cachedSampleRate = -1;
    private double _attackMs = 5;
    private double _releaseMs = 150;
    private double _attackCoeff;
    private double _releaseCoeff;

    public bool IsEnabled { get; set; }
    public double ThresholdDb { get; set; } = -50;

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
        double targetGain = levelDb > ThresholdDb ? 1.0 : 0.0;

        double coeff = targetGain > _gain ? _attackCoeff : _releaseCoeff;
        _gain = targetGain + (_gain - targetGain) * coeff;

        return (float)(input * _gain);
    }
}
