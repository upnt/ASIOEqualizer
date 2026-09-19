using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VbEqualizer.Audio;
using VbEqualizer.Controls;
using VbEqualizer.Settings;

namespace VbEqualizer;

public partial class MainWindow : Window
{
    private static readonly double[] BandFrequencies =
        { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    private sealed record NoiseEngineOption(string Label, NoiseSuppressionEngine Value);

    private static readonly NoiseEngineOption[] NoiseEngineOptions =
    {
        new("Speex (全サンプルレート対応)", NoiseSuppressionEngine.Speex),
        new($"RNNoise ({RNNoiseSuppressor.RequiredSampleRate}Hzのみ、高品質)", NoiseSuppressionEngine.RNNoise),
    };

    private readonly AudioEngine _engine = new();
    private readonly EqualizerBand[] _bands;
    private readonly ObservableCollection<ChannelSettings> _channelSettings = new();
    private string[] _driverNames = Array.Empty<string>();
    private string _settingsFilePath = SettingsStore.DefaultPath;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isExiting;

    public MainWindow()
    {
        InitializeComponent();

        _bands = BandFrequencies.Select(f => new EqualizerBand(f)).ToArray();
        BandsItemsControl.ItemsSource = _bands;
        ChannelSettingsItemsControl.ItemsSource = _channelSettings;
        EqCurveHost.Child = new EqCurveControl(_bands);

        NoiseSuppressionEngineCombo.ItemsSource = NoiseEngineOptions;
        NoiseSuppressionEngineCombo.DisplayMemberPath = nameof(NoiseEngineOption.Label);
        NoiseSuppressionEngineCombo.SelectedValuePath = nameof(NoiseEngineOption.Value);
        NoiseSuppressionEngineCombo.SelectedIndex = 0;

        _engine.EngineError += OnEngineError;

        // Closing the window (the "X" button) just hides it to the tray so processing keeps
        // running in the background; only the tray menu's "終了" actually exits the app.
        Closing += (_, e) =>
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            try { SaveSettings(); } catch { /* best effort on close */ }
            _engine.Dispose();
            _notifyIcon?.Dispose();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) Hide();
        };

        _settingsFilePath = SettingsLocationStore.Load() ?? SettingsStore.DefaultPath;
        UpdateSettingsPathDisplay();

        LoadDrivers();
        RunAtWindowsStartupMenuItem.IsChecked = StartupRegistration.IsEnabled;
        ApplySettings(SettingsStore.Load(_settingsFilePath));

