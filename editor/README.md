# Video Trim & Export

A lightweight Windows (WinUI 3 / .NET 8) desktop app that opens a video, trims it with two timeline
handles (with scrub-to-preview), and exports it with control over resolution, frame rate, bitrate,
**target file size**, audio, and **GIF** output. All encoding is done by a bundled FFmpeg, kept
entirely behind a service layer. Implemented from `../eng-doc.md`; deviations and ambiguity
resolutions are documented in [`decisions.md`](./decisions.md).

## Solution layout

```
editor/
  VideoTrim.sln
  src/
    VideoTrim.Core/         UI-free engine (net8.0) — Models, Encoding, Probing, Services, ViewModels
    VideoTrim.App/          WinUI 3 desktop app (net8.0-windows) — Views, Controls, DI startup
      Assets/ffmpeg/        drop ffmpeg.exe + ffprobe.exe here (not committed; see its README)
  tests/
    VideoTrim.Core.Tests/   xUnit unit tests for the engine (net8.0)
  decisions.md
```

Architecture follows MVVM with a thin service layer (eng-doc §4):

- **Models** (`MediaInfo`, `TrimRange`, `ExportSettings`, `GifSettings`, `EncodeProgress`, …)
- **`ITargetSizeCalculator`** — pure target-size → bitrate math (§9.1)
- **`IFfmpegCommandBuilder`** — pure ffmpeg/ffprobe argument builder (§10)
- **`IMediaProbeService`** — ffprobe metadata (§6.1)
- **`IEncodingService`** — trim + transcode + GIF, two-pass, GIF iterative target size (§6.4, §9.2)
- **View-models** — `MainViewModel`, `ExportViewModel`, `GifViewModel` (live in Core, fully testable)

FFmpeg runs as a child process behind `IProcessRunner`; progress is parsed from
`-progress pipe:1` and cancellation kills the process tree (§11).

## Building & testing the engine (no special tooling)

```powershell
cd editor
dotnet build src/VideoTrim.Core/VideoTrim.Core.csproj
dotnet test  tests/VideoTrim.Core.Tests/VideoTrim.Core.Tests.csproj
```

54 unit tests cover the size calculator, golden ffmpeg arguments, the GIF reduction loop, the
progress parser, ffprobe JSON mapping, and the trim invariants.

## Building the WinUI app

The Windows App SDK XAML compiler must run under **Visual Studio MSBuild** in this environment
(`dotnet build` cannot compile the XAML here — see `decisions.md` §17):

```powershell
$msb = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
& $msb editor\src\VideoTrim.App\VideoTrim.App.csproj `
    /t:Restore,Build /p:Platform=x64 /p:Configuration=Debug /p:RuntimeIdentifier=win-x64
```

Before running the app, place an **LGPL** `ffmpeg.exe` and `ffprobe.exe` in
`src/VideoTrim.App/Assets/ffmpeg/` (see that folder's `README.md`). The app verifies they exist at
startup and fails loudly if they are missing.

## Features → user stories

| Story | Feature |
|-------|---------|
| US1 | Open + play/pause + seek via the unified timeline (`MediaPlayerElement`). |
| US2 | Dual trim handles with kept-range highlight and scrub-to-preview; clamped, frame-snapped. |
| US3 | Export to MP4/MOV/MKV/WebM with resolution, fps, and CRF/bitrate; progress + cancel. |
| US4 | Target output size → two-pass encode at a computed bitrate, with feasibility warnings. |
| US5 | Include/exclude audio and choose audio bitrate (subtracted from the size budget). |
| US6 | GIF export via palettegen/paletteuse with color/fps/dither and optional target size. |
