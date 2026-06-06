# Implementation Decisions

This document records gaps, ambiguities, inconsistencies, and under-specified details found in
`eng-doc.md` while implementing the app in `editor/`, and the decision taken for each. The
engineering document already consolidates its own 16 assumptions in §16; the items below are the
*additional* decisions made during implementation. Section references (§) point at `eng-doc.md`.

---

## 1. FFmpeg integration: direct process instead of `FFMpegCore`
**Issue.** §3.1/§14 name `FFMpegCore` as the FFmpeg wrapper, but §3.1 also explicitly isolates the
backend behind `IEncodingService` and calls the wrapper swappable. The doc's own progress design
(`-progress pipe:1 -nostats`) and cancellation design (`Kill(entireProcessTree: true)`) describe
driving the process directly.
**Decision.** Drive `ffmpeg`/`ffprobe` directly via `System.Diagnostics.Process`, behind an
`IProcessRunner` abstraction, with `FfmpegEncodingService`/`MediaProbeService` implementing the Core
interfaces. This keeps the Core library dependency-free and fully buildable/testable with the .NET
SDK alone, and maps 1:1 onto the doc's progress/cancellation model. `FFMpegCore` (or `Xabe.FFmpeg`)
could replace `FfmpegEncodingService` behind the same interface without touching the UI or VMs.

## 2. Pure command builder extracted for golden-arg testing
**Issue.** §13 requires testing "the produced ffmpeg argument list … against golden expected args"
but the doc keeps command construction inside `IEncodingService`.
**Decision.** Factored a pure `IFfmpegCommandBuilder`/`FfmpegCommandBuilder` that returns **unquoted
token lists**. Tokens are passed to `ProcessStartInfo.ArgumentList` (which performs OS-correct
escaping), so paths/filenames need no manual quoting and golden tests compare clean tokens. All §10
commands are covered by `FfmpegCommandBuilderTests`.

## 3. Export methods return a result object
**Issue.** §6.4 signatures are `Task ExportVideoAsync(...)` / `Task ExportGifAsync(...)`, but §9.2
step 5 requires the GIF target-size flow to "return best-effort smallest result **+ warn the user**"
— which the caller cannot detect from a bare `Task`.
**Decision.** Both methods return `Task<ExportResult>` (`OutputPath`, `FinalSizeBytes`, `MetTarget`).
The view-model uses `MetTarget` to show a success vs. "couldn't reach target size" warning.

## 4. GIF iterative reducer — concrete step sizes
**Issue.** §9.2 specifies the *priority* (colors → fps → height) but leaves the discrete steps for
fps/height partly open ("×0.75 steps, floor 8", "×0.85, floor ~144p").
**Decision.** Implemented as a pure `GifSizeReducer`: colors `256→128→64→32`; fps
`round(fps×0.75)` with floor 8; height `floor(h×0.85)` forced even with floor 144; when
`TargetHeight` is null the source height is used as the height base; `MAX_ITERS = 5` total encodes.
One reduction step is applied per iteration, exhausting colors before fps before height.

## 5. `TargetSizeCalculator` degenerate inputs
**Issue.** The §9.1 formula divides by `duration` and assumes a positive target; it is undefined for
`duration ≤ 0` or `targetBytes ≤ 0`.
**Decision.** Return `Infeasible` with a friendly message for an empty/zero range or a non-positive
target size, rather than dividing by zero. Covered by tests.

## 6. Minimum trim gap
**Issue.** §7.2 says the minimum kept range is "≥ 0.1 s, **or** ≥ 1 frame" — ambiguous which.
**Decision.** `MinTrimGap = max(0.1 s, oneFrame)` so the gap is never below 0.1 s and is always at
least one frame, regardless of source fps.

## 7. Frame snapping rounding
**Issue.** §7.2 says "snap … to the nearest frame" without specifying rounding or the no-fps case.
**Decision.** `round(t × fps)` (half away from zero) `÷ fps`; if fps ≤ 0 (unknown), fall back to the
raw millisecond value (no snapping). Applied to both trim handles, not the playhead.

## 8. Progress `out_time` parsing
**Issue.** §6.4 references `out_time_ms`. In real ffmpeg, `out_time_ms` is historically emitted in
**microseconds**, which is ambiguous/error-prone.
**Decision.** Parse the unambiguous `out_time=HH:MM:SS.ffffff` field first, falling back to
`out_time_us` (microseconds). Implemented in the pure, unit-tested `FfmpegProgressParser`.

