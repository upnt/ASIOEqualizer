namespace VbEqualizer.Settings;

/// <summary>Everything persisted to settings.json. All fields optional/best-effort on load --
/// a driver that no longer exists or a channel whose name changed is simply skipped.</summary>
public sealed class AppSettings
{
    public string? InputDriver { get; set; }
    public string? OutputDriver { get; set; }

    public BandSettings[] Bands { get; set; } = System.Array.Empty<BandSettings>();

    public bool HighPassEnabled { get; set; }
    public double HighPassFrequency { get; set; } = 100;

    public bool NoiseSuppressionEnabled { get; set; }
    public int NoiseSuppressionDb { get; set; } = -20;

    /// <summary>"Speex" or "RNNoise" (see VbEqualizer.Audio.NoiseSuppressionEngine).</summary>
    public string NoiseSuppressionEngine { get; set; } = "Speex";

    public bool NoiseGateEnabled { get; set; }
    public double NoiseGateThresholdDb { get; set; } = -50;

    public bool ExpanderEnabled { get; set; }
    public double ExpanderThresholdDb { get; set; } = -40;
    public double ExpanderRatio { get; set; } = 2;

    public ChannelSettingsData[] Channels { get; set; } = System.Array.Empty<ChannelSettingsData>();

    /// <summary>If true, the engine is started automatically once these settings are applied at launch.</summary>
    public bool AutoStartEngine { get; set; }

    /// <summary>If true, the app starts hidden in the system tray instead of showing its window.</summary>
    public bool StartMinimizedToTray { get; set; }
}

public sealed class BandSettings
{
    public double Frequency { get; set; }
    public double GainDb { get; set; }
}

/// <summary>Matched back to a live ChannelSettings by Name at load time (channel order/count
/// can change when the selected ASIO driver changes).</summary>
public sealed class ChannelSettingsData
{
    public string Name { get; set; } = "";
    public bool EqEnabled { get; set; } = true;
    public double VolumePercent { get; set; } = 100;
    public bool IsMuted { get; set; }
}
