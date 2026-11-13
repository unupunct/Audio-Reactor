# User guide

## Playing music

- **Add music:** *Music Library → Add files / Add folder*, or drag files and folders onto the window. You can also pass audio files to `AudioReactor.exe` on the command line (or use *Open with → Choose another app* in Explorer).
- **Play:** double-click a track. Right-click for *Add to queue*, *Toggle favorite*, *Add to playlist*, *Remove from library*.
- **Player bar:** play/pause, previous/next, stop, shuffle, repeat (off / all / one), seek, volume, output device.
- **Keyboard:** `Space` play/pause, `F11` fullscreen visualizer.
- **Playlists:** create, rename, delete, reorder (Move up/down), import and export `.m3u`, *Save queue*. The drop-down on the Playlists page creates **smart playlists** by genre, artist, recently added, frequently played or favorites.
- **Equalizer & effects:** *Settings → Equalizer* (10 bands, presets) and *Audio effects* (bass, treble, reverb, echo, stereo width, compressor, limiter, normalization). Untick *Enable effects processing* for unprocessed output. *Playback speed* changes speed and pitch together.

### Gapless playback and crossfade

*Settings → Track transitions.* **Gapless** (on by default) opens the next song in advance and joins it with no silence — it follows shuffle, repeat and queue changes, and works across files with different sample rates. **Crossfade** (0–12 s, off by default) overlaps the end of a song with the start of the next with an equal-power fade, so the volume stays even through the blend. Very short songs get a shorter fade automatically. Skipping with Next/Previous cuts immediately. Raw `.aac` files have no reliable length, so a crossfade may not start for them (they are not covered by the tests).

## Internet radio

*Internet Radio → Add station.* Paste a stream URL (Icecast, SHOUTcast, direct MP3/AAC/OGG) or a `.m3u` / `.pls` link — playlist links are resolved to the first stream. You can also import a playlist file or URL. Cards show station name, genre and country; ★ marks favorites; double-click or **Play** to listen. The now-playing title appears in the player bar when the station sends it. If the connection drops, the player reconnects automatically (you will get a notification). *Settings → Audio → Radio buffer* trades start-up delay for stability.

Audio Reactor ships with **no** station list: add the stations you like.

**Radio sound** (*Settings → Radio sound*): **Even volume** measures loudness (K-weighted, like streaming services do) and smoothly lifts quiet songs and lowers loud ones toward a target (−16 LUFS by default, or choose quieter/louder), by up to 12 dB; it never boosts silence or the noise between songs. **Clean sound** removes sub-bass rumble, turns down background hiss in quiet passages and protects against clipping. Both apply to radio automatically; tick *Also apply to my local music* to use them for your files too.

## Visualizer Studio

Pick a preset on the right (filter by category or search). Controls: palette, quality (Low for older graphics cards), intensity, animation speed, beat sensitivity. **Fullscreen** (or `F11`) opens on a second monitor when you have one, on the current monitor otherwise; `Esc` leaves.

**Effect & palette editor** — adjust brightness, contrast, saturation, glow, particle density/speed, rotation, camera, zoom, depth, motion and sensitivities; edit the six palette colours or generate a random palette; **Save** the result as a custom preset, then duplicate, favorite, delete, export or import it (plain JSON files you can share).

## RGB lighting

*RGB Lighting → Detect devices.* The **virtual light bar** always works and previews the effect. For real hardware:

| Integration | You need |
|---|---|
| OpenRGB | OpenRGB running with *SDK Server → Start Server* (default port 6742). Controls everything OpenRGB supports, including many Corsair, ASUS, MSI and Gigabyte devices |
| Razer Chroma | Razer Synapse 3 with Chroma Connect / SDK enabled |
| Philips Hue | A Hue Bridge on your network: enter its IP, press the bridge's link button, then **Pair Hue bridge**. Hue limits commands to about 10 per second, so lights update more slowly than other devices |
| Corsair iCUE | Not supported directly — use OpenRGB |

Tick the devices to control (hardware is **off by default**), set the zone range for each, choose a mode and turn **Lighting ON**. *Strobe* mode is capped at 3 flashes per second but can still affect people with photosensitive epilepsy. The integrations were tested against mock servers, **not real hardware** — please report what works.

## Export a video for YouTube

1. Open **Visualizer Studio → Export for YouTube…** and keep that page open (it is the live preview).
2. **Sound source:** *Use what's playing* or *Choose a song…*
3. **Template:** leave *Auto-match* ticked and Audio Reactor listens to the first part of the song (tempo, energy, bass vs treble) and selects the best of 12 templates — or just click another template. The preview updates immediately.
4. **Text:** title and artist are filled from the song's tags; edit them, add a footer (channel name, link, hashtags), choose the layout, size, whether the title stays or fades after 10 seconds, and the progress bar.
5. **Export:** choose the quality (1080p30 is YouTube's recommended default) and length (a 30-second test is handy), then **Export video**.

The export records in **real time**, so a 4-minute song takes about 4 minutes, then encodes with FFmpeg (GPU encoder if available). You get `AudioReactor_<date>.mp4`, `…_thumbnail.jpg` (1280×720) and `…_description.txt` in the folder you chose. Only use music you have the right to publish.

## Recording

*Visualizer Studio → Record…* — video + audio (MP4), video only, or audio only (WAV or MP3); resolution 720p–4K, 24/30/60 fps; start, pause, stop; duration and estimated size are shown. 4K needs a strong GPU. Stay on Visualizer Studio while recording video.

## Mini player, tray and notifications

The title-bar button switches to a small always-on-top **mini player**; its ↗ button returns. With *Minimize to tray* on, minimizing hides the window and the tray icon brings it back. *Settings → Notifications* chooses which Windows notifications appear (track change, radio lost/restored, errors, new audio device, recording done) — they appear when the app is in the background.
