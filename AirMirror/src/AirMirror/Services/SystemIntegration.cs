using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace AirMirror.Services;

public static class SystemIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "AirMirror";
    public const string FirewallRuleName = "AirMirror (UxPlay)";

    public static void SetAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;
        if (enabled) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue(RunName, throwOnMissingValue: false);
    }

    /// <summary>Legt per UAC-Abfrage eine eingehende Firewall-Regel für uxplay.exe an.</summary>
    public static bool AddFirewallRule(string uxplayExe)
    {
        var ps = $"Remove-NetFirewallRule -DisplayName '{FirewallRuleName}' -ErrorAction SilentlyContinue; " +
                 $"New-NetFirewallRule -DisplayName '{FirewallRuleName}' -Direction Inbound -Program '{uxplayExe}' " +
                 "-Action Allow -Profile Private,Domain | Out-Null";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -WindowStyle Hidden -Command \"{ps}\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }
        catch { return false; } // UAC abgelehnt
    }

    public static async Task<bool> FirewallRuleExistsAsync() =>
        (await RunPowerShellAsync($"(Get-NetFirewallRule -DisplayName '{FirewallRuleName}' -ErrorAction SilentlyContinue | Measure-Object).Count"))
        .Trim() is var s && s != "" && s != "0";

    /// <summary>Netzwerkkategorie der aktiven Verbindungen (Public/Private/DomainAuthenticated).</summary>
    public static async Task<string[]> GetNetworkCategoriesAsync() =>
        (await RunPowerShellAsync("(Get-NetConnectionProfile).NetworkCategory"))
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    static async Task<string> RunPowerShellAsync(string command)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -Command \"{command}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return output;
        }
        catch { return ""; }
    }

    /// <summary>Zeichnet das App-Icon (Bildschirm mit AirPlay-Dreieck) zur Laufzeit.</summary>
    public static Icon CreateIcon(bool active)
    {
        using var bmp = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var accent = active ? Color.FromArgb(52, 199, 89) : Color.FromArgb(10, 132, 255);
            using var pen = new Pen(Color.White, 6) { LineJoin = LineJoin.Round };
            using var bg = new SolidBrush(accent);
            using var path = RoundedRect(new Rectangle(2, 2, 60, 60), 14);
            g.FillPath(bg, path);
            g.DrawLines(pen, new[] { new Point(20, 40), new Point(13, 40), new Point(13, 14), new Point(51, 14), new Point(51, 40), new Point(44, 40) });
            using var white = new SolidBrush(Color.White);
            g.FillPolygon(white, new[] { new Point(32, 30), new Point(46, 52), new Point(18, 52) });
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
