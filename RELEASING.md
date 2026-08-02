# RELEASING.md — publishing TSM to GitHub

**Charter:** the rules for pushes to the public GitHub mirror. **The local repo is ground truth;
GitHub is the public face** — a distribution channel, never the canonical location. Nothing here
changes how development works; it only governs what the public sees and when.

## The mirror

`origin` = https://github.com/Apoplectic1/TargetSchedulerManager (public; created 2026-08-02).
No other remotes.

## Branch policy

- **`dev` = working branch.** All work lands here. **`dev` never pushes.**
- **`main` = distribution-ready ref.** Publish = fast-forward `main` to the chosen `dev` commit,
  then `git push origin main`:
  ```bash
  git checkout main && git merge --ff-only dev && git push origin main && git checkout dev
  ```
- Publish at natural completion points (a shipped unit of work, docs riding the same commit) —
  not on a schedule, and never mid-change. The working tree must be clean and tests green at the
  published commit.

## What "publishing" is (and is not)

- **Source distribution only.** TSM is the author's own management app, run from a local build.
  No installers, no GitHub Releases, no version tags. (If that ever changes, model the release
  flow on TargetPlanner's `RELEASING.md` — Velopack + MinVer.)
- **Not buildable from a public clone today.** TSM has three `ProjectReference`s into the sibling
  `..\Library` repo (`Astronomy.Catalog`, `.Diagnostics`, `.Core`), which has **no public mirror**.
  Until the Library publishes, the GitHub repo is for reading, not cloning-and-building — the
  README must say so plainly.

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
