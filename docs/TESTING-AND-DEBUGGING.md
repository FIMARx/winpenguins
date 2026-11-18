# Testing & Bug-Hunting System — WinPenguins

Inspired by DigiKaveri's multi-layer bug-exposing pipeline, WinPenguins pairs strict compile-time analyzers with property-based fuzzing, in-process UI layout auditing, GDI/USER handle-leak stress checks, and runtime diagnostics telemetry.

---

## 1. Verification Layers at a Glance

| Layer | Where | What it catches | Speed |
|---|---|---|---|
| **1. Whitespace & Analyzers** | `Directory.Build.props`, `.editorconfig`, `IDisposableAnalyzers`, `Meziantou.Analyzer` | Missing `Dispose()`, culture-unsafe parsing/formatting, ignored Win32 return codes, lock/async mistakes (`TreatWarningsAsErrors=true`) | ~5 s |
| **2. Roslyn AST & Contract Guards** | `tests/WinPenguins.Tests/SourceAndContractGuards.cs` | `Thread.Sleep` in `UI/`, discarded `Process.Start` handles, `Form` classes missing Fluent chrome, missing embedded `.bmp`/`.wav` assets, un-rendered `ToyKind` / `PenguinSubType` variants | < 1 s |
| **3. Core Logic Unit Tests** | `tests/WinPenguins.Tests/PenguinTests.cs` | Physics seams across mixed-DPI & staggered monitors, 240Hz sprint parity, toy drag/portals, crew roles, GIF/Aseprite import, WASAPI/focus monitoring, DXGI HResult checks | ~4 s |
| **4. Seeded Property Fuzzer (`CsCheck`)** | `tests/WinPenguins.Tests/FuzzAndStressTests.cs` | NaN/Infinity physics coordinates, toys/portals stranding outside monitors after disconnects, 240Hz–250ms stall spikes (`--slow=4` equivalent), parser crashes on arbitrary inputs | < 2 s |
| **5. UI Layout Auditor & Tab Crawler** | `tests/WinPenguins.Tests/UiAuditTests.cs` | Clipped labels/buttons, overlapping siblings, controls escaping parents (`OUTSIDE`), horizontal overflow (`HORIZ_SCROLL`), WCAG contrast failures across Light & Dark themes at `800x540` & `920x700`, live `Apply`/`Cancel` rollback | ~15–25 s |
| **6. GDI / USER Handle-Leak Watchdog** | `tests/WinPenguins.Tests/HandleAndLeakTests.cs` | Native Win32 GDI font/bitmap leaks across repeated `OptionsForm` / `AboutForm` opens and sustained Direct2D `OverlayHost` renders + theme reloads (`GetGuiResources`) | ~20 s |
| **7. Direct2D Pixel & Menu Contracts** | `tests/WinPenguins.Tests/VisualAndMenuTests.cs` | Offscreen GPU Direct2D pixel-buffer rendering checks (`CaptureTestSceneBgra` for climbing crew hats, Baby 75% height, toys, emotes) & right-click `ContextMenuStrip` role coverage | ~3 s |
| **8. Runtime Diagnostics & Coverage Floor** | `src/WinPenguins/Diagnostics/DiagnosticsReporter.cs`, `scripts/verify.ps1` | Deduplicated unhandled exceptions, frame stalls (`> 350 ms`), GPU recoveries (`diagnostics.jsonl`), **Copy Diagnostics** button in About, Cobertura line-coverage gate (`>= 75%` on `Engine`+`Config`+`Diagnostics`) | Always on / `-Full` |

---

## 2. Running Verification

```powershell
# Fast pre-commit verification (<15s): whitespace + strict build + units + AST guards + CsCheck fuzzers + About audit
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Quick

# Smart verification (inspects git status: adds full UI matrix + handle-leak stress when UI/Platform/Config changed)
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1

# Full verification: every suite + Coverlet coverage threshold + standalone SingleFile publish check
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1 -Full
```

Pre-commit hook setup (runs `-Quick` automatically on `git commit` and blocks committing `publish/` or `TestResults/`):

```powershell
git config core.hooksPath .githooks
```

---

## 3. Real Bugs Already Exposed by This System

1. **GDI `Font` & `Bitmap` leaks in dialogs (`IDisposableAnalyzers`)** — `AboutForm` and `OptionsForm` previously passed inline `new Font(...)` and `appIcon.ToBitmap()` handles directly to controls without disposing them on form close. Tracked & disposed cleanly.
2. **Repeated `FrameStall` logs consuming session cap (`DiagnosticsReporter` tests)** — millisecond differences in stall strings bypassed dedup keys; unified under `"FrameStall"`.
3. **Narrow-window (`800x540`) clipping in `OptionsForm` (`UiLayoutAuditor`)** — crew role chips (`Sleepy`), Quick Toybox buttons (`Scatter All`), sidebar margins, and `AddControlRow` subtitles clipped when shrunk to `MinimumSize`; replaced rigid single-row grids with wrapping `FlowLayoutPanel` strips and responsive `SizeChanged` label wrapping.
4. **`OptionsForm` 4x redundant sprite load + 13.4s layout cascade** — handle-leak & latency checks exposed constructor taking ~13.4s (`O(rows^2 * 2^depth)` unsuspended nested `TableLayoutPanel` layouts + loading all 9 sheets repeatedly); fixed via `SuspendLayout`/`ResumeLayout` across page/card/row containers and `SpriteCatalog.LoadWalkerPreviewSheet` (~876ms warm).
5. **Crawler MTA thread deadlock (`AllowDrop`)** — exposed OLE `RegisterDragDrop` blocking on xUnit MTA threads; added explicit STA `StaRunner` with timeout guard.
6. **Climbing crew accessories upside-down/off-head** — fixed via `DCompOverlayWindow.TryCreateClimbAccessoryTransform` rotating hats, crowns, headphones, nightcaps, scarves, and jetpacks to match left/right wall climber orientation.