        InitializeTrayIcon();
        if (StartMinimizedToTrayMenuItem.IsChecked)
            Loaded += (_, _) => Hide();
    }

    private void UpdateSettingsPathDisplay() => SettingsPathMenuItem.Header = $"設定ファイル: {_settingsFilePath}";

    private void InitializeTrayIcon()
    {
        string exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("開く", null, (_, _) => ShowFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) =>
        {
            _isExiting = true;
            Close();
        });

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath),
            Text = "マイクイコライザ",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    internal void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void UpdateTrayText(string status)
    {
        if (_notifyIcon == null) return;
        string text = $"マイクイコライザ - {status}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private void LoadDrivers()
    {
        string? previousInputDriver = InputDriverCombo.SelectedItem as string;
        string? previousOutputDriver = OutputDriverCombo.SelectedItem as string;

        try
        {
            _driverNames = AudioEngine.GetAsioDriverNames();
        }
        catch
        {
            _driverNames = Array.Empty<string>();
        }

        InputDriverCombo.ItemsSource = _driverNames;
        OutputDriverCombo.ItemsSource = _driverNames;

        InputDriverCombo.SelectedIndex = SelectPreferredDriverIndex(previousInputDriver);
        OutputDriverCombo.SelectedIndex = SelectPreferredDriverIndex(previousOutputDriver);

        if (_driverNames.Length == 0)
            StatusText.Text = "ASIOドライバが見つかりません。オーディオインターフェースのASIOドライバをインストールしてください。";
    }

    private int SelectPreferredDriverIndex(string? previousName)
    {
        if (_driverNames.Length == 0) return -1;
        if (previousName != null)
        {
            int keep = Array.IndexOf(_driverNames, previousName);
            if (keep >= 0) return keep;
        }
        return 0;
    }

    private void InputDriverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSampleRateText(InputDriverCombo, InputSampleRateText);
        RefreshChannelSettings();
        UpdateNoiseSuppressionUiState();
    }

    private void OutputDriverCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSampleRateText(OutputDriverCombo, OutputSampleRateText);
        RefreshChannelSettings();
    }

    private static void RefreshSampleRateText(ComboBox driverCombo, TextBlock label)
    {
        if (driverCombo.SelectedItem is not string driverName)
        {
            label.Text = string.Empty;
            return;
        }

        try
        {
            double rate = AudioEngine.GetCurrentSampleRate(driverName);
            label.Text = $"現在: {rate:0} Hz";
        }
        catch (Exception ex)
        {
            label.Text = $"取得失敗: {ex.Message}";
        }
    }

    /// <summary>
    /// Regenerates the per-channel settings list to min(inputChannels, outputChannels),
    /// reusing existing settings by index so in-progress tweaks survive a driver refresh.
    /// </summary>
    private void RefreshChannelSettings()
    {
        if (InputDriverCombo.SelectedItem is not string inputDriver ||
            OutputDriverCombo.SelectedItem is not string outputDriver)
        {
            _channelSettings.Clear();
            ChannelCountText.Text = string.Empty;
            return;
        }

        string[] inputNames;
        int outputCount;
        try
        {
            inputNames = AudioEngine.GetAsioInputChannelNames(inputDriver);
            outputCount = AudioEngine.GetAsioOutputChannelNames(outputDriver).Length;
        }
        catch (Exception ex)
        {
            _channelSettings.Clear();
            StatusText.Text = $"チャンネル情報の取得に失敗: {ex.Message}";
            return;
        }

        int effectiveChannels = Math.Min(inputNames.Length, outputCount);

        ChannelCountText.Text = inputNames.Length > outputCount
            ? $"入力 {inputNames.Length}ch / 出力 {outputCount}ch → {effectiveChannels}ch のみ使用されます"
            : $"入力 {inputNames.Length}ch / 出力 {outputCount}ch";

        var previous = _channelSettings.ToArray();
        _channelSettings.Clear();
        for (int i = 0; i < effectiveChannels; i++)
        {
            var settings = new ChannelSettings(i, inputNames[i]);
            if (i < previous.Length)
            {
                settings.EqEnabled = previous[i].EqEnabled;
                settings.VolumePercent = previous[i].VolumePercent;
                settings.IsMuted = previous[i].IsMuted;
            }
            _channelSettings.Add(settings);
        }
    }

    /// <summary>
    /// Restores everything from a saved settings file: driver selection (if it still exists),
    /// band gains/frequencies, per-channel volume/mute/EQ (matched by channel name), and the
    /// high-pass/noise-suppression/noise-gate/expander controls. Missing/stale entries are
    /// skipped rather than treated as errors.
    /// </summary>
    private void ApplySettings(AppSettings settings)
    {
        if (settings.InputDriver != null && Array.IndexOf(_driverNames, settings.InputDriver) >= 0)
            InputDriverCombo.SelectedItem = settings.InputDriver;
        if (settings.OutputDriver != null && Array.IndexOf(_driverNames, settings.OutputDriver) >= 0)
            OutputDriverCombo.SelectedItem = settings.OutputDriver;

        for (int i = 0; i < settings.Bands.Length && i < _bands.Length; i++)
        {
            _bands[i].Frequency = settings.Bands[i].Frequency;
            _bands[i].GainDb = settings.Bands[i].GainDb;
        }

        foreach (var saved in settings.Channels)
        {
            var channel = _channelSettings.FirstOrDefault(c => c.Name == saved.Name);
            if (channel == null) continue;
            channel.EqEnabled = saved.EqEnabled;
            channel.VolumePercent = saved.VolumePercent;
            channel.IsMuted = saved.IsMuted;
        }

        HighPassCheckBox.IsChecked = settings.HighPassEnabled;
        HighPassSlider.Value = settings.HighPassFrequency;
        NoiseSuppressionCheckBox.IsChecked = settings.NoiseSuppressionEnabled;
        NoiseSuppressionSlider.Value = settings.NoiseSuppressionDb;
        if (Enum.TryParse<NoiseSuppressionEngine>(settings.NoiseSuppressionEngine, out var savedEngine))
        {
            int index = Array.FindIndex(NoiseEngineOptions, o => o.Value == savedEngine);
            if (index >= 0) NoiseSuppressionEngineCombo.SelectedIndex = index;
        }
        UpdateNoiseSuppressionUiState();
        NoiseGateCheckBox.IsChecked = settings.NoiseGateEnabled;
        NoiseGateSlider.Value = settings.NoiseGateThresholdDb;
        ExpanderCheckBox.IsChecked = settings.ExpanderEnabled;
        ExpanderThresholdSlider.Value = settings.ExpanderThresholdDb;
        ExpanderRatioSlider.Value = settings.ExpanderRatio;
        AutoStartEngineMenuItem.IsChecked = settings.AutoStartEngine;
        StartMinimizedToTrayMenuItem.IsChecked = settings.StartMinimizedToTray;

        if (settings.AutoStartEngine &&
            InputDriverCombo.SelectedItem is string &&
            OutputDriverCombo.SelectedItem is string)
        {
            StartStopButton_Click(this, new RoutedEventArgs());
        }
    }

    private AppSettings BuildCurrentSettings() => new()
    {
        InputDriver = InputDriverCombo.SelectedItem as string,
        OutputDriver = OutputDriverCombo.SelectedItem as string,
        Bands = _bands.Select(b => new BandSettings { Frequency = b.Frequency, GainDb = b.GainDb }).ToArray(),
        HighPassEnabled = HighPassCheckBox.IsChecked == true,
        HighPassFrequency = HighPassSlider.Value,
        NoiseSuppressionEnabled = NoiseSuppressionCheckBox.IsChecked == true,
        NoiseSuppressionDb = (int)NoiseSuppressionSlider.Value,
        NoiseSuppressionEngine = (NoiseSuppressionEngineCombo.SelectedValue as NoiseSuppressionEngine?)?.ToString()
            ?? nameof(Audio.NoiseSuppressionEngine.Speex),
        NoiseGateEnabled = NoiseGateCheckBox.IsChecked == true,
        NoiseGateThresholdDb = NoiseGateSlider.Value,
        ExpanderEnabled = ExpanderCheckBox.IsChecked == true,
        ExpanderThresholdDb = ExpanderThresholdSlider.Value,
        ExpanderRatio = ExpanderRatioSlider.Value,
        Channels = _channelSettings.Select(c => new ChannelSettingsData
        {
            Name = c.Name,
            EqEnabled = c.EqEnabled,
            VolumePercent = c.VolumePercent,
            IsMuted = c.IsMuted,
        }).ToArray(),
        AutoStartEngine = AutoStartEngineMenuItem.IsChecked,
        StartMinimizedToTray = StartMinimizedToTrayMenuItem.IsChecked,
    };

    private void SaveSettings() => SettingsStore.Save(BuildCurrentSettings(), _settingsFilePath);

    /// <summary>Prompts for a destination file and saves the current settings there.</summary>
    private void SaveSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "設定を保存",
            Filter = "設定ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            FileName = _settingsFilePath,
        };

        if (dialog.ShowDialog(this) != true) return;

        string targetPath = dialog.FileName;
        try
        {
            SettingsStore.Save(BuildCurrentSettings(), targetPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"設定の保存に失敗しました。\n{ex.Message}", "マイクイコライザ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settingsFilePath = targetPath;
        UpdateSettingsPathDisplay();
        StatusText.Text = $"設定を保存しました: {_settingsFilePath}";
    }

    /// <summary>Prompts for an existing settings file and loads it immediately (like switching profile).</summary>
    private void LoadSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "設定を読み込む",
            Filter = "設定ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            FileName = _settingsFilePath,
        };

        if (dialog.ShowDialog(this) != true) return;

        if (!SettingsStore.TryLoad(dialog.FileName, out var settings))
        {
            MessageBox.Show(
                "設定ファイルの読み込みに失敗しました。ファイルが壊れているか、形式が正しくない可能性があります。",
                "マイクイコライザ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settingsFilePath = dialog.FileName;
        UpdateSettingsPathDisplay();
        StatusText.Text = $"設定を読み込みました: {_settingsFilePath}";
        ApplySettings(settings);
    }

    /// <summary>Prompts for a settings file and registers it as the one to load automatically
    /// at the next launch (it is not loaded into the UI now -- only "設定を読み込む" does that).</summary>
    private void SetStartupSettingsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "起動時に読み込む設定ファイルを選択",
            Filter = "設定ファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
            FileName = _settingsFilePath,
        };

        if (dialog.ShowDialog(this) != true) return;

        _settingsFilePath = dialog.FileName;
        UpdateSettingsPathDisplay();
        SettingsLocationStore.Save(_settingsFilePath);
        StatusText.Text = $"起動時にこの設定ファイルを読み込むように設定しました: {_settingsFilePath}";
    }

    private void RunAtWindowsStartupMenuItem_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            StartupRegistration.SetEnabled(RunAtWindowsStartupMenuItem.IsChecked);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"スタートアップ登録の変更に失敗しました。\n{ex.Message}", "マイクイコライザ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadDrivers();

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_engine.IsRunning)
        {
            _engine.Stop();
            StartStopButton.Content = "開始";
            StatusText.Text = "停止中";
            SetControlsEnabled(true);
            UpdateTrayText("停止中");
            UpdateNoiseSuppressionUiState();
            return;
        }

        if (InputDriverCombo.SelectedItem is not string inputDriver)
        {
            MessageBox.Show("入力ASIOドライバを選択してください。", "マイクイコライザ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (OutputDriverCombo.SelectedItem is not string outputDriver)
        {
            MessageBox.Show("出力ASIOドライバを選択してください。", "マイクイコライザ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_channelSettings.Count == 0)
        {
            MessageBox.Show("使用できるチャンネルがありません。", "マイクイコライザ",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            int inputCount = AudioEngine.GetAsioInputChannelNames(inputDriver).Length;
            int outputCount = AudioEngine.GetAsioOutputChannelNames(outputDriver).Length;
            if (inputCount > outputCount)
            {
                MessageBox.Show(
                    $"出力ドライバのチャンネル数({outputCount}ch)が入力({inputCount}ch)より少ないため、" +
                    $"先頭の{outputCount}chのみ送信されます。超過する{inputCount - outputCount}chは送信されません。",
                    "マイクイコライザ", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            var channelSettingsArray = _channelSettings.ToArray();
            _engine.Start(inputDriver, outputDriver, _bands, channelSettingsArray);

            _engine.Equalizer!.HighPassEnabled = HighPassCheckBox.IsChecked == true;
            _engine.Equalizer.HighPassFrequency = HighPassSlider.Value;
            _engine.Equalizer.NoiseSuppressionEnabled = NoiseSuppressionCheckBox.IsChecked == true;
            _engine.Equalizer.NoiseSuppressionDb = (int)NoiseSuppressionSlider.Value;
            if (NoiseSuppressionEngineCombo.SelectedValue is NoiseSuppressionEngine engine)
                _engine.Equalizer.NoiseSuppressionEngine = engine;
            _engine.Equalizer.NoiseGateEnabled = NoiseGateCheckBox.IsChecked == true;
            _engine.Equalizer.NoiseGateThresholdDb = NoiseGateSlider.Value;
            _engine.Equalizer.ExpanderEnabled = ExpanderCheckBox.IsChecked == true;
            _engine.Equalizer.ExpanderThresholdDb = ExpanderThresholdSlider.Value;
            _engine.Equalizer.ExpanderRatio = ExpanderRatioSlider.Value;

            StartStopButton.Content = "停止";
            StatusText.Text = $"動作中: {inputDriver} → {outputDriver} ({channelSettingsArray.Length}ch)";
            SetControlsEnabled(false);
            UpdateTrayText($"動作中 ({inputDriver} → {outputDriver})");
            UpdateNoiseSuppressionUiState();
        }
        catch (Exception ex)
        {
            // Covers the "no common sample rate" case too -- we refuse to start rather
            // than send mismatched-rate audio.
            MessageBox.Show(ex.Message, "マイクイコライザ", MessageBoxButton.OK, MessageBoxImage.Error);
            _engine.Stop();
            StatusText.Text = "エラーにより開始できませんでした";
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        InputDriverCombo.IsEnabled = enabled;
        OutputDriverCombo.IsEnabled = enabled;
        RefreshButton.IsEnabled = enabled;
    }

    private void HighPassCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_engine.Equalizer != null)
            _engine.Equalizer.HighPassEnabled = HighPassCheckBox.IsChecked == true;
    }

    private void HighPassSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HighPassLabel == null) return;

        HighPassLabel.Text = $"{e.NewValue:0} Hz";
        if (_engine.Equalizer != null)
            _engine.Equalizer.HighPassFrequency = e.NewValue;
    }

    private void NoiseSuppressionCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_engine.Equalizer != null)
            _engine.Equalizer.NoiseSuppressionEnabled = NoiseSuppressionCheckBox.IsChecked == true;
    }

    private void NoiseSuppressionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NoiseSuppressionLabel == null) return;

        NoiseSuppressionLabel.Text = $"{e.NewValue:0} dB";
        if (_engine.Equalizer != null)
            _engine.Equalizer.NoiseSuppressionDb = (int)e.NewValue;
    }

    private void NoiseSuppressionEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateNoiseSuppressionUiState();
        if (_engine.Equalizer != null && NoiseSuppressionEngineCombo.SelectedValue is NoiseSuppressionEngine engine)
            _engine.Equalizer.NoiseSuppressionEngine = engine;
    }

    /// <summary>Speex's strength slider has no RNNoise equivalent, and RNNoise only actually runs
    /// at 48kHz -- reflect both in the UI using whatever sample rate the selected input driver
    /// (or, once running, the live engine) reports.</summary>
    private void UpdateNoiseSuppressionUiState()
    {
        bool isRNNoise = NoiseSuppressionEngineCombo.SelectedValue is NoiseSuppressionEngine.RNNoise;
        NoiseSuppressionSlider.IsEnabled = !isRNNoise;

        bool? supported;
        if (_engine.Equalizer != null)
        {
            supported = _engine.Equalizer.IsRNNoiseSupported;
        }
        else
        {
            double? sampleRate = TryGetSelectedInputSampleRate();
            supported = sampleRate is { } rate ? (int)rate == RNNoiseSuppressor.RequiredSampleRate : null;
        }

        NoiseSuppressionHintText.Text = isRNNoise && supported == false
            ? $"現在のサンプルレートは{RNNoiseSuppressor.RequiredSampleRate}Hzではないため、動作中はSpeexに自動的にフォールバックします。"
            : string.Empty;
    }

    private double? TryGetSelectedInputSampleRate()
    {
        if (InputDriverCombo.SelectedItem is not string driver) return null;
        try { return AudioEngine.GetCurrentSampleRate(driver); }
        catch { return null; }
    }

    private void NoiseGateCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_engine.Equalizer != null)
            _engine.Equalizer.NoiseGateEnabled = NoiseGateCheckBox.IsChecked == true;
    }

    private void NoiseGateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (NoiseGateLabel == null) return;

        NoiseGateLabel.Text = $"{e.NewValue:0} dB";
        if (_engine.Equalizer != null)
            _engine.Equalizer.NoiseGateThresholdDb = e.NewValue;
    }

    private void ExpanderCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_engine.Equalizer != null)
            _engine.Equalizer.ExpanderEnabled = ExpanderCheckBox.IsChecked == true;
    }

    private void ExpanderThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ExpanderThresholdLabel == null) return;

        ExpanderThresholdLabel.Text = $"{e.NewValue:0} dB";
        if (_engine.Equalizer != null)
            _engine.Equalizer.ExpanderThresholdDb = e.NewValue;
    }

    private void ExpanderRatioSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ExpanderRatioLabel == null) return;

        ExpanderRatioLabel.Text = $"{e.NewValue:0}:1";
        if (_engine.Equalizer != null)
            _engine.Equalizer.ExpanderRatio = e.NewValue;
    }

    private void FrequencyTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ((UIElement)sender).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }

    private void OnEngineError(object? sender, Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            StartStopButton.Content = "開始";
            StatusText.Text = $"デバイスエラー: {ex.Message}";
            SetControlsEnabled(true);
            UpdateTrayText("エラーで停止中");
        });
    }
}
