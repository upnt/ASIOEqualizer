namespace VbEqualizer.Audio;

public enum NoiseSuppressionEngine
{
    /// <summary>SpeexDSP spectral subtraction. Works at any sample rate; adjustable strength.</summary>
    Speex,

    /// <summary>RNNoise (recurrent neural network). Generally cleaner on voice, but only valid
    /// at 48kHz -- see RNNoiseSuppressor.RequiredSampleRate.</summary>
    RNNoise,
}
