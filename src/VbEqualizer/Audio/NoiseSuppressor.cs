using System;
using SpeexDSPSharp.Core;

namespace VbEqualizer.Audio;

/// <summary>
/// Stationary-noise suppression backed by libspeexdsp's spectral-subtraction preprocessor.
/// Speex only operates on fixed-size 16-bit PCM frames (20ms here), so incoming float samples
/// are accumulated into a frame buffer and swapped with the previous frame's processed output
/// one sample at a time -- this trades a constant ~20ms latency for a simple per-sample
/// Process() call that fits the rest of the (sample-by-sample) processing chain.
/// One instance holds state for a single audio channel.
/// </summary>
public sealed class NoiseSuppressor : IDisposable
{
    private const int FrameMs = 20;

    private SpeexDSPPreprocessor? _preprocessor;
    private short[] _inputFrame = Array.Empty<short>();
    private short[] _outputFrame = Array.Empty<short>();
    private int _pos;
    private int _cachedSampleRate = -1;
    private int _appliedSuppressDb = int.MinValue;

    public bool IsEnabled { get; set; }

    /// <summary>Attenuation applied to estimated noise, in dB (negative; 0 = no suppression).</summary>
    public int SuppressDb { get; set; } = -20;

    public float Process(float input, double sampleRate)
    {
        if (!IsEnabled) return input;

        EnsureInitialized((int)sampleRate);

        float output = _outputFrame[_pos] / 32768f;
        _inputFrame[_pos] = ToInt16(input);
        _pos++;

        if (_pos >= _inputFrame.Length)
        {
            _preprocessor!.Run(_inputFrame);
            Array.Copy(_inputFrame, _outputFrame, _inputFrame.Length);
            _pos = 0;
        }

        return output;
    }

    private void EnsureInitialized(int sampleRate)
    {
        if (sampleRate != _cachedSampleRate || _preprocessor == null)
        {
            _preprocessor?.Dispose();

            int frameSize = Math.Max(1, sampleRate * FrameMs / 1000);
            _preprocessor = new SpeexDSPPreprocessor(frameSize, sampleRate);
            _inputFrame = new short[frameSize];
            _outputFrame = new short[frameSize];
            _pos = 0;
            _cachedSampleRate = sampleRate;
            _appliedSuppressDb = int.MinValue; // force SET_NOISE_SUPPRESS below

            int denoise = 1;
            _preprocessor.Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_DENOISE, ref denoise);
        }

        if (SuppressDb != _appliedSuppressDb)
        {
            int suppress = SuppressDb;
            _preprocessor.Ctl(PreprocessorCtl.SPEEX_PREPROCESS_SET_NOISE_SUPPRESS, ref suppress);
            _appliedSuppressDb = SuppressDb;
        }
    }

    private static short ToInt16(float sample)
    {
        float scaled = sample * 32768f;
        if (scaled > short.MaxValue) return short.MaxValue;
        if (scaled < short.MinValue) return short.MinValue;
        return (short)scaled;
    }

    public void Dispose() => _preprocessor?.Dispose();
}
