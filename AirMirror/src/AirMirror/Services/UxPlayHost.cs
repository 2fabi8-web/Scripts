using System.Diagnostics;
using System.IO;
using System.Text;

namespace AirMirror.Services;

/// <summary>Startet/stoppt uxplay.exe und reicht dessen Konsolenausgabe weiter.</summary>
public sealed class UxPlayHost : IDisposable
{
    const string MsysBin = @"C:\msys64\ucrt64\bin";

    readonly IntPtr _job = Native.CreateKillOnCloseJob();
    Process? _proc;

    public event Action<string>? Output;
    public event Action<int>? Exited;

    public bool IsRunning => _proc is { HasExited: false };
    public int ProcessId => IsRunning ? _proc!.Id : 0;

    /// <summary>Sucht uxplay.exe: Einstellung → neben der App → MSYS2 → Build-Ordner im Repo.</summary>
    public static string? FindExecutable(string configured)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured)) candidates.Add(configured);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "engine", "uxplay.exe"));
        candidates.Add(Path.Combine(MsysBin, "uxplay.exe"));
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "engine", "UxPlay", "build", "uxplay.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string BuildArguments(AppSettings s, int screenW, int screenH)
    {
        var a = new List<string>();
        a.Add("-n"); a.Add(Quote(string.IsNullOrWhiteSpace(s.DeviceName) ? "AirMirror" : s.DeviceName.Trim()));
        a.Add("-nh");

        var res = s.Resolution == "auto" ? $"{screenW}x{screenH}" : s.Resolution;
        a.Add("-s"); a.Add($"{res}@60");
        a.Add("-fps"); a.Add(s.MaxFps.ToString());

        a.Add("-vs"); a.Add(s.VideoSink);
        a.Add("-as"); a.Add(s.Audio ? "wasapisink" : "0");
        if (s.H265) a.Add("-h265");
        if (s.AllowTakeover) a.Add("-nohold");
        if (!string.IsNullOrWhiteSpace(s.Pin)) { a.Add("-pin"); a.Add(s.Pin.Trim()); }
        if (!string.IsNullOrWhiteSpace(s.ExtraArgs)) a.Add(s.ExtraArgs.Trim());
        return string.Join(' ', a);
    }

    static string Quote(string v) => "\"" + v.Replace("\"", "") + "\"";

    public void Start(string exe, string args)
    {
        Stop();
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        // DLLs + GStreamer-Plugins finden: eigenes Verzeichnis und MSYS2 vorne in den PATH
        var exeDir = Path.GetDirectoryName(exe)!;
        psi.Environment["PATH"] = $"{exeDir};{MsysBin};{Environment.GetEnvironmentVariable("PATH")}";
        var bundledPlugins = Path.Combine(exeDir, "lib", "gstreamer-1.0");
        if (Directory.Exists(bundledPlugins)) psi.Environment["GST_PLUGIN_PATH"] = bundledPlugins;

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        p.OutputDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) Output?.Invoke(e.Data); };
        p.Exited += (_, _) => Exited?.Invoke(SafeExitCode(p));

        p.Start();
        Native.AssignProcessToJobObject(_job, p.Handle);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        _proc = p;
    }

    static int SafeExitCode(Process p) { try { return p.ExitCode; } catch { return -1; } }

    public void Stop()
    {
        var p = _proc;
        _proc = null;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
            {
                p.EnableRaisingEvents = false;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
        }
        catch { /* bereits beendet */ }
        p.Dispose();
    }

    public void Dispose() => Stop();
}
