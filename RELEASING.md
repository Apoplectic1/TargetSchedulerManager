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
  git tag -a vX.Y.Z -m "one-line release summary"
  git push origin main vX.Y.Z
  git checkout dev
  ```
  Tags are **annotated** (`-a -m`, portfolio convention 2026-08-06) so the summary shows in
  `git tag -n` and GUI clients; earlier tags are lightweight — cosmetic only, MinVer treats
  both alike.
- Publish at natural completion points (a shipped unit of work, docs riding the same commit) —
  not on a schedule, and never mid-change. The working tree must be clean and tests green at the
  published commit. No tag → no push: the tag is what makes a `main` state a published state.
- **AL coordination (pre-flight):** the installer embeds the sibling `..\Library` working tree
  at pack time, unpinned. If AL is dirty or has moved past its last published tag, **publish AL
  first** (see Library `RELEASING.md`) so the payload's `Astronomy.*` DLLs stamp a clean
  `X.Y.Z` that exists on AL's public mirror. `release.ps1` enforces this — it aborts on a
  dirty Library tree or an `-alpha` MinVer stamp in the payload.
- **Docs-only exception (2026-08-02):** a `main` push may omit the tag when the delta contains
  only documentation/images — nothing that changes the built app — so the GitHub storefront
  (README, screenshots) can update without minting a release. Any change to code or build
  inputs keeps the full no-tag-no-push rule.

## Distribution: Velopack installers, built locally (shipped 2026-08-02)

Installers ship as GitHub Releases **packed and uploaded from this machine, by decision** —
pinned as a requirement in spec `openspec/specs/self-update/` ("Releases build locally, never in
CI"; the CI alternative was designed and deliberately dropped 2026-08-02). AL has since been
mirrored publicly (github.com/Apoplectic1/Astronomy-Library, later the same day), so the
original hard constraint — no checkout could resolve TSM's `..\Library` `ProjectReference`s —
is gone, but the decision stands on its own merits: local-build remains the contract until the
spec is deliberately changed.

One-time setup: `dotnet tool install -g vpk`, and `$env:GITHUB_TOKEN` = a PAT with `public_repo`
scope (only needed for upload; `-NoUpload` dry-runs without it).

Per-release flow:
```powershell
# on main, at the published commit (see Branch policy)
git tag -a vX.Y.Z -m "one-line release summary"
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
- **Bare `vpk` commands: run from the repo root.** vpk reads `.\Releases\` of the *current
  directory* with no repo/package cross-check — a wrong cwd uploads another app's payload
  (2026-08-06: a drifted shell published TP 1.3.3 assets as XFM v2.2.1; caught and deleted in
  a minute). `release.ps1` is immune — it pins the repo root.
- **A stale build directory is structurally unshippable** (2026-08-10, after v1.5.3 shipped a payload
  built from the wrong path). `release.ps1` derives the publish path from the csproj's own
  `TargetFramework` rather than repeating it as a literal — so a TFM bump can't leave the script
  packing yesterday's folder — and then **gates the packed exe's MinVer stamp against the release
  tag**, aborting when they disagree. Between them the two checks make "packed something that isn't
  this tag" a failed release instead of a published one. Don't hand-edit a path around either gate;
  if one fires, the build tree is what's wrong.

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
  DLLs; since 2026-08-02 AL's source is also publicly mirrored at
  github.com/Apoplectic1/Astronomy-Library (its own RELEASING.md governs that mirror — tagged
  source snapshots only, no binary releases; installers like TSM's remain AL's binary channel).
- **Site coordinates + local paths ship in `DevDefaults.cs`** — a deliberate solo-consumer
  trade-off, same call TP made (see TP `DOMAIN.md` → personal presets; same site, already the
  author's stated convention). `BIRDWATCHER` is a LAN hostname; `E:\…` paths are this machine's.
  If TSM ever ships to others, the split-to-gitignored-partial path is the fix — not scrubbing
  history.
- **Never in the repo, so never published:** tokens/credentials (none exist), logs and
  observation screenshots (`%APPDATA%\TargetSchedulerManager\Logs\` — outside the tree).
- History publishes whole. Anything that must not be public must never be committed — there is
  no post-hoc scrub step.
