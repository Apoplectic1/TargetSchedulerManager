# velopack-self-update — design

## Context

See `proposal.md` → Why. TP is the working model (WinForms: `Updates/UpdateService.cs`,
`scripts/release.ps1`, MinVer + Velopack in the csproj); TSM is unpackaged WinUI 3
(`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, win-x64, assembly `tsmui`), which
changes exactly two things: the entry point (WinUI generates `Main`; Velopack must run first) and
the prompt surface (no `MessageBox`/`IWin32Window` — `ContentDialog` needs a live `XamlRoot`).
TSM's repo is public, so the update source needs no token (simpler than TP's private-repo setup).

## Goals / Non-Goals

- **Goals:** installed-copy self-update from GitHub Releases; a one-command local release script;
  zero impact on the load/edit/sync machinery.
- **Non-Goals:** CI builds (Library unpublished — RELEASING.md), code signing (SmartScreen warning
  accepted, TP precedent), a manual check surface (startup-only; revisit later), auto-download
  without prompt, Library/NuGet packaging changes.

## Decisions

- **D1 — Custom `Main` via `DISABLE_XAML_GENERATED_MAIN` + `Program.cs`.**
  `VelopackApp.Build().Run()` must be the first statement of the process — Setup.exe/Update.exe
  relaunch the app with hook args (`--veloapp-install` etc.) that must be serviced and exited
  before any UI exists. WinUI's generated `Main` leaves nowhere to put it, so we own the entry
  point: define the constant, add `Program.Main` that runs Velopack, then the standard unpackaged
  bootstrap (`ComWrappersSupport.InitializeComWrappers()`, `Application.Start` installing a
  `DispatcherQueueSynchronizationContext`, `new App()`). Alternative rejected: calling Velopack
  from `App()`/`OnLaunched` — too late; hook invocations would boot the whole XAML stack.

- **D2 — Check launches from `OnLaunched`, after window activation, via `FireAndLog`.**
  The prompt is a `ContentDialog` and needs the main window's `XamlRoot`, which exists only after
  activation. The check is fire-and-forget under the house `FireAndLog` rule and never awaited by
  the load pipeline — spec: it must not delay load. It races the initial load harmlessly: the
  dialog is UI-thread work and `ApplyUpdatesAndRestart` only runs on explicit accept (a mid-load
  restart is acceptable — nothing local is dirty before the user edits, and the journal survives
  restarts regardless).

- **D3 — Pack the `dotnet publish` output, not the build output.** TP packs its build folder;
  unpackaged self-contained WinUI needs the publish layout (WinAppSDK runtime + .NET runtime
  included) for a machine with no SDKs. `dotnet publish -c Release -r win-x64` on the App project;
  `vpk pack -u TargetSchedulerManager -e tsmui.exe` over that folder. Pack id
  `TargetSchedulerManager` (install dir identity), title "Target Scheduler Manager"; no `--icon`
  (none exists in the repo — add later if wanted).

- **D4 — `GithubSource(repoUrl, accessToken: null, prerelease: false)`.** Public repo — anonymous
  API works; `prerelease: false` + MinVer's `-alpha` shaping on untagged commits is the guard that
  keeps dev builds from ever being offered. Same double lock TP uses.

- **D5 — UpdateService is a thin untested facade in `Services/`** (the VM/services seam —
  CONVENTIONS' one-plausible-home: it's app-shell behavior, not sync/edit logic, so not
  `Shared/`). Startup path swallows everything with `Log.Warn`; the `IsInstalled` guard is the
  dev no-op. No unit tests — it's all Velopack + network; the release dry-run and the v1.1.1
  update hop are the real verification.

- **D6 — MinVer on the App project only.** Tests don't need stamped versions.
  `MinVerTagPrefix=v`, `MinVerDefaultPreReleaseIdentifiers=alpha.0` (TP's values). Note
  TreatWarningsAsErrors is on: MinVer's no-tag-found warning case can't occur (v1.0.0 exists),
  and its package is analyzer-clean in TP under the same ratchet.

## Risks / Trade-offs

- [Velopack hook args meet a WinUI app that logs at startup] → `VelopackApp.Run()` exits the
  process on hook invocations before `Log.Init` runs — keep `Main` Velopack-first, log-second.
- [A mid-load restart on accept] → acceptable by design (D2); the local db and journal are
  crash-safe by existing contract.
- [Publish output drifts from what F5 runs] → release script always publishes fresh; VERIFICATION
  gains the dry-run recipe so the flow is reproducible.
- [MinVer makes every local build compute a version from git] → cosmetic only; no build-time
  network, negligible cost.

## Migration Plan

None — no persisted state changes. First release: tag `v1.1.0`, run `scripts/release.ps1`,
install Setup.exe over nothing (fresh install; the F5 workflow continues unchanged beside it).
Rollback = delete the GitHub Release; installed copy keeps running what it has.

## Open Questions

- App icon for the installer/shortcut (cosmetic; `--icon` can join any later release).
