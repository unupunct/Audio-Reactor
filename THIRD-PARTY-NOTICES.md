# Third-party components

Audio Reactor itself is MIT licensed (see `LICENSE`). It uses the following components, each under its own license.

| Component | Use | License |
|---|---|---|
| [.NET / WPF](https://github.com/dotnet/runtime) | Runtime and UI framework (bundled, self-contained) | MIT |
| [NAudio](https://github.com/naudio/NAudio) 3.x | Audio playback (WASAPI), decoding, DSP building blocks | MIT |
| [NAudio.Vorbis](https://github.com/naudio/Vorbis) | Ogg Vorbis decoding | MIT |
| [TagLibSharp](https://github.com/mono/taglib-sharp) | Reading music tags and artwork | LGPL-2.1 |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) | Local library database | MIT |
| [Serilog](https://serilog.net/) | Structured logs | Apache-2.0 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MVVM helpers | MIT |
| [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/) | Hosts the visualizer renderer. The WebView2 **Runtime** is a Microsoft component installed separately (included with Windows 11). | Microsoft WebView2 SDK license / Runtime EULA |
| [FFmpeg](https://ffmpeg.org/) (LGPL v3 build by [BtbN](https://github.com/FFmpeg-Builds/FFmpeg-Builds)) | Encodes recordings to MP4/MP3. Runs as a separate process (`ThirdParty\ffmpeg\ffmpeg.exe`) and can be replaced by any compatible build. | LGPL-3.0 (no GPL components, no x264) |
| [WiX Toolset](https://wixtoolset.org/) v5 | Builds the installer (build-time only) | MS-RL |

LGPL notes: TagLibSharp and FFmpeg are used unmodified as separate libraries/executables.
FFmpeg source: <https://ffmpeg.org/download.html#get-sources>; build scripts: <https://github.com/FFmpeg-Builds/FFmpeg-Builds>.
