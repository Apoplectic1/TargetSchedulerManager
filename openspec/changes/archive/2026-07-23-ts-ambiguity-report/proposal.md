# ts-ambiguity-report — proposal

## Why

The 2026-07-08 resolver rejection (docs/2026-07-08-resolver-rejection-isp-lane.md, decision 3) settled the
workflow for TS/disk ambiguities: TSM **detects**, the user **fixes by hand in NINA's TS UI on BIRDWATCHER**.
But the detections are currently scattered and half-invisible — grid badges name the *what* but not the *fix*,
write-back's held cells appear only as a `tsm.log` WARN line, and several TS-internal problems (same-key
duplicate plans, planned-only twin targets, duplicate template names) have no surface at all. The user needs
one printable, walk-to-the-rig document: every ambiguity, what it is, why it's flagged, and the exact TS-UI
edit that clears it. This report **is** the "write-back app action" in its final form — a tripwire with
instructions, not a resolution dialog.

## What Changes

- New toolbar action **"Ambiguities…"** that generates a dated Markdown report and auto-opens it in the
  default `.md` handler (printable from any editor; the file persists for the walk to BIRDWATCHER).
- Report content = the union of all existing detections plus three new TS-internal checks:
  - **Existing (already computed each load):** `CatalogBuildReport` target issues (name-mismatch, ambiguous
    match, duplicate fold, alias fold, unanchored, invalid), `WriteBackPlan.Manual` held cells (with reason:
    identity / duplicate / multi-plan), `WriteBackPlan.NeedsReconciliation` notes incl. `UnplannedFrames`.
  - **New checks (grid can't badge these):** ≥2 exposure plans on one target with the same
    (filter, purpose, effective whole-second exposure) — the Swan H900 ×2 case; planned-only twin targets
    (same name, or a <tolerance coordinate pair with no disk anchor — invisible today because duplicate
    detection only fires on a disk claim); duplicate exposure-template names within a profile.
- Every item carries a **hand-edit instruction** phrased for NINA's TS UI (e.g. *"rename target `FishHead` →
  `IC 1795`"*, *"delete exposure plan Id 1040 (H900, desired 1) on `Swan`"*). The report never edits anything.
- Empty case: the report still generates, stating all checks ran clean — the tripwire's "0 held" proof.
- Report count surfaces in the status line after each load (e.g. `· 7 ambiguities`), so the tripwire is
  visible without opening the report.

## Capabilities

### New Capabilities
- `ts-ambiguity-report`: detection roll-up + printable report generation + toolbar/status surfacing — the
  read-only tripwire for TS authoring-convention violations and disk↔TS join problems.

### Modified Capabilities
<!-- none — no existing spec's requirements change; write-back, sync model, grid badges all stay as specified -->

## Impact

- **TSM only; read-only.** No TS writes, no library (AL) schema changes.
- App: new `Services/` report builder + the three TS-internal checks (pure functions over the already-loaded
  `CatalogGraph`/`CatalogBuildReport`/`WriteBackPlan`); `MainViewModel` exposes the command + count;
  `MainWindow` toolbar button; report file written under `%APPDATA%\TargetSchedulerManager\Reports\` and
  launched via the default handler.
- Uses `WriteBackPlanner.Plan(...)` output the load already produces (or re-plans cheaply in memory — decided
  in design); no disk rescan.
- Docs: DOMAIN.md chrome/checklist touch (new toolbar element), ROADMAP recently-shipped, in the same commit.
