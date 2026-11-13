## Audio Reactor 1.0.0

A Windows music player, internet radio and real-time audio-reactive visualizer, with RGB light sync and one-click **YouTube video export**.

### Download
- **`AudioReactor-Setup.exe`** — recommended. Per-user install (no administrator rights), Start Menu shortcut, optional desktop shortcut, in-place upgrades that keep your settings.
- `AudioReactor.msi` — the same package without the setup wrapper, for advanced use.

Requires Windows 10/11 x64 and the Microsoft Edge WebView2 Runtime (built into Windows 11; the installer checks for it). The app is self-contained — no .NET install needed. The installer is **not code-signed**, so Windows SmartScreen may warn: *More info → Run anyway*.

SHA-256:
```
2cb7b3a56adafb2244a30ae5c637af8c5a0c6185bb048d3ff78e0d934e265e32  AudioReactor-Setup.exe
f830de500b6aa4e626acf7f536fcbe1260ff654902aa4d3fefe3336cd423e60a  AudioReactor.msi
```

### Highlights
- Local player (MP3, WAV, FLAC, OGG, M4A, WMA), library, playlists + smart playlists, 10-band EQ, effects (reverb, echo, compressor, limiter…), playback speed
- Internet radio (Icecast/SHOUTcast, M3U/PLS, now-playing metadata, auto-reconnect) — you add your own stations
- 51 audio-reactive visualizer presets, real-time FFT + beat/BPM detection, effect & palette editor, custom presets
- RGB lighting: 10 modes, OpenRGB / Razer Chroma / Philips Hue adapters, virtual preview
- **Export for YouTube:** use the playing song or pick one → auto-matched (or chosen) template → edit title/subtitle/footer → H.264 + AAC MP4, thumbnail and description file
- Recording to MP4 / WAV / MP3, mini player, tray + Windows notifications

### Tested
65 automated tests plus manual runs on one Windows 11 PC, including an installer install → upgrade → uninstall cycle (user data kept). See the [README](https://github.com/unupunct/Audio-Reactor#what-is-verified--and-what-is-not).

### Known limitations (please read)
- **RGB hardware is untested:** Hue, Razer Chroma and OpenRGB support was tested only against mock servers, not real devices.
- **Radio** was tested against a local Icecast-style server, **not live internet stations**; AAC/HLS streams use a Windows-decoder fallback and are untested.
- Not implemented yet: crossfade/gapless playback, desktop-mode and screen-saver visualizer windows, compact mode, per-frequency routing and bloom in the effect editor, WebGL 3D scenes (3D presets are software-projected canvas effects), output sample-rate/capture-device settings, CPU/GPU meters.
- Raw `.aac` files play but show an estimated duration.
- YouTube export and recording run in real time and need the Visualizer Studio page to stay open.
- Not tested on a clean virtual machine.

Bundles FFmpeg (LGPL v3, unmodified) — see `THIRD-PARTY-NOTICES.md`. Only upload music you have the rights to publish.
