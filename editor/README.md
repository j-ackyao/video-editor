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

Before running the app, place a **GPL** `ffmpeg.exe` and `ffprobe.exe` in
`src/VideoTrim.App/Assets/ffmpeg/` (see that folder's `README.md` — a GPL build is required for the
`libx264` encoder). The app verifies they exist at startup and fails loudly if they are missing.

## Building a release .exe

The project is configured **unpackaged + self-contained** (`WindowsAppSDKSelfContained` +
`SelfContained` in the `.csproj`), so the output is a plain folder containing `VideoTrim.App.exe`
plus the bundled .NET runtime, the Windows App Runtime, and FFmpeg — it runs on any Windows 10
1809+/11 machine with **no installs**.

```powershell
$msb = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"

# Option A — Release build (runnable output folder)
& $msb editor\src\VideoTrim.App\VideoTrim.App.csproj `
    /t:Restore,Build /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64
#   → editor\src\VideoTrim.App\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\VideoTrim.App.exe

# Option B — Publish (clean, self-contained distributable folder)
& $msb editor\src\VideoTrim.App\VideoTrim.App.csproj `
    /t:Restore,Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
    /p:PublishDir=bin\publish\
#   → editor\src\VideoTrim.App\bin\publish\VideoTrim.App.exe
```

Use `/p:Platform=ARM64 /p:RuntimeIdentifier=win-arm64` for an ARM64 build. Distribute the **entire**
output folder (≈550 MB — most of it is the two static GPL FFmpeg binaries); double-click
`VideoTrim.App.exe` to run. Bundling a GPL FFmpeg has licensing implications — see `decisions.md`
(decision 25).

### One single `.exe` (self-extracting)

You can collapse the whole folder into a **single** `.exe` with .NET single-file publishing. Add the
two single-file flags to the publish command:

```powershell
& $msb editor\src\VideoTrim.App\VideoTrim.App.csproj `
    /t:Restore,Publish /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 `
    /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishDir=bin\publish_single\
#   → editor\src\VideoTrim.App\bin\publish_single\VideoTrim.App.exe   (one ~540 MB file)
```

This embeds the .NET runtime, the Windows App Runtime native DLLs, **and** the bundled FFmpeg
binaries into a single exe. On first launch it self-extracts to `%TEMP%\.net\VideoTrim.App\<hash>\`
and the app's `FfmpegLocator` finds `ffmpeg.exe` there (verified working). Caveats:

- It's a *self-extracting* single file, not a run-from-memory one: first launch is slower and writes
  ~540 MB to the temp folder. Native components (WinUI, FFmpeg) can't run from inside the exe.
- WinUI 3 single-file publishing isn't officially fully supported by Microsoft — test before shipping
  and re-verify after Windows App SDK updates.
- The file is still ~540 MB (mostly the two FFmpeg binaries). To shrink it, swap the static GPL
  FFmpeg for a smaller shared/essentials build, or drop encoders you don't use.

If you prefer a conventional installer instead of a fat exe, wrap the normal publish folder with
Inno Setup or build an MSIX package — both give "one file to hand someone" without the temp-extract
behavior.



## Features → user stories

| Story | Feature |
|-------|---------|
| US1 | Open + play/pause + seek via the unified timeline (`MediaPlayerElement`). |
| US2 | Dual trim handles with kept-range highlight and scrub-to-preview; clamped, frame-snapped. |
| US3 | Export to MP4/MOV/MKV/WebM with resolution, fps, and CRF/bitrate; progress + cancel. |
| US4 | Target output size → two-pass encode at a computed bitrate, with feasibility warnings. |
| US5 | Include/exclude audio and choose audio bitrate (subtracted from the size budget). |
| US6 | GIF export via palettegen/paletteuse with color/fps/dither and optional target size. |
