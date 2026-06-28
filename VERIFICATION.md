# VERIFYING.md — TargetSchedulerManager

**Charter:** how to build, run, test, and verify a change here. Read before calling a change done.
TSM is a WinUI app — the build proves *code-correct*; **visual/UX correctness is feature-verified
by running + screenshotting the app** (the author's call), not by the build.

## Build & run
```bash
# Build (slnx pulls in Astronomy.Catalog + Astronomy.XISF from ..\Library)
dotnet build TargetSchedulerManager.slnx -v:m -nologo

# Run the WinUI app: TS plan vs disk grid (fresh in-memory scan on load, no Catalog.db needed); edit TS live
TargetSchedulerManager.App/bin/Debug/net10.0-windows10.0.19041.0/win-x64/tsmui.exe

# Tests (App.Tests only)
dotnet test TargetSchedulerManager.slnx -v:q --nologo
```
TSM's graph is **pure-managed** (Microsoft.Data.Sqlite only), AnyCPU/x64, no native deps — so it
builds with plain `dotnet build` (the `.vcxproj` MSBuild caveat does **not** apply here; the native
PCL projects aren't in TSM's solution). Path defaults live in
`TargetSchedulerManager.App\Shared\DevDefaults.cs`. TS db via `TsDatabaseResolver` (prefers the LIVE
BIRDWATCHER db over SMB when reachable, else the local working copy under
`Processing\Catalog\TS Database\schedulerdb.sqlite`); live writes are guarded (refuse open-sidecar /
read-only) + read-back verified, with a LIVE/LOCAL toolbar badge.

## Tests
One test project: **`TargetSchedulerManager.App.Tests`** (`dotnet test TargetSchedulerManager.slnx`)
— the app's real logic: `ReconciliationLoader.BuildRows`, the `MainViewModel` filter/toggle
pipeline, `VisibleRowTree` (the flatten==splice invariant), `TsDatabaseResolver`, and the guarded-
TS-write seam (`TsSource` / `TsEditGate`). Runs in a **plain test host (no XAML runtime)**: never
touch the `Brush` getters (`SecondsBackground`/`HoursBackground` need `Application.Current`) — those
stay app-verified. `TestEnv` blanks `TSM_DIAG` so VM tests can't write the user's session log.

Heavy logic (schema / scan / resolve / write-back) is covered in the **library repo**:
```bash
# from ..\Library
dotnet test Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj                         # full suite
dotnet test Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj --filter "FullyQualifiedName~TargetResolver"
```

## Trap — xUnit v3 (build-breaking)
`App.Tests` is xUnit v3 (`OutputType=Exe`; v3 generates the entry point). **Never let `xunit.v3`
land on `TargetSchedulerManager.App`** (or any non-test project) — a "Manage NuGet for Solution →
all projects" action sprays it silently, and its `mtp-v1` targets then fail the build with "test
projects must be executable" (hit the whole `.slnx` on 2026-06-21). A non-test project that needs
xUnit types references `xunit.v3.extensibility.core`. (Also noted in `..\Library\CLAUDE.md`.)

## Feature-verified vs code-correct
Build + tests = **code-correct**. A change touching the grid / look-and-feel isn't **done** until
**visually confirmed** — the author runs + screenshots the app (don't do this unprompted). UI rules
the change must respect: `DOMAIN.md`.
