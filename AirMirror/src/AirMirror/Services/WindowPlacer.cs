using System.Drawing;
using static AirMirror.Services.Native;

namespace AirMirror.Services;

/// <summary>
/// Findet das Videofenster von uxplay.exe und setzt es auf den gewünschten Monitor –
/// randlos im Vollbild oder als normales Fenster. GStreamer selbst kennt keine Monitor-Auswahl.
/// </summary>
public sealed class WindowPlacer
{
    IntPtr _hwnd;
    IntPtr _originalStyle;
    string? _appliedKey;

    public IntPtr VideoWindow => _hwnd;

    /// <summary>Wird periodisch aufgerufen. Liefert true, solange ein Videofenster existiert.</summary>
    public bool Tick(int pid, Rectangle monitor, bool fullscreen, bool topmost)
    {
        if (pid == 0) { Reset(); return false; }

        if (_hwnd == IntPtr.Zero || !IsWindow(_hwnd) || !IsWindowVisible(_hwnd))
        {
            Reset();
            _hwnd = FindVideoWindow((uint)pid);
            if (_hwnd == IntPtr.Zero) return false;
            _originalStyle = GetWindowLongPtr(_hwnd, GWL_STYLE);
        }

        var key = $"{monitor}|{fullscreen}|{topmost}";
        if (fullscreen)
        {
            // Vollbild aktiv durchsetzen: Der Sink vergrößert/verkleinert das Fenster bei Rotation des iPads.
            GetWindowRect(_hwnd, out var r);
            bool fits = r.Left == monitor.Left && r.Top == monitor.Top &&
                        r.Right == monitor.Right && r.Bottom == monitor.Bottom;
            if (!fits || key != _appliedKey) ApplyFullscreen(monitor, topmost);
        }
        else if (key != _appliedKey)
        {
            ApplyWindowed(monitor, topmost);
        }
        _appliedKey = key;
        return true;
    }

    /// <summary>Erzwingt eine erneute Platzierung beim nächsten Tick.</summary>
    public void Invalidate() => _appliedKey = null;

    void Reset() { _hwnd = IntPtr.Zero; _appliedKey = null; }

    void ApplyFullscreen(Rectangle m, bool topmost)
    {
        long style = (long)_originalStyle & ~(WS_CAPTION | WS_THICKFRAME | WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLongPtr(_hwnd, GWL_STYLE, new IntPtr(style | WS_VISIBLE));
        SetWindowPos(_hwnd, topmost ? HWND_TOPMOST : HWND_TOP, m.Left, m.Top, m.Width, m.Height,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW);
        SetForegroundWindow(_hwnd);
    }

    void ApplyWindowed(Rectangle m, bool topmost)
    {
        SetWindowLongPtr(_hwnd, GWL_STYLE, _originalStyle);
        int w = (int)(m.Width * 0.6), h = (int)(m.Height * 0.6);
        SetWindowPos(_hwnd, topmost ? HWND_TOPMOST : HWND_NOTOPMOST,
            m.Left + (m.Width - w) / 2, m.Top + (m.Height - h) / 2, w, h,
            SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    }

    static IntPtr FindVideoWindow(uint pid)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var wpid);
            if (wpid != pid || !IsWindowVisible(h) || GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;
            if (((long)GetWindowLongPtr(h, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return true;
            GetWindowRect(h, out var r);
            if (r.Right - r.Left < 64 || r.Bottom - r.Top < 64) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
