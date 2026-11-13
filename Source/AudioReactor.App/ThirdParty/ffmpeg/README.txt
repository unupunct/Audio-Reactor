FFmpeg (LGPL v3 build)
----------------------
Audio Reactor uses FFmpeg to encode recordings (MP4/MP3).

Binary: ffmpeg.exe from the "win64-lgpl" static build, FFmpeg-Builds (BtbN), release n8.1
Source: https://github.com/FFmpeg-Builds/FFmpeg-Builds  (build scripts)
        https://ffmpeg.org/download.html#get-sources    (FFmpeg source code)
Licence: GNU LGPL v3 (see LICENSE-FFmpeg-LGPLv3.txt). FFmpeg is a separate program that Audio Reactor
starts as a child process; you may replace this file with any compatible ffmpeg.exe build.
This build contains no GPL components (no libx264); H.264 video uses the Windows Media Foundation / GPU encoders.
SHA-256 of the downloaded archive: 364dbf1acd56385c5649421ba2584b7ef632e2f9c06c5c3fe6cc128bac90bb7b
