# Engineering Design Document — Lightweight Video Trim & Export App

**Status:** Draft for implementation
**Author:** Engineering
**Source of truth for requirements:** `user-stories-and-eng-tips.md`
**Audience:** The engineer(s) implementing this application end-to-end.

---

## 0. How to read this document

This document is intended to be complete enough that an engineer can implement the
application without needing further clarification. Where the user stories or engineering
tips left something ambiguous or unspecified, a decision has been made and called out
explicitly in an **> Assumption** callout. All assumptions are also consolidated in
[Section 16](#16-consolidated-assumptions).

If an assumption turns out to be wrong, only the local section it appears in should need
to change — the architecture is intentionally decoupled so that decisions are swappable.

---

## 1. Overview

### 1.1 Product summary

A small, fast Windows desktop application that lets a user:

1. Open a video file and play it back like any normal video player (play/pause, seek/scrub).
2. **Temporally** crop (trim) the video by dragging two handles on a timeline — the start
   handle and the end handle — while seeing the corresponding frame in the preview.
3. Export the trimmed video with control over resolution, bitrate/compression, FPS, and
   container/format. Spatial cropping is explicitly **out of scope** — only the time range changes.
4. Optionally request a **target output file size** (e.g. "≤ 10 MB"); the app computes the
   encoding bitrate needed to land at or under that size for the chosen resolution/FPS/format.
5. Configure audio export settings (audio bitrate, or exclude audio entirely).
6. Export to **GIF** instead of a video container, with control over compression level,
   resolution, FPS, and an optional target file size.

### 1.2 Goals

- Cover all six user stories.
- Be lightweight, responsive, and simple to build and maintain. The encoding work is done by
  FFmpeg; the app is essentially a thin, well-structured UI + orchestration layer over FFmpeg.
- Keep the UI minimal — default WinUI 3 styling is acceptable. Focus engineering effort on
  correct behavior, intuitive component placement, and a responsive (never-frozen) UI.

### 1.3 Non-goals (explicitly out of scope)

- Spatial cropping, rotation, filters, color grading, overlays, or text.
- Multi-clip timelines, transitions, or joining multiple sources.
- Frame-by-frame editing or keyframe animation.
- Cloud features, accounts, or telemetry.
- Cross-platform support (Windows only, per the WinUI 3 requirement).

> **Assumption (scope):** The app edits **one** source video at a time and produces **one**
> output. The user stories only ever refer to "the video" (singular) and a single export, so
> multi-input/timeline editing is treated as out of scope. This keeps the model and UI simple.

---

## 2. Glossary

| Term | Meaning |
|------|---------|
| **Trim / temporal crop** | Selecting a `[start, end]` time sub-range of the source; everything outside is discarded. No spatial change. |
| **Playhead** | The current playback position indicator on the timeline. |
| **Trim handles** | The two draggable markers defining the start and end of the kept range. |
| **Container / format** | The output file wrapper (MP4, MOV, WebM, GIF, …). |
| **Codec** | The compression algorithm inside the container (H.264, VP9, …). |
| **CRF** | Constant Rate Factor — quality-targeted encoding (lower = higher quality/bigger file). |
| **Two-pass** | Encoding the video twice so the encoder can hit a precise average bitrate / target size. |
| **Probe** | Reading a media file's metadata (duration, resolution, fps, codecs) via `ffprobe`. |

---

## 3. Technology stack & key decisions

| Concern | Decision | Rationale |
|---------|----------|-----------|
| UI framework | **WinUI 3** (Windows App SDK) | Required by the engineering tips. |
| Language / runtime | **C# on .NET 8 (LTS)** | Standard pairing for WinUI 3 / Windows App SDK; LTS for stability. |
| App model | **Packaged or unpackaged WinUI 3 desktop app**, MVVM | MVVM keeps UI responsive and logic testable. |
| Media playback | **`MediaPlayerElement`** (`Windows.Media.Playback`) | Built-in, hardware-accelerated, supports play/pause/seek out of the box. |
| Encoding backend | **FFmpeg via a CLI-wrapper binding** (`FFMpegCore`) + **bundled `ffmpeg.exe` / `ffprobe.exe`** | Simplest possible code path that still exposes every option the user stories need (bitrate, two-pass, scale, fps, GIF palette). See §3.1. |
| Async / threading | `async`/`await`, `IProgress<T>`, `CancellationToken` | Keep the UI thread free; all FFmpeg work runs off-thread. |
| MVVM helpers | `CommunityToolkit.Mvvm` | Lightweight source-generated `ObservableObject` / `RelayCommand`; avoids boilerplate. |

### 3.1 Why FFmpeg-as-a-process instead of native bindings

Three families of FFmpeg integration exist for .NET:

1. **CLI wrappers** (`FFMpegCore`, `Xabe.FFmpeg`) — build an `ffmpeg` command line and run the
   bundled `ffmpeg.exe` as a child process, parsing stdout/stderr for progress.
2. **Native P/Invoke bindings** (`FFmpeg.AutoGen`) — call `libav*` directly in-process.
3. Writing our own decode/encode pipeline.

**Decision: use a CLI wrapper (`FFMpegCore`) over a bundled FFmpeg binary.**

- It is by far the **simplest in terms of code** — exactly the stated priority. Every feature
  here (trim, scale, fps, two-pass target bitrate, audio bitrate, GIF palette generation) maps to
  a small, well-understood FFmpeg command line.
- No manual native memory management or marshalling (which `FFmpeg.AutoGen` requires).
- Robust cancellation and progress: we own the child process and can kill it / parse its progress
  output.
- "Lightweight" is preserved: we ship a single static `ffmpeg.exe` + `ffprobe.exe` (LGPL build).

> **Assumption (binding):** `FFMpegCore` is the chosen wrapper. `Xabe.FFmpeg` is an acceptable
> drop-in alternative with the same architecture. The encoding logic is isolated behind an
> `IEncodingService` interface (§6.4), so the concrete wrapper can be swapped without touching the
> UI or view-models. If a future requirement needs in-process frame access beyond what `ffprobe`
> thumbnails provide, `FFmpeg.AutoGen` can be introduced behind the same interface.

> **Assumption (FFmpeg distribution & licensing):** We bundle a prebuilt **LGPL** static
> `ffmpeg.exe`/`ffprobe.exe` in the app package under e.g. `Assets/ffmpeg/`. The chosen build must
> include the encoders referenced in §10 (`libx264`, `libvpx-vp9`, `aac`, `libopus`, `gif`). If a
> GPL build (e.g. with `libx265`) is ever bundled, the app's distribution license must be revisited.
> FFmpeg is invoked as a separate executable, which is the LGPL-friendly integration model.

### 3.2 Why `MediaPlayerElement` for playback

`MediaPlayerElement` + its `MediaPlayer.PlaybackSession` gives us, for free:

- Play/pause/stop, volume.
- `Position` (get/set `TimeSpan`) for seeking and reading the playhead — this is the core of
  scrub-to-preview (User Story 2).
- `NaturalDuration` for the timeline length.
- Hardware-accelerated rendering.

It does **not** expose random-access frame extraction. That is fine: scrub-to-preview is
implemented by setting `PlaybackSession.Position`, which displays the frame at that time. Optional
timeline thumbnails (a "filmstrip") are generated with `ffmpeg`/`ffprobe` (see §7.2) and are a
nice-to-have, not required for the core story.

---

## 4. High-level architecture

The app follows **MVVM** with a thin service layer. FFmpeg is the only heavy dependency and it
lives entirely behind services.

```
┌──────────────────────────────────────────────────────────────────────┐
│                              Views (XAML)                              │
│  MainWindow / MainPage · TimelineControl · ExportPanel · GifPanel      │
│  (data-bound only — no business logic)                                 │
└───────────────▲───────────────────────────────────────────┬──────────┘
                │ x:Bind / Commands                          │ events
┌───────────────┴───────────────────────────────────────────▼──────────┐
│                            ViewModels                                  │
│  MainViewModel · ExportViewModel · GifViewModel                       │
│  (state, validation, commands, progress; no UI types, no FFmpeg args) │
└───────────────▲───────────────────────────────────────────┬──────────┘
                │                                            │
┌───────────────┴────────────────────────────────────────────▼─────────┐
│                              Services                                  │
│  IMediaProbeService   →  ffprobe: duration / resolution / fps / codecs │
│  IEncodingService     →  ffmpeg: trim + transcode + GIF (progress/cancel)│
│  ITargetSizeCalculator→  bitrate math for "target size"                 │
│  IThumbnailService    →  ffmpeg: optional filmstrip frames (optional)   │
│  IFilePickerService   →  open/save dialogs (testable wrapper)           │
└───────────────────────────────────▲──────────────────────────────────┘
                                     │ child process
                          ┌──────────┴───────────┐
                          │ Bundled ffmpeg.exe /  │
                          │ ffprobe.exe (LGPL)    │
                          └──────────────────────┘
```

### 4.1 Project layout

```
/src
  /VideoTrim.App            (WinUI 3 packaged app: Views + App startup + DI wiring)
    App.xaml(.cs)
    MainWindow.xaml(.cs)
    /Views
      MainPage.xaml(.cs)
      ExportPanel.xaml(.cs)
      GifPanel.xaml(.cs)
    /Controls
      TimelineControl.xaml(.cs)
    /Assets/ffmpeg/ffmpeg.exe, ffprobe.exe
  /VideoTrim.Core           (class library: no UI dependency — fully unit-testable)
    /Models
      MediaInfo.cs
      TrimRange.cs
      ExportSettings.cs
      GifSettings.cs
      AudioSettings.cs
      EncodeProgress.cs
      OutputFormat.cs (enum + capability table)
    /Services
      IMediaProbeService.cs / MediaProbeService.cs
      IEncodingService.cs   / FfmpegEncodingService.cs
      ITargetSizeCalculator.cs / TargetSizeCalculator.cs
      IThumbnailService.cs  / ThumbnailService.cs
    /ViewModels
      MainViewModel.cs
      ExportViewModel.cs
      GifViewModel.cs
  /VideoTrim.Core.Tests     (unit tests for Core — esp. the size calculator)
```

> **Assumption (project split):** View-models live in `VideoTrim.Core` (no UI types) so they can be
> unit-tested without spinning up WinUI. Views in `VideoTrim.App` are pure XAML + code-behind that
> only forward to view-models. This is a common, low-risk WinUI MVVM layout.

### 4.2 Dependency injection / startup

Use `Microsoft.Extensions.DependencyInjection`. In `App.OnLaunched`, register all services and
view-models as singletons/transients and resolve `MainViewModel` for the main page. This keeps
construction explicit and makes services mockable in tests.

---

## 5. Data models (Core)

```csharp
// Immutable description of the loaded source, from ffprobe.
public sealed record MediaInfo(
    string FilePath,
    TimeSpan Duration,
    int Width,
    int Height,
    double FrameRate,        // e.g. 29.97
    string VideoCodec,       // e.g. "h264"
    bool HasAudio,
    string? AudioCodec,
    long FileSizeBytes);

// The kept time range. Invariant: 0 <= Start < End <= source duration.
public sealed record TrimRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

public enum OutputFormat { Mp4, Mov, Webm, Mkv, Gif }

public sealed class AudioSettings
{
    public bool IncludeAudio { get; set; } = true;
    public int AudioBitrateKbps { get; set; } = 128;   // ignored if !IncludeAudio
}

public sealed class ExportSettings
{
    public OutputFormat Format { get; set; } = OutputFormat.Mp4;
    public int? TargetHeight { get; set; }   // null = keep source height; width auto (keep aspect)
    public double? TargetFps { get; set; }   // null = keep source fps
    public AudioSettings Audio { get; set; } = new();

    // Bitrate strategy — exactly one is active:
    public BitrateMode Mode { get; set; } = BitrateMode.Quality;
    public int QualityCrf { get; set; } = 23;       // used when Mode == Quality
    public int? VideoBitrateKbps { get; set; }       // used when Mode == Bitrate
    public long? TargetSizeBytes { get; set; }       // used when Mode == TargetSize
}

public enum BitrateMode { Quality, Bitrate, TargetSize }

public sealed class GifSettings
{
    public int? TargetHeight { get; set; }       // null = keep source
    public double TargetFps { get; set; } = 15;  // GIFs are typically low-fps
    public int MaxColors { get; set; } = 256;    // 2..256 — primary "compression" knob
    public DitherMode Dither { get; set; } = DitherMode.Bayer;
    public long? TargetSizeBytes { get; set; }   // optional
}

public enum DitherMode { None, Bayer, FloydSteinberg }

// Streamed back to the UI during encoding.
public sealed record EncodeProgress(
    double Percent,           // 0..100, best-effort
    TimeSpan ProcessedTime,   // how much of the trimmed range is done
    double? SpeedX,           // encode speed multiple (e.g. 3.2x), if parseable
    string RawLine);          // last ffmpeg status line (for diagnostics/log)
```

---

## 6. Services (the engine)

All services are interfaces in Core with FFmpeg-backed implementations. All long-running methods
are `async`, accept a `CancellationToken`, and report via `IProgress<EncodeProgress>`.

### 6.1 `IMediaProbeService`

```csharp
Task<MediaInfo> ProbeAsync(string filePath, CancellationToken ct);
```

Runs `ffprobe -v quiet -print_format json -show_format -show_streams <file>` and maps the JSON to
`MediaInfo`. Frame rate comes from the `r_frame_rate` "num/den" string (parse and divide). This is
the single source of truth for duration, dimensions, fps, and whether an audio stream exists.

### 6.2 `IFilePickerService`

Thin wrapper over WinUI file open/save pickers, returning paths. Wrapped so view-models stay
UI-free and testable.

### 6.3 `IThumbnailService` (optional / nice-to-have)

```csharp
Task<IReadOnlyList<ThumbFrame>> GenerateFilmstripAsync(
    string filePath, int count, int thumbHeight, CancellationToken ct);
```

Extracts `count` evenly-spaced frames for the timeline background (§7.2). Not required for the core
scrub-to-preview behavior; ship core first, add this later.

### 6.4 `IEncodingService` — the core export engine

```csharp
Task ExportVideoAsync(
    string inputPath, string outputPath,
    TrimRange range, ExportSettings settings,
    MediaInfo source,
    IProgress<EncodeProgress> progress, CancellationToken ct);

Task ExportGifAsync(
    string inputPath, string outputPath,
    TrimRange range, GifSettings settings,
    MediaInfo source,
    IProgress<EncodeProgress> progress, CancellationToken ct);
```

Responsibilities:

- Translate `ExportSettings`/`GifSettings` + `TrimRange` into FFmpeg command line(s) (§10).
- For two-pass / target-size and for GIF, orchestrate the multi-step process.
- Parse FFmpeg progress and surface `EncodeProgress`.
- Honor cancellation by killing the child process and deleting partial output.

**Progress parsing:** invoke ffmpeg with `-progress pipe:1 -nostats` to get machine-readable
`out_time_ms=…`, `speed=…`, `progress=continue/end` key-value lines on stdout. Percent =
`processed_out_time / trimmed_duration`. For two-pass, map pass 1 to 0–50% and pass 2 to 50–100%.
For GIF, map palette-gen to 0–20% and palette-apply to 20–100%. (These split ratios are cosmetic.)

### 6.5 `ITargetSizeCalculator` — bitrate math (pure, unit-tested)

```csharp
// Returns the video bitrate (kbps) to request from a two-pass encode, or a
// TargetSizeResult that says "infeasible" so the UI can warn instead of silently
// producing a worse-than-expected file.
TargetSizeResult ComputeVideoBitrate(
    long targetSizeBytes, TimeSpan duration, AudioSettings audio);
```

See [Section 9](#9-algorithms) for the formula and edge cases. This class has **no FFmpeg
dependency** and is the most important thing to unit-test.

---

## 7. Feature design (mapped to user stories)

### 7.1 US1 — Video preview & playback

**UI:** `MediaPlayerElement` fills the top region. A single "Open video" button (or drag-and-drop
onto the window) triggers `IFilePickerService` → `IMediaProbeService.ProbeAsync` → bind the source
into the player via `MediaSource.CreateFromUri/StorageFile`.

**Controls:** Play/Pause toggle, current-time / duration labels, and the timeline (§7.2) which
doubles as the seek bar. Set `MediaPlayerElement.AreTransportControlsEnabled = false` and provide
our own controls so the same timeline serves both seeking and trimming (single, unified control is
more intuitive than two separate bars).

> **Assumption (transport controls):** We use a **custom** transport row + custom timeline rather
> than the built-in `MediaTransportControls`, because the timeline must also host the two trim
> handles. One unified timeline (playhead + two trim handles) is clearer than a built-in seek bar
> plus a separate trim bar.

**Playback ↔ timeline sync:** A ~30 Hz `DispatcherQueueTimer` reads
`PlaybackSession.Position` and updates the playhead while playing. When the user drags the playhead,
write `PlaybackSession.Position` to seek. Standard two-way pattern; guard against feedback loops with
an "is user dragging" flag.

### 7.2 US2 — Trim with dual handles + scrub-to-preview

**TimelineControl** (custom `UserControl`) renders, left-to-right across the source duration:

```
 0:00                                                         2:30
 ┌───────────────────────────────────────────────────────────────┐
 │░░░░░░░░│██████████████████████████████████████│░░░░░░░░░░░░░░░░│   ← track
 │        ▲start handle        ▲ playhead         ▲end handle     │
 └───────────────────────────────────────────────────────────────┘
   (dim = excluded)      (bright = kept range)      (dim = excluded)
```

- The track maps `time ↔ x` linearly: `x = (t / duration) * trackWidth`.
- Optional filmstrip thumbnails (§6.3) render as the track background.
- Three draggable elements implemented with `Thumb` controls (or pointer events on `Border`s):
  **start handle**, **end handle**, **playhead**.
- The region between the trim handles is highlighted; outside is dimmed.

**Bindings / outputs:** `TimelineControl` exposes `TrimStart`, `TrimEnd`, and `PlayheadPosition`
(`TimeSpan`, two-way) plus a `SeekRequested` event. `MainViewModel` owns the authoritative
`TrimRange`.

**Scrub-to-preview (the core of US2):** While a trim handle is being dragged, set
`MediaPlayer.PlaybackSession.Position` to that handle's time on each move (throttled to ~15–20 Hz to
avoid seek spam). This makes the preview show exactly the frame the handle is on, so the user can see
where they're placing the cut. Pause playback during a drag for a stable preview.

**Constraints / invariants enforced in the view-model:**

- `Start < End`, with a minimum gap (e.g. ≥ 0.1 s, or ≥ 1 frame).
- `0 ≤ Start` and `End ≤ Duration`.
- Dragging start past end (or vice-versa) clamps rather than crossing.
- Playhead is clamped to `[0, Duration]`. (It may move outside the trim range during plain
  playback; that's fine — trim handles only define the export range.)

> **Assumption (handle precision):** Trim precision is to the **frame** where possible (snap handle
> time to the nearest frame using `source.FrameRate`), falling back to millisecond precision. This
> matches the "intuitive, see-where-I-am" intent without exposing frame-number UI.

> **Assumption (preview-while-dragging keyframe accuracy):** Seeking via `PlaybackSession.Position`
> during a drag may land on the nearest decodable frame rather than the exact frame for some codecs.
> This is acceptable for *preview*. The actual exported cut is frame-accurate because export
> re-encodes with accurate seeking (§10.1).

### 7.3 US3 — Export with options (resolution, bitrate, FPS, format)

**ExportPanel** (right-side panel or a dialog) bound to `ExportViewModel`/`ExportSettings`:

| Control | Binds to | Notes |
|---------|----------|-------|
| Format dropdown | `Format` | MP4, MOV, MKV, WebM (and GIF routes to GifPanel, §7.6). |
| Resolution dropdown | `TargetHeight` | "Same as source", 2160p, 1440p, 1080p, 720p, 480p, 360p. Width auto to keep aspect. |
| FPS dropdown | `TargetFps` | "Same as source", 60, 30, 24, 15. Only offer ≤ source fps + "Same". |
| Quality mode radio | `Mode` | **Quality (CRF)** / **Target bitrate** / **Target size**. |
| Quality slider | `QualityCrf` | Visible when Mode = Quality. Label as "Smaller ⟷ Better quality". |
| Bitrate input | `VideoBitrateKbps` | Visible when Mode = Bitrate. |
| Target size input + unit | `TargetSizeBytes` | Visible when Mode = TargetSize (§7.4). |
| Audio sub-section | `AudioSettings` | §7.5. |
| Export button | command | Opens save dialog → runs `ExportVideoAsync`. |

Aspect ratio and framing are preserved (only height is chosen; width is derived) — satisfying "keep
the aspect ratio and framing as is, just crop time."

> **Assumption (format → codec mapping):** The user chooses a **container**; the app picks a sane
> default **codec** per container (table in §10). This is simpler and more intuitive than asking
> users to choose codecs. Mapping: MP4/MOV/MKV → H.264 (`libx264`) + AAC; WebM → VP9 (`libvpx-vp9`)
> + Opus. Codec selection can be exposed later as an "advanced" toggle without architectural change.

> **Assumption (default quality):** Default `Mode = Quality`, `CRF = 23` (visually near-lossless for
> H.264 at reasonable size) so a user who just clicks "Export" gets a good result with zero config.

### 7.4 US4 — Targeted output size

When `Mode = TargetSize`, the user enters a number + unit (MB/KB). The flow:

1. `ITargetSizeCalculator.ComputeVideoBitrate(targetSizeBytes, range.Duration, audio)` →
   either a feasible **video bitrate (kbps)** or an `Infeasible` result.
2. If infeasible (computed video bitrate ≤ a minimum floor), the UI shows an inline warning
   ("Can't reach 5 MB at 1080p/30 — try a lower resolution or fps") and disables Export until the
   user adjusts. (Math + policy in §9.1.)
3. If feasible, run a **two-pass** encode at that bitrate (§10.2). Two-pass is what makes the final
   size land predictably near the target.

Target size and the chosen resolution/FPS/format are independent inputs, exactly as the story
describes ("based on the provided resolution, FPS, and video format"): resolution/fps affect
*quality at that bitrate*, the calculator only converts size+duration into a bitrate budget.

### 7.5 US5 — Audio settings

A sub-section in the export panel:

- **Include audio** checkbox (`AudioSettings.IncludeAudio`). Hidden/disabled if the source has no
  audio (`MediaInfo.HasAudio == false`).
- **Audio bitrate** dropdown (`AudioBitrateKbps`): 320 / 256 / 192 / 128 / 96 / 64 kbps. Disabled
  when "include audio" is off.

The audio bitrate is fed into the target-size budget (it's subtracted before computing the video
bitrate — §9.1) so audio is correctly accounted for when hitting a size target. Excluding audio adds
`-an` and frees the entire budget for video.

### 7.6 US6 — GIF conversion

GIF is selected as a "format", but it has no audio and no bitrate concept, so it gets its **own
panel** (`GifPanel` / `GifSettings`) rather than reusing the video bitrate/audio controls.

Controls:

| Control | Binds to | Notes |
|---------|----------|-------|
| Resolution dropdown | `TargetHeight` | Same options as video; GIFs are usually downscaled. |
| FPS dropdown | `TargetFps` | Default 15; offer 8/10/12/15/20/24. |
| Colors / compression slider | `MaxColors` | 2..256. **Primary compression knob** — fewer colors = smaller file. Labelled "More compression ⟷ Better quality". |
| Dither dropdown | `Dither` | None / Bayer / Floyd–Steinberg. |
| Target size input | `TargetSizeBytes` | Optional; iterative approach in §9.2. |
| Export button | command | Save dialog → `ExportGifAsync`. |

GIF generation uses the standard high-quality **palettegen → paletteuse** two-step (§10.3) so output
isn't the ugly default 256-web-palette GIF.

> **Assumption (GIF "compression level"):** The user story's "compression levels" for GIF is mapped
> to **palette color count** (`MaxColors`, 2–256) plus dithering, because GIF has no bitrate. This is
> the standard, effective way to trade GIF size vs quality.

---

## 8. UI / UX layout

Single main window, default WinUI 3 styling. Suggested layout:

```
┌──────────────────────────────────────────────────────────────────────────┐
│  [ Open Video ]                                  filename.mp4 · 1080p·30fps │ ← top bar
├───────────────────────────────────────────────┬────────────────────────────┤
│                                                │   EXPORT                    │
│                                                │   Format:   [ MP4      ▼]   │
│              MediaPlayerElement                │   Resolution:[Same     ▼]   │
│                 (preview)                      │   FPS:      [Same      ▼]   │
│                                                │   Mode: (•)Quality          │
│                                                │         ( )Bitrate          │
│                                                │         ( )Target size      │
│                                                │   Quality:  [====o-----]    │
│                                                │   ── Audio ──               │
│                                                │   [x] Include audio         │
│                                                │   Bitrate:  [128 kbps  ▼]   │
├────────────────────────────────────────────────┤                            │
│  ⏯  00:12 / 02:30                               │                            │
│  ┌────────────────────────────────────────────┐ │                            │
│  │░░░│███████████████████████│░░░░░░░░░░░░░░░░░│ │   [   Export   ]           │
│  └────────────────────────────────────────────┘ │   [progress bar……] [Cancel]│
│        TimelineControl (seek + trim)            │                            │
└────────────────────────────────────────────────┴────────────────────────────┘
```

Principles (per engineering tips):

- Default styles; no custom theming required. Use standard `Button`, `ComboBox`, `Slider`,
  `RadioButtons`, `ProgressBar`, `InfoBar`.
- Placement matters: preview on top, transport + unified timeline directly under it (natural reading
  order), export options grouped on the right, primary action (Export) clearly separated at the
  bottom of the panel.
- Mode-specific controls (Quality slider / Bitrate box / Target-size box) are shown/hidden by the
  selected radio so the panel is never cluttered.
- A dismissible `InfoBar` is used for warnings (e.g. infeasible target size) and errors.

> **Assumption (GIF panel surfacing):** Choosing `GIF` in the Format dropdown swaps the right-side
> panel's body to the GIF controls (audio/quality controls hidden). One panel that morphs is simpler
> and less confusing than a separate window.

---

## 9. Algorithms

### 9.1 Target file size → video bitrate (video export)

Goal: pick a video bitrate so the final file is **at or just under** the target.

```
Let:
  targetBytes      = user target (after MB/KB → bytes)            [bytes]
  duration         = trimmed range duration                      [seconds]
  audioKbps        = audio.IncludeAudio ? audio.AudioBitrateKbps : 0
  SAFETY           = 0.97   // 3% headroom for container/muxing overhead + VBV slack
  MIN_VIDEO_KBPS   = 100    // feasibility floor (below this, quality is pointless)

budgetBits      = targetBytes * 8 * SAFETY
audioBits       = audioKbps * 1000 * duration
videoBits       = budgetBits - audioBits
videoKbps       = floor( videoBits / (1000 * duration) )

if videoKbps < MIN_VIDEO_KBPS  → return Infeasible(suggested: lower res/fps or raise size)
else                            → return Feasible(videoKbps)
```

Notes & decisions:

- **Bits vs bytes / kbps:** bitrate is expressed in **kbps where 1 kbps = 1000 bit/s** (FFmpeg's
  convention via `-b:v 2500k`). File size targets use **binary** units in the UI (1 MB = 1024×1024
  bytes) because that matches what Windows/most upload limits show, but the unit is configurable.
- **SAFETY factor** absorbs container overhead and the encoder's bitrate inaccuracy so we land
  *under* the limit. 0.97 is a starting value; tune from real measurements during QA.
- **Two-pass is mandatory** for target-size mode (single-pass average-bitrate is much less accurate).
- Audio bitrate is subtracted first, so US5 and US4 compose correctly.

> **Assumption (size units):** Target size is interpreted as **binary MB/KB (MiB/KiB)** by default,
> shown next to the field, with a unit selector. Rationale: upload limits like "10 MB" are usually
> enforced as binary. If a platform means decimal MB, the user can pick the smaller value.

> **Assumption (overhead model):** A flat 3% safety margin is used instead of modelling exact
> container overhead. It's simple and robust; the value is a single constant to tune.

### 9.2 Target file size → GIF (iterative)

GIF size is a non-linear function of resolution, fps, color count, and *content motion*, so there's
no closed-form bitrate. Strategy: **estimate, encode, measure, refine** with a bounded loop.

```
Inputs: targetBytes, base GifSettings (user's chosen res/fps/colors are the starting upper bound)
MAX_ITERS = 5

1. Encode once with current settings → measure actual size.
2. If size <= targetBytes  → done (success).
3. Else reduce size by stepping knobs in this priority order (each step, re-encode & measure):
      a. MaxColors: 256 → 128 → 64 → 32   (biggest, lowest-quality-loss wins first)
      b. FPS:       reduce toward 10 (e.g. ×0.75 steps, floor 8)
      c. Height:    scale down ×0.85 (floor ~144p)
4. Stop when size <= targetBytes OR MAX_ITERS reached.
5. If still over target after MAX_ITERS → return best-effort smallest result + warn the user.
```

- Each iteration reuses the palettegen/paletteuse pipeline (§10.3).
- Bound the loop (`MAX_ITERS`) so it stays responsive; report progress per iteration.
- This is intentionally simple (greedy, not optimal). It reliably gets at or under target for typical
  short clips, which is the use case (sharing small GIFs).

> **Assumption (GIF target approach):** Because GIF has no bitrate, a bounded iterative
> encode-measure-adjust loop is used, prioritizing color reduction, then fps, then resolution. This
> trades a few extra encodes for predictability while staying responsive.

### 9.3 Trim accuracy

Export always **re-encodes** (because resolution/fps/bitrate can change), so we use **accurate
input seeking**: place `-ss <start>` *before* `-i` and `-to`/`-t` to bound the range. Modern FFmpeg
decodes from the preceding keyframe and trims to the exact requested start when re-encoding, giving
frame-accurate cuts without the slowness of output seeking.

> **Assumption (no lossless stream-copy fast path):** We do **not** offer a `-c copy` "fast trim"
> path. It can only cut on keyframes (imprecise) and conflicts with changing resolution/fps/bitrate.
> Always re-encoding keeps behavior consistent and frame-accurate, at the cost of some speed —
> acceptable for an editing tool and far simpler to reason about.

---

## 10. FFmpeg command reference

`{in}` = source path, `{out}` = output path, `{ss}`/`{to}` = trim start/end (seconds, e.g. `12.500`).
`-y` overwrites; `-hide_banner` quiets; `-progress pipe:1 -nostats` enables machine-readable progress.

### 10.0 Format → codec/extension table

| `OutputFormat` | Container/ext | Video codec | Audio codec | Notes |
|----------------|---------------|-------------|-------------|-------|
| Mp4  | `.mp4` | `libx264` | `aac` | Add `-movflags +faststart` for web playback. |
| Mov  | `.mov` | `libx264` | `aac` | |
| Mkv  | `.mkv` | `libx264` | `aac` | |
| Webm | `.webm`| `libvpx-vp9` | `libopus` | VP9 is slower; fine for short clips. |
| Gif  | `.gif` | (palette)  | — | See §10.3; no audio. |

Common video filter chain (built from settings; omit parts that are "same as source"):

```
-vf "scale=-2:{height},fps={fps}"
```

`scale=-2:{height}` keeps aspect ratio and forces an even width (required by H.264/VP9). If
`TargetHeight` is null, omit `scale`. If `TargetFps` is null, omit `fps`.

### 10.1 Quality (CRF) export — single pass, default path

```
ffmpeg -y -hide_banner -ss {ss} -to {to} -i "{in}" \
  -vf "scale=-2:{height},fps={fps}" \
  -c:v libx264 -crf {crf} -preset medium -pix_fmt yuv420p \
  {audioArgs} -movflags +faststart \
  -progress pipe:1 -nostats "{out}.mp4"
```

`{audioArgs}` =
- include audio: `-c:a aac -b:a {audioKbps}k`
- exclude audio: `-an`

`-pix_fmt yuv420p` ensures broad player compatibility. Swap codec/audio per §10.0 for other formats
(VP9 uses `-c:v libvpx-vp9 -crf {crf} -b:v 0` for constant-quality, `-c:a libopus`).

### 10.2 Target-bitrate / target-size export — two-pass

Used for `Mode = Bitrate` (user-given bitrate) and `Mode = TargetSize` (computed via §9.1). `{vk}` =
video bitrate in kbps.

```
# Pass 1 (no audio, discard output)
ffmpeg -y -hide_banner -ss {ss} -to {to} -i "{in}" \
  -vf "scale=-2:{height},fps={fps}" \
  -c:v libx264 -b:v {vk}k -pass 1 -preset medium -pix_fmt yuv420p \
  -an -f mp4 -progress pipe:1 -nostats NUL

# Pass 2 (real output + audio)
ffmpeg -y -hide_banner -ss {ss} -to {to} -i "{in}" \
  -vf "scale=-2:{height},fps={fps}" \
  -c:v libx264 -b:v {vk}k -pass 2 -preset medium -pix_fmt yuv420p \
  {audioArgs} -movflags +faststart \
  -progress pipe:1 -nostats "{out}.mp4"
```

- On Windows the pass-1 null sink is `NUL`. The two passes share a `*-0.log` stats file; run both in
  the **same working directory** and clean up the log afterwards.
- For VP9 two-pass, the analogous flags are `-b:v {vk}k -pass 1/2` with `-c:v libvpx-vp9` and
  `-f webm` / `-f null`.

### 10.3 GIF export — palettegen + paletteuse

Two-step for high quality (single global palette). `{colors}` = `MaxColors`, `{dither}` = `none` /
`bayer:bayer_scale=3` / `floyd_steinberg`.

```
# Step 1 — generate an optimized palette for the trimmed/scaled/fps'd range
ffmpeg -y -hide_banner -ss {ss} -to {to} -i "{in}" \
  -vf "fps={fps},scale=-2:{height}:flags=lanczos,palettegen=max_colors={colors}" \
  "{palette}.png"

# Step 2 — apply palette to produce the GIF
ffmpeg -y -hide_banner -ss {ss} -to {to} -i "{in}" -i "{palette}.png" \
  -lavfi "fps={fps},scale=-2:{height}:flags=lanczos[x];[x][1:v]paletteuse=dither={dither}" \
  -progress pipe:1 -nostats "{out}.gif"
```

The palette PNG is written to a temp file and deleted afterwards. The iterative target-size loop
(§9.2) repeats both steps with adjusted `{colors}`/`{fps}`/`{height}`.

### 10.4 Probe (load)

```
ffprobe -v quiet -print_format json -show_format -show_streams "{in}"
```

---

## 11. Concurrency, responsiveness & process management

- **UI thread is sacred.** All probe/encode work runs via `await Task.Run`-wrapped process
  execution. The UI only marshals progress back via `IProgress<T>` (captured on the UI
  `SynchronizationContext`) / `DispatcherQueue`.
- **One export at a time.** Export button is disabled while an export runs; a Cancel button is shown.
  (Probing on file open is also async but fast.)
- **Cancellation:** `CancellationToken` → kill the ffmpeg child process tree and delete the partial
  `{out}` and any temp (`palette.png`, two-pass `*.log`). `FFMpegCore` supports cancellation; if
  driving the process directly, keep the `Process` handle and `Kill(entireProcessTree: true)`.
- **Seek throttling:** scrub-to-preview seeks are throttled (~15–20 Hz) and coalesced so dragging a
  handle never floods the player with seeks.
- **Temp files:** use a per-export temp subfolder under `Path.GetTempPath()`; always clean up in a
  `finally`.

---

## 12. Validation & error handling

| Situation | Handling |
|-----------|----------|
| Unsupported / corrupt input | `ffprobe` fails → `InfoBar` error "Couldn't read this file"; stay idle. |
| `Start >= End` | Prevented by timeline clamping; Export disabled if range invalid. |
| Trimmed duration ~0 | Enforce minimum range (§7.2); Export disabled otherwise. |
| Target size infeasible | §9.1 → inline warning + Export disabled until adjusted. |
| Target FPS > source FPS | Clamp to source (don't fabricate frames); only offer ≤ source in dropdown. |
| Target height > source height | Allow but warn (upscaling reduces quality); default options cap at source. |
| Output path locked / no space | Catch ffmpeg failure exit code → error `InfoBar` with the last stderr line. |
| Source has no audio | Hide/disable audio controls; never pass `-c:a`. |
| ffmpeg/ffprobe missing | Verify bundled binaries exist on startup; hard error if not (it's a packaging bug). |
| GIF target unreachable | §9.2 → produce smallest best-effort GIF + warn. |

All ffmpeg stderr is captured to an in-memory ring buffer and written to a session log file for
diagnostics; only a friendly summary is shown in the UI.

---

## 13. Testing strategy

**Unit tests (`VideoTrim.Core.Tests`) — highest priority, no FFmpeg needed:**

- `TargetSizeCalculator`: budget math, audio subtraction, safety margin, feasibility floor,
  boundary cases (zero/near-zero duration, audio excluded, huge/tiny targets). This is pure math and
  must be rock-solid.
- `TrimRange`/view-model invariants: clamping, min-gap, start/end crossing, fps snapping.
- Command-builder: given settings, assert the produced ffmpeg argument list (string-compare against
  golden expected args) — verifies §10 mappings without running ffmpeg. Mock `IEncodingService`'s arg
  builder as a pure function for this.

**Integration tests (run against the real bundled ffmpeg, gated/optional in CI):**

- Probe a known fixture → expected duration/res/fps.
- Trim+export a short fixture → output exists, `ffprobe` confirms duration ≈ trimmed range,
  resolution/fps match settings.
- Target-size export of a fixture at e.g. 2 MB → assert output ≤ target and within ~10% below.
- GIF export → output is a valid GIF of expected dimensions; target-size loop lands ≤ target.

**Manual / UX smoke tests:** open various real files; scrub; drag both handles and confirm preview
tracks the handle; export each format; cancel mid-export and confirm no partial/temp files remain.

> **Assumption (test fixtures):** A few short, redistributable sample clips (a few seconds, with and
> without audio, varied fps) are committed under `test-assets/` for integration tests.

---

## 14. Build, packaging & dependencies

- **NuGet:** `Microsoft.WindowsAppSDK`, `CommunityToolkit.Mvvm`, `FFMpegCore`,
  `Microsoft.Extensions.DependencyInjection`.
- **Bundled native:** `ffmpeg.exe`, `ffprobe.exe` (LGPL static build) under `Assets/ffmpeg/`, marked
  *Copy to output / included in package*. Configure `FFMpegCore`'s `BinaryFolder` to point at this
  path at startup.
- **Packaging:** MSIX (packaged) is the default for distribution; an unpackaged build is acceptable
  for dev. Either way the ffmpeg binaries ship alongside the app — no system FFmpeg dependency.
- **Min OS:** Windows 10 1809+ (Windows App SDK requirement).

> **Assumption (no system FFmpeg):** FFmpeg is always bundled, never assumed present on the user's
> machine, so the app works out-of-the-box. This adds ~tens of MB to the package — acceptable and
> still "lightweight" for a media tool.

---

## 15. Delivery milestones

Each milestone is independently demoable; ship in order.

| # | Milestone | Delivers | Covers |
|---|-----------|----------|--------|
| M1 | **Playback** | Open file, probe, `MediaPlayerElement` preview, play/pause, seek via timeline playhead. | US1 |
| M2 | **Trim** | Dual trim handles, kept-range highlight, scrub-to-preview, enforced invariants. | US2 |
| M3 | **Basic export** | Trim + re-encode to MP4 with resolution/fps/CRF; progress + cancel. | US3 (partial) |
| M4 | **Formats + audio** | MOV/MKV/WebM, codec mapping, audio include/exclude + bitrate. | US3, US5 |
| M5 | **Target size** | Two-pass + size calculator + feasibility warnings. | US4 |
| M6 | **GIF** | GIF panel + palettegen/paletteuse + iterative target size. | US6 |
| M7 | **Polish** | Filmstrip thumbnails, drag-and-drop open, logging, edge-case hardening. | — |

---

## 16. Consolidated assumptions

1. **Scope:** one source video → one output per session; no multi-clip timeline (§1.3).
2. **FFmpeg binding:** `FFMpegCore` CLI-wrapper over a bundled LGPL `ffmpeg.exe`/`ffprobe.exe`,
   isolated behind `IEncodingService` for swappability (§3.1).
3. **Licensing/distribution:** LGPL FFmpeg build invoked as a child process; required encoders
   bundled (§3.1).
4. **Project split:** view-models live in a UI-free Core library for testability (§4.1).
5. **Transport controls:** custom unified timeline (playhead + 2 trim handles) instead of built-in
   `MediaTransportControls` (§7.1).
6. **Trim precision:** snap to frame where possible, else ms; preview-seek may be near-frame, export
   is frame-accurate (§7.2, §9.3).
7. **Format vs codec:** user picks a container; app maps to a default codec, with mapping table
   (§7.3, §10.0).
8. **Defaults:** Quality/CRF 23 default so one-click export "just works" (§7.3).
9. **Size units:** target size interpreted as binary MB/KB by default, unit-selectable (§9.1).
10. **Overhead model:** flat 3% safety margin rather than exact container modelling (§9.1).
11. **GIF compression knob:** mapped to palette color count + dithering (§7.6).
12. **GIF target size:** bounded iterative encode-measure-adjust (colors → fps → resolution) (§9.2).
13. **No lossless `-c copy` fast-trim path:** always re-encode for accuracy/consistency (§9.3).
14. **GIF panel:** Format=GIF morphs the export panel rather than opening a new window (§8).
15. **FFmpeg always bundled:** never assume a system install (§14).
16. **Test fixtures:** small redistributable clips committed for integration tests (§13).

---

## 17. Open questions (non-blocking — defaults chosen above)

These have working defaults and don't block implementation; revisit if product priorities shift.

- Should codec selection ever be user-exposed (advanced mode), or is container-only selection
  permanent? (Default: container-only.)
- Should the filmstrip thumbnail strip (§7.2) ship in v1 or be deferred to M7? (Default: M7.)
- Hardware-accelerated encoders (NVENC/QSV/AMF) for speed — worth adding behind a "fast encode"
  toggle later? (Default: software `libx264` for predictable size/quality; revisit for large files.)
- Decimal vs binary size units default (§9.1) — confirm with target upload platforms.
```