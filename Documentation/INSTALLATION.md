# Installation

## Requirements

- Windows 10 (64-bit) or Windows 11
- Microsoft Edge **WebView2 Runtime** — preinstalled on Windows 11 and on most up-to-date Windows 10 PCs. The installer checks for it and tells you where to get it if it is missing: <https://developer.microsoft.com/microsoft-edge/webview2/>
- About 500 MB of free disk space (≈290 MB installed) (the app is self-contained, so you do **not** need to install .NET)
- A graphics card with up-to-date drivers is recommended for the visualizer; GPU video encoders (NVIDIA, AMD, Intel) are used automatically for exports when available

## Install

1. Download `AudioReactor-Setup.exe` from the [Releases page](https://github.com/unupunct/Audio-Reactor/releases/latest).
2. Run it. Windows SmartScreen may warn that the publisher is unknown because the installer is not code-signed — choose **More info → Run anyway** if you trust the download.
3. Accept the licence and pick the components (the desktop shortcut is optional).
4. Start **Audio Reactor** from the Start Menu.

The app installs **per user** into `%LocalAppData%\Programs\Audio Reactor`. No administrator rights are needed.

## Upgrade

Run the newer `AudioReactor-Setup.exe`; it replaces the old version. Your settings, library, playlists, stations and presets are stored separately in `%AppData%\AudioReactor` and are kept.

## Uninstall

*Settings → Apps → Installed apps → Audio Reactor → Uninstall.* This removes the program. Your data in `%AppData%\AudioReactor` is left in place so a reinstall picks up where you left off; delete that folder if you want to remove everything.

## Silent install (IT / scripting)

```
AudioReactor-Setup.exe /quiet              (install for the current user)
AudioReactor-Setup.exe /uninstall /quiet   (uninstall)
```

`AudioReactor.msi` (the same package without the setup wrapper) is also attached to each release for advanced use; prefer the Setup.exe, which installs per user.

## Where things are stored

| What | Where |
|---|---|
| Settings | `%AppData%\AudioReactor\settings.json` |
| Library, playlists, stations | `%AppData%\AudioReactor\library.db` |
| Custom visual presets | `%AppData%\AudioReactor\presets\*.arpreset.json` |
| Logs (7 days) | `%AppData%\AudioReactor\logs` |
| Recordings (default) | `%UserProfile%\Videos\Audio Reactor` |
