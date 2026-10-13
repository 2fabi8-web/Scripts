using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AirMirror.Services;
using Forms = System.Windows.Forms;

namespace AirMirror;

public partial class MainWindow : Window
{
    enum State { Stopped, Waiting, Mirroring, Error }

    readonly UxPlayHost _host = new();
    readonly WindowPlacer _placer = new();
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _restartDebounce = new() { Interval = TimeSpan.FromMilliseconds(900) };
    readonly StringBuilder _log = new();

    AppSettings _s = new();
    string? _exe;
    string _runningArgs = "";
    bool _loading = true, _userStopped, _trayHintShown;
    State _state = State.Stopped;

    /// <summary>(Statustext, Spiegelung aktiv) – für Tray-Icon/Tooltip.</summary>
    public event Action<string, bool>? StatusChanged;

    public MainWindow()
    {
        InitializeComponent();
        _host.Output += line => Dispatcher.BeginInvoke(() => AppendLog(line));
        _host.Exited += code => Dispatcher.BeginInvoke(() => OnHostExited(code));
        _tick.Tick += (_, _) => OnTick();
        _restartDebounce.Tick += (_, _) => { _restartDebounce.Stop(); if (_host.IsRunning) StartServer(); };
        SourceInitialized += (_, _) => UseDarkTitleBar();
    }

    // ---------------------------------------------------------------- Init

    public void Initialize()
    {
        _s = AppSettings.Load();

        FillMonitors();
        Fill(ResolutionBox, ("auto", "Wie Monitor"), ("1280x720", "1280 × 720"), ("1920x1080", "1920 × 1080"),
                            ("2560x1440", "2560 × 1440"), ("3840x2160", "3840 × 2160 (4K)"));
        Fill(FpsBox, ("30", "30 fps"), ("60", "60 fps"));
        Fill(SinkBox, ("d3d11videosink", "Direct3D 11 (empfohlen)"), ("d3d12videosink", "Direct3D 12"),
                      ("glimagesink", "OpenGL"), ("autovideosink", "Automatisch"));

        NameBox.Text = _s.DeviceName;
        Select(MonitorBox, _s.MonitorDevice);
        Select(ResolutionBox, _s.Resolution);
        Select(FpsBox, _s.MaxFps.ToString());
        Select(SinkBox, _s.VideoSink);
        FullscreenCheck.IsChecked = _s.Fullscreen;
        TopmostCheck.IsChecked = _s.AlwaysOnTop;
        AudioCheck.IsChecked = _s.Audio;
        H265Check.IsChecked = _s.H265;
        TakeoverCheck.IsChecked = _s.AllowTakeover;
        PinBox.Text = _s.Pin;
        AutostartCheck.IsChecked = _s.StartWithWindows;
        AutoServerCheck.IsChecked = _s.AutoStartServer;
        ExtraArgsBox.Text = _s.ExtraArgs;
        _loading = false;

        _exe = UxPlayHost.FindExecutable(_s.UxPlayPath);
        EngineText.Text = _exe != null ? $"Engine: {_exe}" : "Engine: uxplay.exe nicht gefunden";

        if (_exe == null)
            SetState(State.Error, "UxPlay-Engine fehlt",
                "uxplay.exe wurde nicht gefunden. Führe engine\\build-uxplay.sh in MSYS2 (UCRT64) aus.");
        else
            SetState(State.Stopped);

        _ = CheckEnvironmentAsync();
        _tick.Start();
        if (_s.AutoStartServer && _exe != null) StartServer();
    }

    void FillMonitors()
    {
        var items = Forms.Screen.AllScreens.Select((sc, i) =>
            (sc.DeviceName, $"Monitor {i + 1} – {sc.Bounds.Width} × {sc.Bounds.Height}{(sc.Primary ? " (Hauptmonitor)" : "")}"));
        Fill(MonitorBox, items.ToArray());
    }

