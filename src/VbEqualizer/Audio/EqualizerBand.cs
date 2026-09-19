using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace VbEqualizer.Audio;

public sealed class EqualizerBand : INotifyPropertyChanged
{
    private const double MinFrequency = 20;
    private const double MaxFrequency = 20000;

    private double _frequency;
    private double _gainDb;

    public double Frequency
    {
        get => _frequency;
        set
        {
            double clamped = Math.Clamp(value, MinFrequency, MaxFrequency);
            if (_frequency == clamped) return;
            _frequency = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FrequencyText));
            FrequencyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Editable text form, e.g. "500", "0.5k", "1k", "16k".</summary>
    public string FrequencyText
    {
        get => FormatFrequency(_frequency);
        set
        {
            if (TryParseFrequency(value, out double hz))
                Frequency = hz;
            else
                OnPropertyChanged(); // reject: re-push the last valid text back to the UI
        }
    }

    public double GainDb
    {
        get => _gainDb;
        set
        {
            if (_gainDb == value) return;
            _gainDb = value;
            OnPropertyChanged();
            GainChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? FrequencyChanged;
    public event EventHandler? GainChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public EqualizerBand(double frequency)
    {
        _frequency = Math.Clamp(frequency, MinFrequency, MaxFrequency);
    }

    private static string FormatFrequency(double hz)
    {
        if (hz >= 1000)
        {
            double khz = hz / 1000.0;
            return khz % 1 == 0 ? $"{khz:0}k" : $"{khz:0.##}k";
        }
        return hz % 1 == 0 ? $"{hz:0}" : $"{hz:0.#}";
    }

    private static bool TryParseFrequency(string? text, out double hz)
    {
        hz = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        double multiplier = 1;
        if (text.EndsWith("k", StringComparison.OrdinalIgnoreCase))
        {
            multiplier = 1000;
            text = text[..^1];
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return false;

        hz = Math.Clamp(value * multiplier, MinFrequency, MaxFrequency);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