## 9. Two-pass log file is explicit
**Issue.** §10.2 relies on running both passes in the same working directory so they share the
`*-0.log` stats file.
**Decision.** Pass an explicit `-passlogfile <tempdir>/ffmpeg2pass` and run inside a per-export temp
directory that is always deleted in a `finally`. Same result, no reliance on ambient CWD, and clean
temp handling (§11).

## 10. Null sink is OS-aware
**Issue.** §10.2 hardcodes `NUL` (Windows-only).
**Decision.** Use `NUL` on Windows and `/dev/null` elsewhere so the Core library builds and its
tests run cross-platform; the shipping app is Windows-only as required.

## 11. Output path carries its own extension
**Issue.** §10 command samples write to `"{out}.mp4"`, implying the service appends the extension.
**Decision.** Services receive a complete final output path; the view-model derives the extension
from `FormatCatalog` when showing the save dialog. The builder writes `outputPath` verbatim to avoid
double extensions.

## 12. Audio is force-disabled when the source has none
**Issue.** §12 says "source has no audio → never pass `-c:a`", but the user could still toggle
"include audio".
**Decision.** The command builder emits `-an` whenever `!source.HasAudio` (or the container can't
carry audio, e.g. GIF) even if "include audio" is checked; the view-model additionally disables the
audio controls when the source has no audio stream.

## 13. Project organization / namespaces
**Issue.** §4.1 places every service under a single `/Services` folder.
**Decision.** Organized Core into `Models`, `Encoding`, `Probing`, `Services`, `Abstractions`, and
`ViewModels` namespaces for readability. View-models still live in the UI-free Core library (doc
assumption §16.4) so they are unit-testable without WinUI. This is purely structural.

## 14. Source-aware dropdown option catalogs
**Issue.** §7.3/§7.6 describe resolution/fps dropdowns "≤ source" and "Same as source" but do not
define the data.
**Decision.** Added `ExportOptionCatalog`: resolutions = `{Same, 2160/1440/1080/720/480/360}` capped
at source height; video fps = `{Same, 60/30/24/15}` capped at source fps; GIF fps =
`{24/20/15/12/10/8}` (default 15). "Same as source" is represented as a null height/fps.

## 15. FFmpeg binaries are not committed; located at runtime
**Issue.** §14 bundles `ffmpeg.exe`/`ffprobe.exe` (tens of MB). Committing binaries is undesirable
and no redistributable build is available in this environment.
**Decision.** Binaries are **not** committed. `Assets/ffmpeg/README.md` documents how to drop in an
LGPL build; `FfmpegLocator` resolves them from that folder and `EnsureAvailable()` hard-errors at
startup if missing (§12). When no folder is configured it falls back to the system `PATH` (useful for
development and the integration tests described in §13).

## 16. Integration-test fixtures not included
**Issue.** §13 (and assumption §16.16) call for small redistributable sample clips under
`test-assets/` for integration tests against the real ffmpeg.
**Decision.** Because ffmpeg is not bundled here and no redistributable clips are available, the
committed test suite is the **unit** layer (54 tests): target-size math, golden ffmpeg args, the GIF
reduction loop (driven by a fake process runner + scripted file sizes), progress parsing, ffprobe
JSON mapping, and the view-model trim invariants. The §13 integration tests are documented but not
run in this environment.

## 17. Build toolchain (environment finding)
**Issue.** `dotnet build` cannot compile the WinUI 3 XAML in this environment: the Windows App SDK's
net472 `XamlCompiler.exe` exits with code 1 and produces no diagnostics under the .NET CLI.
**Decision.** Build the WinUI app with **Visual Studio MSBuild**, which runs the XAML compiler
correctly (see `README.md`). The Core library and its tests build and pass with the plain .NET SDK
(`dotnet build` / `dotnet test`). This affects only how the app is built, not the app's code.

## 18. Single morphing export panel
**Issue.** Mostly covered by §8's assumption (Format = GIF swaps the panel). Implementation detail:
**Decision.** `ExportPanel` and `GifPanel` are separate `UserControl`s both hosted in `MainPage`,
shown/hidden by `Export.IsVideo` / `Export.IsGif`, so one region morphs rather than opening a new
window.
