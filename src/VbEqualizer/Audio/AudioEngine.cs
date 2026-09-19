using System;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace VbEqualizer.Audio;

public sealed class AudioEngine : IDisposable
{
    private AsioOut? _asioIn;
    private AsioOut? _asioOut;
    private BufferedWaveProvider? _buffer;
    private float[]? _asioSampleBuffer;

    public EqualizerSampleProvider? Equalizer { get; private set; }
    public bool IsRunning { get; private set; }

    public event EventHandler<Exception>? EngineError;

    public static string[] GetAsioDriverNames() => AsioOut.GetDriverNames();

    /// <summary>Queries a driver's input channel names without leaving it open.</summary>
    public static string[] GetAsioInputChannelNames(string driverName)
    {
        using var probe = new AsioOut(driverName);
        var names = new string[probe.DriverInputChannelCount];
        for (int i = 0; i < names.Length; i++)
            names[i] = probe.AsioInputChannelName(i);
        return names;
    }

    /// <summary>Queries a driver's output channel names without leaving it open.</summary>
    public static string[] GetAsioOutputChannelNames(string driverName)
    {
        using var probe = new AsioOut(driverName);
        var names = new string[probe.DriverOutputChannelCount];
        for (int i = 0; i < names.Length; i++)
            names[i] = probe.AsioOutputChannelName(i);
        return names;
    }

    /// <summary>
    /// Read-only query of the sample rate the driver is currently configured for
    /// (whatever its own control panel has set). Never calls SetSampleRate -- NAudio's
    /// AsioDriverExt constructor only calls driver.Init() + driver.GetSampleRate().
    /// </summary>
    public static double GetCurrentSampleRate(string driverName)
    {
        var basicDriver = AsioDriver.GetAsioDriverByName(driverName);
        try
        {
            var driverExt = new AsioDriverExt(basicDriver);
            return driverExt.Capabilities.SampleRate;
        }
        finally
        {
            basicDriver.ReleaseComAsioDriver();
        }
    }

    /// <summary>
    /// All of the input driver's channels (from channel 0) -> EQ -> all of the output
    /// driver's channels. If the output has fewer channels than the input, only the first
    /// min(inputChannels, outputChannels) channels are captured/sent -- callers should warn
    /// about that using GetAsioInputChannelNames/GetAsioOutputChannelNames before calling this.
    /// </summary>
    public void Start(string inputDriverName, string outputDriverName,
        EqualizerBand[] bands, ChannelSettings[] channelSettings)
    {
        Stop();

        double inputRate = GetCurrentSampleRate(inputDriverName);
        double outputRate = GetCurrentSampleRate(outputDriverName);

        if (inputRate != outputRate)
        {
            throw new InvalidOperationException(
                $"入力ドライバ「{inputDriverName}」({inputRate:0} Hz)と" +
                $"出力ドライバ「{outputDriverName}」({outputRate:0} Hz)のサンプルレートが一致していません。" +
                "各ドライバの設定パネルで同じ値に揃えてください(このアプリはサンプルレートを変更しません)。");
        }

        int sampleRate = (int)inputRate;
        int channels = channelSettings.Length;

        _asioIn = new AsioOut(inputDriverName);

        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _buffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromMilliseconds(500),
        };

        _asioIn.AudioAvailable += OnAsioAudioAvailable;
        _asioIn.PlaybackStopped += OnAsioInStopped;

        var sampleProvider = _buffer.ToSampleProvider();
        Equalizer = new EqualizerSampleProvider(sampleProvider, bands, channelSettings);

        _asioOut = new AsioOut(outputDriverName);
        _asioOut.PlaybackStopped += OnAsioOutStopped;
        _asioOut.Init(Equalizer);

        _asioIn.InitRecordAndPlayback(null, channels, sampleRate);
        _asioIn.Play();
        _asioOut.Play();
        IsRunning = true;
    }

    private void OnAsioAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        int neededLength = e.SamplesPerBuffer * e.InputBuffers.Length;
        if (_asioSampleBuffer == null || _asioSampleBuffer.Length < neededLength)
            _asioSampleBuffer = new float[neededLength];

        int sampleCount = e.GetAsInterleavedSamples(_asioSampleBuffer);
        var bytes = new byte[sampleCount * sizeof(float)];
        Buffer.BlockCopy(_asioSampleBuffer, 0, bytes, 0, bytes.Length);
        _buffer?.AddSamples(bytes, 0, bytes.Length);
    }

    private void OnAsioInStopped(object? sender, StoppedEventArgs e) => HandleStopped(e);
    private void OnAsioOutStopped(object? sender, StoppedEventArgs e) => HandleStopped(e);

    private void HandleStopped(StoppedEventArgs e)
    {
        IsRunning = false;
        if (e.Exception != null)
            EngineError?.Invoke(this, e.Exception);
    }

    public void Stop()
    {
        if (_asioIn != null)
        {
            _asioIn.AudioAvailable -= OnAsioAudioAvailable;
            _asioIn.PlaybackStopped -= OnAsioInStopped;
            try { _asioIn.Stop(); } catch { /* driver may already be gone */ }
            _asioIn.Dispose();
            _asioIn = null;
        }

        if (_asioOut != null)
        {
            _asioOut.PlaybackStopped -= OnAsioOutStopped;
            try { _asioOut.Stop(); } catch { /* driver may already be gone */ }
            _asioOut.Dispose();
            _asioOut = null;
        }

        _buffer = null;
        Equalizer?.Dispose();
        Equalizer = null;
        IsRunning = false;
    }

    public void Dispose() => Stop();
}
