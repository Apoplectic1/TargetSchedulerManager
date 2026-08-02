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

## Distribution: Velopack installers via GitHub Actions (target state)

Installers ship as **GitHub Releases built by CI, never from this machine** — pushing a `vX.Y.Z`
tag is the release trigger:

- `.github/workflows/release.yml` runs on tag push: check out TSM **and the Library repo
  side-by-side** (the `..\Library` `ProjectReference`s demand it), `dotnet publish` Release
  x64 self-contained, `vpk pack`, `vpk upload github --publish`. The Actions-provided
  `GITHUB_TOKEN` suffices for uploading Releases to this repo; a PAT secret is needed only for
  checking out the Library if it publishes private.
- **Versions come from the tag** via MinVer (TP's pattern — `<MinVerTagPrefix>v</MinVerTagPrefix>`);
  no version files to edit. Untagged commits shape as prereleases and never roll out.
- **The app self-updates from GitHub Releases** (Velopack `UpdateManager`, checked at startup +
  on demand) — model on TP's `Updates/UpdateService.cs`.
- Local dry-run stays possible (`vpk pack` without upload → `Releases\`, already gitignored),
  but the published artifact is always the CI one.

**Not yet wired (2026-08-02).** TSM has no Velopack integration yet (the `.gitignore` entry is
aspirational), no workflow file, and the hard prerequisite is unmet: **the Library repo is not on
GitHub in any form**, so no CI checkout can build TSM. Prerequisite chain: publish Library
(public or private) → workflow → Velopack app integration + MinVer. Until that lands, a `vX.Y.Z`
tag publishes **source only**, and the README says the clone doesn't build.

## README = storefront

`README.md` is the potential-user-facing description: what TSM does, screenshots, usage caveats,
license. Development/testing minutiae (test mechanics, fixtures, benchmarks, internal doc links)
stay out. The workshop dirs are publicly visible in the tree, so the README carries **one short
repo-layout paragraph** labeling them (`docs/` journal, `openspec/` specs + change records,
`.claude/` agent tooling) — one labeled line beats confusing silence. The reference docs
(`ARCHITECTURE.md`, `SUBSYSTEMS.md`, …) and specs are the product's deep description; don't
simplify them for browsing — the README is the human layer on top.

## Content rules (what is deliberately public)

- **Site coordinates + local paths ship in `DevDefaults.cs`** — a deliberate solo-consumer
  trade-off, same call TP made (see TP `DOMAIN.md` → personal presets; same site, already the
  author's stated convention). `BIRDWATCHER` is a LAN hostname; `E:\…` paths are this machine's.
  If TSM ever ships to others, the split-to-gitignored-partial path is the fix — not scrubbing
  history.
- **Never in the repo, so never published:** tokens/credentials (none exist), logs and
  observation screenshots (`%APPDATA%\TargetSchedulerManager\Logs\` — outside the tree).
- History publishes whole. Anything that must not be public must never be committed — there is
  no post-hoc scrub step.
