using System.IO;
using System.Text.Json;

namespace AirMirror.Services;

public sealed class AppSettings
{
    public string DeviceName { get; set; } = "AirMirror";
    /// <summary>"auto" = Auflösung des Zielmonitors, sonst z.B. "1920x1080".</summary>
    public string Resolution { get; set; } = "auto";
    public int MaxFps { get; set; } = 60;
    /// <summary>Screen.DeviceName des Zielmonitors, leer = primärer Monitor.</summary>
    public string MonitorDevice { get; set; } = "";
    public bool Fullscreen { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public bool H265 { get; set; } = false;
    public bool Audio { get; set; } = true;
    public string VideoSink { get; set; } = "d3d11videosink";
    public string Pin { get; set; } = "";
    public bool AllowTakeover { get; set; } = true;
    /// <summary>-vsync no: Bild sofort anzeigen statt mit dem Ton zu synchronisieren (Mac als 2. Bildschirm).</summary>
    public bool LowLatency { get; set; } = true;
    public string ExtraArgs { get; set; } = "";
    public string UxPlayPath { get; set; } = "";
    public bool AutoStartServer { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirMirror");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* kaputte Datei -> Standardwerte */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }
}
