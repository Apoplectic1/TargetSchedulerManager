# RELEASING.md — publishing TSM to GitHub

**Charter:** the rules for pushes to the public GitHub mirror. **The local repo is ground truth;
GitHub is the public face** — a distribution channel, never the canonical location. Nothing here
changes how development works; it only governs what the public sees and when.

## The mirror

`origin` = https://github.com/Apoplectic1/TargetSchedulerManager (public; created 2026-08-02).
No other remotes.

## Branch policy

- **`dev` = working branch.** All work lands here. **`dev` never pushes.**
- **`main` = distribution-ready ref, and every push of `main` carries a tag** — `vX.Y.Z`
  (semver, `v`-prefixed; the same form XFM uses). Publish = fast-forward `main` to the chosen
  `dev` commit, tag it, push both:
  ```bash
  git checkout main && git merge --ff-only dev
  git tag vX.Y.Z
  git push origin main vX.Y.Z
  git checkout dev
  ```
- Publish at natural completion points (a shipped unit of work, docs riding the same commit) —
  not on a schedule, and never mid-change. The working tree must be clean and tests green at the
  published commit. No tag → no push: the tag is what makes a `main` state a published state.

## Distribution: Velopack installers, built locally (shipped 2026-08-02)

Installers ship as GitHub Releases **packed and uploaded from this machine** — the sibling
Library repo stays unpublished, so only here do TSM's `ProjectReference`s resolve. No CI builds
(that alternative was designed and deliberately dropped 2026-08-02: local-build keeps AL off
GitHub entirely). Spec: `openspec/specs/self-update/`.

One-time setup: `dotnet tool install -g vpk`, and `$env:GITHUB_TOKEN` = a PAT with `public_repo`
scope (only needed for upload; `-NoUpload` dry-runs without it).

Per-release flow:
```powershell
# on main, at the published commit (see Branch policy)
git tag vX.Y.Z
git push origin main vX.Y.Z
.\scripts\release.ps1          # publish → vpk pack → upload to GitHub Releases
```
- **Versions come from the tag** via MinVer (`<MinVerTagPrefix>v</MinVerTagPrefix>`) — the same
  tag gates the `main` push, names the GitHub Release, and stamps the assembly. No version files.
  Untagged builds shape as `-alpha` prereleases the updater never offers.
- **The installed app self-updates**: startup-only check of this repo's Releases
  (`Services/UpdateService.cs` — silent on no-update/failure, ContentDialog prompt on a hit);
  dev/F5 runs never check. No manual check surface (user decision 2026-08-02).
- **Dry-run:** `.\scripts\release.ps1 -NoUpload` → artifacts in `Releases\` (gitignored);
  run the Setup.exe there to test an install locally. Verification recipe: `VERIFICATION.md`.

## README = storefront

`README.md` is the potential-user-facing description: what TSM does, screenshots, usage caveats,
license. Development/testing minutiae (test mechanics, fixtures, benchmarks, internal doc links)
stay out. The workshop dirs are publicly visible in the tree, so the README carries **one short
repo-layout paragraph** labeling them (`docs/` journal, `openspec/` specs + change records,
`.claude/` agent tooling) — one labeled line beats confusing silence. The reference docs
(`ARCHITECTURE.md`, `SUBSYSTEMS.md`, …) and specs are the product's deep description; don't
simplify them for browsing — the README is the human layer on top.

## Content rules (what is deliberately public)

- **The shared library ships compiled.** Release packages carry the `Astronomy.*` assemblies as
  DLLs — AL's *source* stays unpublished, but its binaries are publicly downloadable in every
  release. A deliberate call (2026-08-02): "AL stays private" means source, not bits.
- **Site coordinates + local paths ship in `DevDefaults.cs`** — a deliberate solo-consumer
  trade-off, same call TP made (see TP `DOMAIN.md` → personal presets; same site, already the
  author's stated convention). `BIRDWATCHER` is a LAN hostname; `E:\…` paths are this machine's.
  If TSM ever ships to others, the split-to-gitignored-partial path is the fix — not scrubbing
  history.
- **Never in the repo, so never published:** tokens/credentials (none exist), logs and
  observation screenshots (`%APPDATA%\TargetSchedulerManager\Logs\` — outside the tree).
- History publishes whole. Anything that must not be public must never be committed — there is
  no post-hoc scrub step.
