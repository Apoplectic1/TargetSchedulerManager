# Velopack self-update

## Why

TSM now has a public face (github.com/Apoplectic1/TargetSchedulerManager, first published v1.0.0
2026-08-02) but no installable distribution and no update path — the only way to run it is a local
build on the dev machine. The user wants TP's proven Velopack model: installers on GitHub Releases,
the installed app updating itself. Releases must build locally — the sibling Library repo the app
references stays unpublished, so CI cannot build TSM (decided 2026-08-02, RELEASING.md).

## What Changes

- TSM ships as a Velopack installer (Setup.exe + delta packages) on the public repo's GitHub
  Releases, packed and uploaded from the dev machine by a new `scripts/release.ps1`
  (publish → `vpk pack` → `vpk upload github`).
- The installed app checks GitHub Releases **at startup only** (user-decided surface, 2026-08-02):
  silent on no-update and on failure (log trail only), a ContentDialog prompt on a hit, install +
  restart on accept. Dev (F5) builds never check and never roll out.
- The app takes a custom `Main` (`DISABLE_XAML_GENERATED_MAIN` + `Program.cs`) so
  `VelopackApp.Build().Run()` runs before any WinUI bootstrap — the hook Setup.exe/Update.exe drive.
- Assembly versions derive from git tags via MinVer (`v` prefix — the same `vX.Y.Z` tags that gate
  `main` pushes per RELEASING.md); untagged builds shape as prereleases invisible to the updater.
- Docs: RELEASING.md's distribution section flips from the Actions target-state to the local-build
  flow; README gains an Install section; first installer tag is **v1.1.0** (user-decided).

## Capabilities

### New Capabilities

- `self-update`: how TSM is distributed and how the installed app updates itself — the release
  artifact contract (Velopack packages on GitHub Releases, tag-derived versions), the startup
  check's behavior (silent failure, prompt on hit, dev-build no-op), and the local-build rule
  (releases pack the dev machine's publish output; the Library never publishes).

### Modified Capabilities

(none — no existing spec's requirements change; the update check runs before/outside the
load/edit/sync machinery and touches none of its gates)

## Impact

- **App project:** `TargetSchedulerManager.App.csproj` (Velopack + MinVer packages,
  `DISABLE_XAML_GENERATED_MAIN`), new `Program.cs`, new `Services/UpdateService.cs`, one
  `FireAndLog` call after window activation.
- **New:** `scripts/release.ps1` (modeled on TP's).
- **Docs:** RELEASING.md, README.md, CHANGELOG.md, CLAUDE.md/ARCHITECTURE.md only if the
  invariants mirror needs a line (expected: no).
- **No behavior change** to load/edit/sync/visible-tonight; no schema or Library change.
