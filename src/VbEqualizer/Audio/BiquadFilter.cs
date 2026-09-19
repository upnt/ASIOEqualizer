using System;

namespace VbEqualizer.Audio;

/// <summary>
/// RBJ Audio EQ Cookbook peaking filter, Direct Form I biquad.
/// One instance holds state for a single audio channel.
/// </summary>
public sealed class BiquadFilter
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;

    public void SetPeakingEq(double sampleRate, double centerFrequency, double q, double dbGain)
    {
        double a = Math.Pow(10, dbGain / 40.0);
        double w0 = 2 * Math.PI * centerFrequency / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2 * q);

        double b0 = 1 + alpha * a;
        double b1 = -2 * cosW0;
        double b2 = 1 - alpha * a;
        double a0 = 1 + alpha / a;
        double a1 = -2 * cosW0;
        double a2 = 1 - alpha / a;

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public void SetHighPass(double sampleRate, double cutoffFrequency, double q)
    {
        double w0 = 2 * Math.PI * cutoffFrequency / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2 * q);

        double b0 = (1 + cosW0) / 2;
        double b1 = -(1 + cosW0);
        double b2 = (1 + cosW0) / 2;
        double a0 = 1 + alpha;
        double a1 = -2 * cosW0;
        double a2 = 1 - alpha;

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    /// <summary>
    /// Analytic magnitude response of the currently-set coefficients, in dB, at an arbitrary
    /// frequency -- evaluates H(z) at z = e^(jw) rather than running any samples through the
    /// filter. Used to draw the EQ curve; sampleRate must match whatever Set* call configured it.
    /// </summary>
    public double GetMagnitudeDb(double sampleRate, double evalFrequency)
    {
        double w = 2 * Math.PI * evalFrequency / sampleRate;
        double cosW = Math.Cos(w), sinW = Math.Sin(w);
        double cos2W = Math.Cos(2 * w), sin2W = Math.Sin(2 * w);

        double numReal = _b0 + _b1 * cosW + _b2 * cos2W;
        double numImag = -(_b1 * sinW + _b2 * sin2W);
        double denReal = 1 + _a1 * cosW + _a2 * cos2W;
        double denImag = -(_a1 * sinW + _a2 * sin2W);

        double magnitude = Math.Sqrt((numReal * numReal + numImag * numImag) /
                                      (denReal * denReal + denImag * denImag));
        return 20 * Math.Log10(Math.Max(magnitude, 1e-9));
    }

    public float Process(float input)
    {
        double x0 = input;
        double y0 = _b0 * x0 + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;

        _x2 = _x1;
        _x1 = x0;
        _y2 = _y1;
        _y1 = y0;

        return (float)y0;
    }
}
