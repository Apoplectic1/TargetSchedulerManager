# VERIFICATION.md — TargetSchedulerManager

**Charter:** how to build, run, test, and verify a change here. Read before calling a change done.
TSM is a WinUI app — the build proves *code-correct*; **visual/UX correctness is feature-verified
by running + screenshotting the app** (the author's call), not by the build.

## Build & run
```bash
# Build (slnx pulls in Astronomy.Catalog + Astronomy.Diagnostics + Astronomy.Core from ..\Library,
# plus Astronomy.XISF transitively via .Catalog)
dotnet build TargetSchedulerManager.slnx -v:m -nologo

# Run the WinUI app: TS plan vs disk grid (fresh in-memory scan on load, no Catalog.db needed)
TargetSchedulerManager.App/bin/x64/Debug/net10.0-windows10.0.19041.0/win-x64/tsmui.exe

# Tests (App.Tests only)
dotnet test TargetSchedulerManager.slnx -v:q --nologo
```
TSM's graph is **pure-managed** (Microsoft.Data.Sqlite only), AnyCPU/x64, no native deps — so it
builds with plain `dotnet build` (the `.vcxproj` MSBuild caveat does **not** apply here; the native
PCL projects aren't in TSM's solution). Path defaults live in
`TargetSchedulerManager.App\Shared\DevDefaults.cs`. The app loads and edits the **local TS working
copy** (`Processing\Catalog\TS Database\schedulerdb.sqlite`) only; `TsSync` pulls it fresh from
BIRDWATCHER at open (skipped when the persisted baseline says it's unchanged — so rapid test
relaunches skip the copy) and pushes journaled edits back through the reviewed **Push** button.
Reload rescans and never pulls; full toolbar map: `DOMAIN.md` → *Chrome*.

**The instrument for a visual pass is `Ctrl+N`** — the Diagnostics window: type a note, capture the main
window, and both land in `tsm.log` bracketed by `USER_OBS_START`/`USER_OBS_END`, so an observation carries its
own context. The window itself is a singleton and always available; `TSM_DIAG` gates the DIAG *context* lines
that the bracket scopes (`TestEnv` blanks it so VM tests can't write the session log). Capture mechanics and
the transient-UI trick: `DOMAIN.md` → Chrome.

### Sync flows worth exercising after a sync-layer change
1. **Fresh pull:** delete `schedulerdb.sqlite.tsm-sync.json` beside the local db → open → status says
   "pulled fresh"; relaunch → "unchanged — pull skipped".
2. **Edit → badge → push:** change a `desired` → badge shows "1 unpushed", Push enables → Push… →
   review dialog → confirm → verify the value in NINA's TS editor on BIRDWATCHER.
3. **Write-back:** with drifted TS counts, open → status notes "write-back stamped N plan(s)", the
   push review lists them (decreases first, caution-colored).
4. **Dirty-open prompt:** edit, kill TSM, relaunch with BIRDWATCHER reachable → push/discard/not-now
   dialog appears BEFORE any pull.
5. **Offline session:** open with BIRDWATCHER off → badge says offline; edits journal; next reachable
   open offers the push.

### Other surfaces worth exercising
- **Visible Tonight:** set Duration + Floor → Tonight → target flips apply, then the project flips derived
  only from targets that *landed*; the whole pass is one busy scope.
- **Cancel (phase-scoped):** the button shows while any cancellable phase runs. Hit it during the **pull** →
  the copy stops, the previous local copy survives, and the load *continues* onto the scan with a note saying
  what didn't happen. Hit it during the **scan/resolve** → the load ends, the status reads `load cancelled —
  showing the previous scan`, and the grid still shows the prior rows (empty on a first-ever load). Never
  `load failed`, never a blanked grid. *Not unit-tested* — cancelling mid-scan needs a race the suite has no
  seam for, so this recipe is the check.
- **Busy refusal:** start a pull, then try a row edit → refused with a status note, edit surfaces disabled off
  `CanEdit` (search / filters / Ambiguities stay live). The reverse: with an edit in flight, a bulk op refuses
  immediately rather than waiting.
- **Templates…:** the picker's blast-radius title + per-template change mark; a `filtername` edit warns.
- **Ambiguities…:** writes a dated report to `%APPDATA%\TargetSchedulerManager\Reports\` and opens it; the
  status line's `· N ambiguities` matches.
- **Editor field marks:** open the row editor dialog (right-click → Edit…, or the row's edit glyph) on a row
  with inbound + local changes → per-field `←`/`→`/`⇄`, blank
  slots still aligned. Revert a field to its baseline → its mark and its unpushed count both clear.
- **Adopt a disk-only cell:** right-click an eligible disk-only row → "Add TS plan…" (or "Add to TS…" when
  the whole target is disk-only) → assignment dialog (project locked whenever the TS target exists;
  template dropdown scoped to same filter + bin, non-pairing selection cautions but never blocks) → Accept →
  the grid re-reconciles in place (a pairing assignment merges to `Both`); the new rows appear in the next
  Push review's **Creates** section.

## Tests

> **Trap — always test via the slnx, never csproj-direct.** The slnx pins Platform=x64, so solution
> builds write `bin\x64\Debug\…`; a csproj-direct `dotnet build`/`dotnet test` defaults to AnyCPU and
> uses `bin\Debug\…` — a *separate, possibly stale* output. A csproj-direct `dotnet test --no-build`
> after a slnx build silently runs the stale tree (green results, old binaries — bit us 2026-07-23:
> a new test file "passed" without ever being compiled). `dotnet test TargetSchedulerManager.slnx`
> both builds and runs the same tree.

One test project: **`TargetSchedulerManager.App.Tests`** (`dotnet test TargetSchedulerManager.slnx`)
— the app's real logic: row building, the VM pipeline, and the sync model (the test-file names are the
index; an enumeration here would just drift). The
sync tests use per-test temp dirs with **real SQLite files** for the online-backup pull ("remote" =
a local temp path; unreachable = a path that doesn't exist) and stub `ITsEditor` /
`ITsWriteBackApplier` seams for push replay (`SyncStubs.cs`). Runs in a **plain test host (no XAML
runtime)**: never touch the `Brush` getters (`SecondsBackground`/`HoursBackground` need
`Application.Current`) — those stay app-verified. `TestEnv` blanks `TSM_DIAG` so VM tests can't
write the user's session log.

The suite contains a few **timing-gated tests** (`CommitChainTests`' 50 ms no-overlap window, the busy-gate
blocking-editor waits) that can flake under build-time disk load. If a run reports a failure you can't
reproduce, re-run before investigating — and capture the test name (`dotnet test` *without* `-v:q`) so a
recurrence can be pinned rather than re-guessed.

Heavy logic (schema / scan / resolve / write-back) is covered in the **library repo**:
```bash
# from ..\Library
dotnet test Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj                         # full suite
dotnet test Astronomy.Catalog.Tests/Astronomy.Catalog.Tests.csproj --filter "FullyQualifiedName~TargetResolver"
```

## Warnings are build breaks
Both projects build with `<TreatWarningsAsErrors>` (2026-08-01, portfolio-wide ratchet after 45 xUnit
analyzer warnings accumulated silently in AL's test bench). Fix the warning, or — rarely, with a comment —
suppress it deliberately; never turn the ratchet off. It also applies transitively: AL's projects carry it
too, so an AL-side warning fails a TSM build — read which project the error message names before hunting in
TSM code. In test code, pass `TestContext.Current.CancellationToken` to ct-accepting calls (xUnit1051).

## Trap — xUnit v3 (build-breaking)
`App.Tests` is xUnit v3 (`OutputType=Exe`; v3 generates the entry point). **Never let `xunit.v3`
land on `TargetSchedulerManager.App`** (or any non-test project) — a "Manage NuGet for Solution →
all projects" action sprays it silently, and its `mtp-v1` targets then fail the build with "test
projects must be executable" (hit the whole `.slnx` on 2026-06-21). A non-test project that needs
xUnit types references `xunit.v3.extensibility.core`. (Also noted in `..\Library\CLAUDE.md`.)

## Release (Velopack) verification
Distribution rules + per-release flow live in `RELEASING.md`; this is the *verification* recipe:
```powershell
.\scripts\release.ps1 -NoUpload     # publish → vpk pack, no GitHub touch
# pre-flight fires on -NoUpload too: HEAD needs a reachable vX.Y.Z tag, and ..\Library must be
# clean + tagged (no -alpha MinVer stamp) or the AL gate aborts — RELEASING.md → AL coordination.
.\Releases\TargetSchedulerManager-win-Setup.exe
```
Confirm: installs per-user with Start Menu shortcut, launches, and `tsm.log` shows the startup
update check ran (an F5/dev run logs nothing — the check is installed-only). The in-app update
*prompt* can only be proven across two releases: install vN, publish vN+1, relaunch vN → prompt →
accept → app restarts as vN+1. The installed copy and the F5 workflow coexist — installing does
not affect dev builds.

## Feature-verified vs code-correct
Build + tests = **code-correct**. A change touching the grid / look-and-feel isn't **done** until
**visually confirmed** — the author runs + screenshots the app (don't do this unprompted). UI rules
the change must respect: `DOMAIN.md`.
