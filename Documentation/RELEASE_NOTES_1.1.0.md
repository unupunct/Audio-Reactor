## Audio Reactor 1.1.0

### New
- **Gapless playback** — the next song is opened in advance and joined with no silence (on by default). Works with shuffle, repeat and queue changes, and across files with different sample rates.
- **Crossfade** — 0–12 s equal-power blend between songs (*Settings → Track transitions*, off by default).
- **Radio sound filter** (*Settings → Radio sound*)
  - **Even volume:** loudness leveling so every song and station plays at about the same loudness (target −16 LUFS by default, or quieter/louder; up to ±12 dB; silence and noise are never boosted).
  - **Clean sound:** removes sub-bass rumble, turns down hiss in quiet passages and prevents clipping.
  - Always applied to radio; tick *Also apply to my local music* to use it for your files too.

### Download
- **`AudioReactor-Setup.exe`** — recommended. Per-user install (no administrator rights); upgrades 1.0.0 in place and keeps your settings and library.
- `AudioReactor.msi` — the same package without the setup wrapper.

Requires Windows 10/11 x64 and the Microsoft Edge WebView2 Runtime. The installer is **not code-signed**; SmartScreen may warn (*More info → Run anyway*).

SHA-256:
```
67481b240ef9904f279b5a0d78f58b8512c92f20c98c0dab926ae0eff74a6d1b  AudioReactor-Setup.exe
fe5f43bd7997e8cc82b9959e8b611fbf435d6251a82a6a2ef60597cdf2e440ce  AudioReactor.msi
```

### Tested
81 automated tests (16 new), including a real-audio-device test and a 3-track crossfaded queue run in the app. The 1.1.0 installer was installed, run and uninstalled on the development PC (user data kept). Radio filter results were measured on synthetic audio: a 20 dB loudness gap became 0.4 dB, hiss dropped from −61 to −72 dB, and rumble was removed.

### Known limitations
- The radio filter has **not been listened to on live stations**; RGB hardware (Hue, Razer Chroma, OpenRGB) is still **untested on real devices**; radio was tested against a local server, not internet stations.
- Raw `.aac` files may not crossfade (no reliable length).
- Not implemented yet: desktop-mode and screen-saver visualizer windows, compact mode, per-frequency routing and bloom in the effect editor, WebGL 3D scenes, output sample-rate/capture-device settings, CPU/GPU meters.
- Not tested on a clean virtual machine. Bundles FFmpeg (LGPL v3, unmodified) — see `THIRD-PARTY-NOTICES.md`.

Full changelog: [CHANGELOG.md](https://github.com/unupunct/Audio-Reactor/blob/main/CHANGELOG.md)
