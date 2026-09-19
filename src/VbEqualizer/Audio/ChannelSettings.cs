using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VbEqualizer.Audio;

public sealed class ChannelSettings : INotifyPropertyChanged
{
    private bool _eqEnabled = true;
    private double _volumePercent = 100;
    private bool _isMuted;

    public int Index { get; }
    public string Name { get; }

    public bool EqEnabled
    {
        get => _eqEnabled;
        set { if (_eqEnabled == value) return; _eqEnabled = value; OnPropertyChanged(); }
    }

    public double VolumePercent
    {
        get => _volumePercent;
        set { if (_volumePercent == value) return; _volumePercent = value; OnPropertyChanged(); }
    }

    public bool IsMuted
    {
        get => _isMuted;
        set { if (_isMuted == value) return; _isMuted = value; OnPropertyChanged(); }
    }

    public ChannelSettings(int index, string name)
    {
        Index = index;
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
