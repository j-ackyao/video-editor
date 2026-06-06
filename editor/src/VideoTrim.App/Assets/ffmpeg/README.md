# Bundled FFmpeg binaries

Place the LGPL static builds of `ffmpeg.exe` and `ffprobe.exe` in this folder. They are copied
next to the application on build (`Assets/ffmpeg/`) and located at runtime by `FfmpegLocator`
(see `App.xaml.cs`). The app verifies they exist at startup and fails loudly if missing (§12, §14).

Required encoders for the chosen builds (§3.1):
- `libx264` (MP4/MOV/MKV video) and `aac`
- `libvpx-vp9` (WebM video) and `libopus`
- `gif` (palettegen/paletteuse)

Recommended source: https://www.gyan.dev/ffmpeg/builds/ or https://github.com/BtbN/FFmpeg-Builds
(use an LGPL build; a GPL build with `libx265` would require revisiting the distribution license).

These large binaries are intentionally **not** committed to the repository.
