# AirMirror

Windows-App zum Spiegeln von iPad/iPhone per AirPlay auf einen beliebigen Monitor.
Die GUI (C#/WPF, .NET 8) steuert [UxPlay](https://github.com/FDH2/UxPlay) (GPL-3.0) als AirPlay-Engine.

## Aufbau

```
AirMirror/
├─ engine/
│  ├─ build-uxplay.sh     baut UxPlay in MSYS2 (UCRT64)
│  └─ UxPlay/build/uxplay.exe
└─ src/AirMirror/         WPF-App
   ├─ Services/UxPlayHost.cs     startet uxplay.exe (Job Object → stirbt mit der App)
   ├─ Services/WindowPlacer.cs   setzt das Videofenster randlos auf den gewählten Monitor
   ├─ Services/SystemIntegration.cs  Autostart, Firewall, Netzwerkprofil, Tray-Icon
   └─ MainWindow.xaml(.cs)       Oberfläche
```

## Einrichten

1. MSYS2 installieren (`winget install MSYS2.MSYS2`), dann UxPlay bauen:
   ```
   C:\msys64\usr\bin\env MSYSTEM=UCRT64 CHERE_INVOKING=1 /usr/bin/bash -lc ./build-uxplay.sh
   ```
   (im Ordner `engine`; das erste `pacman -Syu` beendet sich evtl. selbst → einfach nochmal starten)
2. App bauen: `dotnet build -c Release` in `src/AirMirror`
3. `AirMirror.exe` starten → „Firewall-Freigabe einrichten“ klicken (einmalig)
4. iPad: Kontrollzentrum → Bildschirmsynchronisierung → „AirMirror“

## Voraussetzungen

- PC und iPad im selben (W)LAN, keine Client-Isolation (Gast-WLAN)
- Windows-Netzwerkprofil **Privat**
- Einstellungen liegen in `%AppData%\AirMirror\settings.json`

## Bedienung

- Fenster schließen → App läuft im Tray weiter; Beenden über Rechtsklick aufs Tray-Symbol
- Vollbild: Schalter in der App oder Tray-Menü „Vollbild umschalten“
- Mac als zweiter Bildschirm: am Mac Bildschirmsynchronisierung → „AirMirror“ → „Als separaten Bildschirm verwenden“.
  „Niedrige Latenz“ (`-vsync no`, Standard: an) verhindert die hohe Verzögerung durch die Audio/Video-Synchronisation;
  zum Video-Schauen mit exaktem Lippensync ausschalten.
- iPad: AirPlay kann unter iPadOS nur spiegeln, ein erweiterter Bildschirm (Stage Manager) geht nur per Kabel.
- Bildstörungen/Absturz bei Nvidia: Video-Ausgabe auf „Direct3D 11“ bzw. „OpenGL“ umstellen
