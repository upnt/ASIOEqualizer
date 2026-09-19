using System;
using RNNoise.NET;

namespace VbEqualizer.Audio;

/// <summary>
/// Stationary+non-stationary noise suppression backed by RNNoise (a small recurrent neural
/// network). Generally cleaner on voice than SpeexDSP's spectral subtraction, but the model is
/// trained for a fixed 480-sample (10ms) frame at exactly 48kHz mono -- unlike NoiseSuppressor,
/// this cannot adapt its frame size to other sample rates, so Process() is a no-op pass-through
/// whenever the engine isn't running at RequiredSampleRate. One instance holds state for a
/// single audio channel.
/// </summary>
public sealed class RNNoiseSuppressor : IDisposable
{
    public const int RequiredSampleRate = 48000;
    private const int FrameSize = 480;

    private Denoiser? _denoiser;
    private readonly float[] _inputFrame = new float[FrameSize];
    private readonly float[] _outputFrame = new float[FrameSize];
    private int _pos;

    public bool IsEnabled { get; set; }

    public static bool IsSupported(double sampleRate) => (int)sampleRate == RequiredSampleRate;

    public float Process(float input, double sampleRate)
    {
        if (!IsEnabled || !IsSupported(sampleRate)) return input;

        _denoiser ??= new Denoiser();

        float output = _outputFrame[_pos];
        // RNNoise expects int16-scale PCM, not normalized -1..1 floats (same convention as Speex).
        _inputFrame[_pos] = input * 32768f;
        _pos++;

        if (_pos >= FrameSize)
        {
            _denoiser.Denoise(_inputFrame, false);
            Array.Copy(_inputFrame, _outputFrame, FrameSize);
            _pos = 0;
        }

        return output / 32768f;
    }

    public void Dispose() => _denoiser?.Dispose();
}
