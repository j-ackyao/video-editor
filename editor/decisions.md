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

---

# Post-implementation changes (UX feedback)

## 19. Fixed: both export panels rendering at once (binding bug)
**Issue (user-reported).** After the audio section the UI showed a second, redundant set of
resolution/fps/target-size controls. Root cause: in `MainPage.xaml` each panel set
`DataContext="{Binding Export}"` *and* `Visibility="{Binding Export.IsVideo}"` on the same element;
once the DataContext was rebased, the visibility path resolved against the wrong scope
(`Export.Export.IsVideo`), failed silently, and defaulted to **Visible** — so both the video and GIF
panels were always shown.
**Decision.** Wrap each panel in a `Grid` that keeps the page's `MainViewModel` DataContext and
carries the `Visibility` binding; the inner panel alone rebases its DataContext. Now exactly one
panel shows per format, removing the duplicated controls and the confusing second "target size".

## 20. Custom resolution and fps entry
**Issue (user request).** Keep the preset dropdowns but also allow typing an arbitrary
resolution/fps.
**Decision.** _(Superseded by decision 22 — the "Custom…"/NumberBox approach below was replaced by a
single editable combo per field.)_ Originally added a "Custom…" sentinel entry that revealed a
`NumberBox`. Custom height is normalized to an even, positive value
(`ExportOptionCatalog.NormalizeHeight`) because yuv420p requires even dimensions — this rule carried
over into the editable-combo design.

## 21. High-refresh fps options (120, 144)
**Issue (user request).** Offer 120 and 144 fps.
**Decision.** Added 144 and 120 to the standard video-fps list. They follow the existing §12 rule —
only presets ≤ source fps are listed, so they appear for high-fps sources (e.g. 120/144 fps capture)
rather than fabricating frames by default. The editable combo field (decision 22) is the explicit
escape hatch for any other value, including deliberate up-sampling.

## 22. Editable combo fields replace number boxes + custom dropdowns
**Issue (user request).** Remove the up/down spinner on numeric inputs; merge the preset dropdown and
the free-typing box into one control (click → see presets, but can also type); make resolution/fps
boxes always numeric and pre-filled with the source value on load, keeping a "Same as source" entry
at the top that re-fills the box with the source value when chosen. Extend this to bitrate, target
size and audio bitrate with popular presets.
**Decision.** Replaced every `NumberBox` (and the separate preset/unit dropdowns and the earlier
"Custom…" option from decision 20) with an **editable `ComboBox`** (`IsEditable="True"`) per field —
this removes the spinner entirely and unifies presets + typing in one box. Introduced a reusable
`ComboFieldViewModel` (Options + free-text `Text` + optional "Same as source" substitution) and a
pure, unit-tested `FieldParsing` helper that turns the text into a concrete value, tolerating units
and stray characters (`"2500k"`, `"10 MB"`, `"59.94"`). Resolution/fps fields are seeded with the
source value on load and treat "value == source" as "same as source" (null → no scale/fps filter).
Presets added: video bitrate {8000, 5000, 2500, 1000, 500} kbps; target size {25, 10, 5, 2, 1 MB,
500 KB}; audio bitrate {320…64} kbps. The separate MB/KB unit dropdown was removed — the unit is now
typed inline (e.g. "10 MB", default bare number = MiB, per §9.1).

## 23. GIF fps default kept at 15 (not source)
**Issue.** The "show the source value on load" rule (decision 22) conflicts with GIF, where a 30/60
fps default would produce very large GIFs.
**Decision.** For the GIF panel the height box is still seeded with the source value, but the fps box
defaults to **15** (a GIF-appropriate value), while still offering "Same as source" in the dropdown
(which resolves to the source fps). Video export is unaffected and follows decision 22 fully.

## 25. FFmpeg must be a GPL build (eng-doc "LGPL + libx264" is inconsistent)
**Issue (user-reported).** A default MP4 export failed with `Unknown encoder 'libx264'` →
`Encoder not found`. The bundled binary was an **LGPL** FFmpeg build (matching §3.1's "bundle an LGPL
build" assumption), but the same eng-doc requires the `libx264` encoder (§3.1, §10.0). `libx264` is
**GPL-licensed and is not present in LGPL FFmpeg builds**, so those two requirements are mutually
exclusive — an internal inconsistency in the eng-doc.
**Decision.** Bundle a **GPL** FFmpeg build, which includes all encoders the app uses (`libx264`,
`libvpx-vp9`, `aac`, `libopus`, `gif`). This makes the §10 commands work unchanged. A static GPL
build is used so only `ffmpeg.exe` + `ffprobe.exe` are needed (no shared `av*` DLLs). Verified
end-to-end: a 2472×1620/30 fps clip trims and re-encodes via the exact default command
(`libx264 -crf 23 -preset medium … -movflags +faststart`) with exit code 0.
**Licensing consequence (flagging, per §3.1's own caveat).** Shipping a GPL `ffmpeg.exe` means the
distributed FFmpeg binary is under GPLv3; the app invokes it as a separate process (no static
linking), but anyone redistributing the app must comply with the GPL for that binary. The
alternative — staying LGPL by switching H.264 to `libopenh264` — was rejected because it changes the
quality model (`-crf`/`-preset` don't map to OpenH264) and lowers quality, diverging from the
documented §10 commands. If LGPL distribution is a hard requirement, revisit by replacing the H.264
codec mapping in `FormatCatalog`/§10.0 with `libopenh264` (or hardware encoders) behind the existing
`IEncodingService` seam. The binaries themselves remain uncommitted (decision 15 / `.gitignore`).