    static void Fill(ComboBox box, params (string value, string label)[] items)
    {
        box.Items.Clear();
        foreach (var (value, label) in items) box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    }

    static void Select(ComboBox box, string value)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == value)
                           ?? box.Items.Cast<ComboBoxItem>().FirstOrDefault();
    }

    static string Selected(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    Forms.Screen TargetScreen =>
        Forms.Screen.AllScreens.FirstOrDefault(sc => sc.DeviceName == _s.MonitorDevice) ?? Forms.Screen.PrimaryScreen!;

    // ---------------------------------------------------------------- Server

    void StartServer()
    {
        if (_exe == null) return;
        var bounds = TargetScreen.Bounds;
        _runningArgs = UxPlayHost.BuildArguments(_s, bounds.Width, bounds.Height);
        AppendLog($"> uxplay {_runningArgs}");
        try
        {
            _userStopped = false;
            _host.Start(_exe, _runningArgs);
            SetState(State.Waiting);
        }
        catch (Exception ex)
        {
            SetState(State.Error, "Start fehlgeschlagen", ex.Message);
        }
    }

    void StopServer()
    {
        _userStopped = true;
        _restartDebounce.Stop();
        _host.Stop();
        SetState(State.Stopped);
    }

    public void ShutdownServer()
    {
        _tick.Stop();
        _userStopped = true;
        _host.Dispose();
    }

    void OnHostExited(int code)
    {
        if (_userStopped) return;
        SetState(State.Error, "Empfänger wurde beendet",
            $"UxPlay hat sich mit Code {code} beendet. Details im Protokoll unten.");
    }

    void OnTick()
    {
        if (!_host.IsRunning) return;
        bool mirroring = _placer.Tick(_host.ProcessId, TargetScreen.Bounds, _s.Fullscreen, _s.AlwaysOnTop);
        if (mirroring && _state != State.Mirroring) SetState(State.Mirroring);
        else if (!mirroring && _state == State.Mirroring) SetState(State.Waiting);
    }

    public void ToggleFullscreen()
    {
        FullscreenCheck.IsChecked = !(FullscreenCheck.IsChecked == true);
        Setting_Changed(FullscreenCheck, new RoutedEventArgs());
    }

    // ---------------------------------------------------------------- Status

    void SetState(State state, string? title = null, string? hint = null)
    {
        _state = state;
        var name = string.IsNullOrWhiteSpace(_s.DeviceName) ? "AirMirror" : _s.DeviceName.Trim();
        (Brush dot, string t, string h) = state switch
        {
            State.Waiting => ((Brush)FindResource("Accent"), "Bereit – warte auf iPad",
                $"Auf dem iPad: Kontrollzentrum öffnen → „Bildschirmsynchronisierung“ → „{name}“ wählen. iPad und PC müssen im selben WLAN sein."),
            State.Mirroring => ((Brush)FindResource("Ok"), "Spiegelung aktiv",
                "Zum Beenden auf dem iPad „Synchronisierung beenden“ tippen."),
            State.Error => ((Brush)FindResource("Danger"), title ?? "Fehler", hint ?? ""),
            _ => ((Brush)FindResource("SubText"), "Gestoppt", "Starte den Empfänger, um dein iPad zu spiegeln."),
        };
        StatusDot.Fill = dot;
        StatusText.Text = title ?? t;
        StatusHint.Text = hint ?? h;

        bool running = state is State.Waiting or State.Mirroring;
        StartStopButton.Content = running ? "Empfänger stoppen" : "Empfänger starten";
        StartStopButton.Background = running ? (Brush)FindResource("Danger") : (Brush)FindResource("Accent");
        StartStopButton.IsEnabled = _exe != null;
        FullscreenButton.IsEnabled = state == State.Mirroring;
        StatusChanged?.Invoke(StatusText.Text, state == State.Mirroring);
    }

    async Task CheckEnvironmentAsync()
    {
        var warnings = new List<string>();
        var categories = await SystemIntegration.GetNetworkCategoriesAsync();
        if (categories.Contains("Public"))
            warnings.Add("Dein Netzwerk ist als „Öffentlich“ eingestuft – dann blockiert Windows AirPlay meist. " +
                         "Stelle es unter Einstellungen → Netzwerk und Internet → WLAN/Ethernet → Netzwerkprofil auf „Privat“.");
        bool hasRule = await SystemIntegration.FirewallRuleExistsAsync();
        if (!hasRule && _exe != null)
            warnings.Add("Für UxPlay gibt es noch keine Firewall-Freigabe. Ohne sie findet das iPad den PC evtl. nicht.");

        FirewallButton.Visibility = !hasRule && _exe != null ? Visibility.Visible : Visibility.Collapsed;
        WarningText.Text = string.Join("\n\n", warnings);
        WarningCard.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------------------------------------------------------- Log

    void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > 60_000) _log.Remove(0, _log.Length - 40_000);
        LogBox.Text = _log.ToString();
        LogBox.ScrollToEnd();
    }

    // ---------------------------------------------------------------- UI-Events

    void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (_host.IsRunning) StopServer(); else StartServer();
    }

    void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    void RefreshMonitors_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        FillMonitors();
        Select(MonitorBox, _s.MonitorDevice);
        _loading = false;
    }

    async void Firewall_Click(object sender, RoutedEventArgs e)
    {
        if (_exe == null) return;
        FirewallButton.IsEnabled = false;
        bool ok = await Task.Run(() => SystemIntegration.AddFirewallRule(_exe));
        FirewallButton.IsEnabled = true;
        if (!ok) MessageBox.Show(this, "Die Firewall-Regel konnte nicht angelegt werden (Admin-Abfrage abgelehnt?).", "AirMirror");
        await CheckEnvironmentAsync();
    }

    void CopyLog_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(_log.ToString());

    void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;

        _s.DeviceName = NameBox.Text;
        _s.MonitorDevice = Selected(MonitorBox);
        _s.Resolution = Selected(ResolutionBox);
        _s.MaxFps = int.TryParse(Selected(FpsBox), out var fps) ? fps : 60;
        _s.VideoSink = Selected(SinkBox);
        _s.Fullscreen = FullscreenCheck.IsChecked == true;
        _s.AlwaysOnTop = TopmostCheck.IsChecked == true;
        _s.Audio = AudioCheck.IsChecked == true;
        _s.H265 = H265Check.IsChecked == true;
        _s.AllowTakeover = TakeoverCheck.IsChecked == true;
        _s.Pin = new string(PinBox.Text.Where(char.IsDigit).ToArray());
        _s.ExtraArgs = ExtraArgsBox.Text;
        _s.AutoStartServer = AutoServerCheck.IsChecked == true;

        if (sender == AutostartCheck)
        {
            _s.StartWithWindows = AutostartCheck.IsChecked == true;
            try { SystemIntegration.SetAutostart(_s.StartWithWindows); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Autostart"); }
        }
        _s.Save();

        _placer.Invalidate();
        if (_state == State.Waiting || _state == State.Mirroring)
        {
            // Nur wenn sich die UxPlay-Argumente ändern, muss der Server neu starten
            var b = TargetScreen.Bounds;
            if (UxPlayHost.BuildArguments(_s, b.Width, b.Height) != _runningArgs) _restartDebounce.Start();
            else SetState(_state);
        }
    }

    // ---------------------------------------------------------------- Fenster

    protected override void OnClosing(CancelEventArgs e)
    {
        // Schließen = in den Tray minimieren; Beenden über das Tray-Menü
        e.Cancel = true;
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            ((App)Application.Current).NotifyBalloon("AirMirror läuft weiter",
                "Der Empfänger bleibt im Infobereich aktiv. Rechtsklick auf das Symbol → Beenden.");
        }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void UseDarkTitleBar()
    {
        int on = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref on, sizeof(int));
    }
}
