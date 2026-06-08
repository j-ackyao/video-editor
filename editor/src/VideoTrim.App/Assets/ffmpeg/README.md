# Bundled FFmpeg binaries

Place `ffmpeg.exe` and `ffprobe.exe` in this folder. They are copied next to the application on
build (`Assets/ffmpeg/`) and located at runtime by `FfmpegLocator` (see `App.xaml.cs`). The app
verifies they exist at startup and fails loudly if missing (§12, §14).

## Use a GPL build (required for libx264)

The app's default video export uses **`libx264`** for MP4/MOV/MKV (eng-doc §10.0). `libx264` is
**GPL-licensed and is NOT included in LGPL FFmpeg builds** — an LGPL build raises
`Unknown encoder 'libx264'` at export time. You must therefore bundle a **GPL** build. See
`decisions.md` (decision 25) for the licensing note: the eng-doc's "LGPL build + libx264" assumption
is internally inconsistent, and a GPL build is required for the documented commands to work.

Required encoders (verify with `ffmpeg -encoders`): `libx264`, `libvpx-vp9`, `aac`, `libopus`, `gif`.

Recommended source: https://github.com/BtbN/FFmpeg-Builds — use a **gpl** build, e.g.
`ffmpeg-master-latest-win64-gpl.zip` (static: just `ffmpeg.exe` + `ffprobe.exe`, no extra DLLs).
The `gyan.dev` "full"/"essentials" builds are also GPL and work.

These large binaries are intentionally **not** committed to the repository (see `.gitignore`).
