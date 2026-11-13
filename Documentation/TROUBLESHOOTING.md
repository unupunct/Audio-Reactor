# Troubleshooting

Logs are in `%AppData%\AudioReactor\logs` — attach the latest file when reporting a problem.

## The visualizer area is blank, or says WebView2 is missing
Install the **Microsoft Edge WebView2 Runtime**: <https://developer.microsoft.com/microsoft-edge/webview2/>. Then restart Audio Reactor.

## No sound
- Check the output device in the player bar (bottom right). *Default device* follows Windows.
- If you unplugged the device, playback moves to the default device automatically; you will get a notification.
- Try a larger latency: *Settings → Audio → Output latency*.

## A file will not play
Corrupted or unsupported files are skipped and logged. Raw `.aac` files may show a wrong duration and seek poorly — convert them to `.m4a`. WMA and some other formats depend on the Windows media codecs installed on your PC.

## Radio station does not play
- Make sure the URL is a **stream** (not a web page). `.m3u` / `.pls` links are resolved automatically.
- HTTPS streams work; streams that need a login or special headers may not.
- AAC and HLS (`.m3u8`) streams use the Windows decoder as a fallback and are less tested than MP3 streams.
- Try a larger *Radio buffer* on slow connections.

## There is a short gap between songs
Make sure *Settings → Track transitions → Gapless* is on. Gaps can still occur when you skip manually, for raw `.aac` files, or when the next file cannot be opened (it is then skipped).

## Radio sounds too quiet, too loud or pumping
Adjust *Settings → Radio sound → Target loudness*, or turn *Even volume* off. If a station sounds dull or over-processed, turn *Clean sound* off.

## The visualizer stutters
Use **Quality: Low/Medium** and a 30 fps limit (*Settings → Visuals*). Heavy presets (plasma, nebula, fractals) cost more; the particle and spectrum presets are light.

## RGB devices are not found
- **OpenRGB:** start OpenRGB and enable *SDK Server → Start Server* (port 6742), then *Detect devices*.
- **Razer:** install Razer Synapse 3 and enable *Chroma Connect*.
- **Hue:** enter the bridge IP, **press the link button on the bridge first**, then pair. The bridge must be reachable on your network.
- Devices are disabled until you tick them. These integrations are untested on real hardware — see the README.

## Recording / YouTube export says FFmpeg was not found
`ffmpeg.exe` must be in `ThirdParty\ffmpeg` next to `AudioReactor.exe`. The installer includes it; when building from source run `Tools\fetch-ffmpeg.ps1`.

## Export fails or the video has no sound
- Start the song or let the export start it; the audio source must be playing during the export. Do not change songs or leave Visualizer Studio while exporting.
- If a failed export leaves a folder named `unfinished_<date>` in your output folder, it contains the raw recording; you can mux it with FFmpeg manually.
- 4K needs a lot of GPU power; use 1080p if the preview lags.

## The installer says WebView2 is required
Install the WebView2 Runtime (link above) and run the installer again.

## SmartScreen warns about the installer
The installer is not code-signed. Choose *More info → Run anyway* if you downloaded it from this repository's Releases page.

## Reset everything
Close the app and delete `%AppData%\AudioReactor` (settings, library, presets, logs).
