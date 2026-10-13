using System.Windows;
using AirMirror.Services;
using Forms = System.Windows.Forms;

namespace AirMirror;

public partial class App : Application
{
    Mutex? _singleInstance;
    Forms.NotifyIcon? _tray;
    MainWindow? _main;
    System.Drawing.Icon? _iconIdle, _iconActive;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance = new Mutex(true, "AirMirror.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("AirMirror läuft bereits (siehe Infobereich der Taskleiste).", "AirMirror");
            Shutdown();
            return;
        }

        base.OnStartup(e);
        bool startInTray = e.Args.Contains("--tray");

        _iconIdle = SystemIntegration.CreateIcon(active: false);
        _iconActive = SystemIntegration.CreateIcon(active: true);

        _main = new MainWindow();
        _main.Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
            _iconIdle.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        _main.StatusChanged += (text, mirroring) =>
        {
            if (_tray == null) return;
            _tray.Text = ("AirMirror – " + text)[..Math.Min(63, ("AirMirror – " + text).Length)];
            _tray.Icon = mirroring ? _iconActive : _iconIdle;
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Öffnen", null, (_, _) => ShowMain());
        menu.Items.Add("Vollbild umschalten", null, (_, _) => _main.ToggleFullscreen());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitApp());

        _tray = new Forms.NotifyIcon { Icon = _iconIdle, Text = "AirMirror", Visible = true, ContextMenuStrip = menu };
        _tray.MouseClick += (_, me) => { if (me.Button == Forms.MouseButtons.Left) ShowMain(); };

        if (!startInTray) _main.Show();
        _main.Initialize();
    }

    void ShowMain()
    {
        if (_main == null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void ExitApp()
    {
        _main?.ShutdownServer();
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }

    public void NotifyBalloon(string title, string text)
    {
        _tray?.ShowBalloonTip(3000, title, text, Forms.ToolTipIcon.Info);
    }
}
