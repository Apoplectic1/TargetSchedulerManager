# Proposal: adopt-disk-rows

## Why

Disk-only rows (frames on disk with no corresponding TS plan — today's `UnplannedFrames` notes) are
invisible to TS: its planner doesn't know the data exists, and the grid can never read `Both` for them.
The write-back contract deliberately parked this ("write-back updates existing plan rows only — plan
creation is an M2 concern"); this change is that concern arriving, as a **user-initiated, per-row
adoption** — never a sweep.

## What Changes

- **Right-click "Add to TS" on a disk-only row** — new items in the existing `Row_RightTapped` context
  menu (the documented extension point; disk-only rows currently get no menu):
  - Disk-only filter cell under a target TS already knows → **"Add TS plan…"**: INSERT one
    `exposureplan` for exactly that cell.
  - Row under a fully disk-only target → **"Add to TS…"**: dialog with a **project picker** +
    name/coords confirm, then INSERT a `target` (disk RA/Dec centroid, RA converted degrees → hours)
    plus the one plan. One click = one cell; repeat per cell.
- **Template auto-match, hold if unclear**: the plan's template is chosen by the merge rule — filter +
  `"Stars "` purpose + gain/offset/bin expressed and equal (a `-1` camera-default sentinel never pairs,
  matching the capture-config rule's honest reading). ≥2 candidates → refuse with reason; **zero** → the
  hold dialog offers creating the missing template from the cell's numbers (policy fields cloned from a
  same-profile donor) — user-confirmed, never silent (added 2026-08-03 after field feedback: historical
  cells shot under configs no current template expresses are the normal case).
- **Born complete (record history)**: `desired` = `acquired` = `accepted` = disk file count for that
  exposure bucket; TS sees the history and schedules nothing until the user raises `desired`.
- **Split rows are excluded**: a disk row separated from an existing same-`(filter, purpose, seconds)`
  plan by gain/offset/binning/framing disagreement gets **no menu item** — creating one would mint a
  same-key duplicate the authoring conventions forbid; the separation stays the diagnostic.
- **A third replay leg — insert**: a new journal kind carrying the full row payload + minted guid;
  push replays it as a remote INSERT (remote autoincrement mints its own `Id`), resolving
  `targetid`/`exposureTemplateId` by **parent guid**, target-inserts before their plan-inserts. The
  push review dialog grows a "creates" section.
- **Sync marks fall out of the journal**: an insert entry marks its row `→` like any edit; the
  post-push Id-space divergence (local Id ≠ remote Id for inserted rows) is masked guid-wise so the
  closing pull's differ never stamps a spurious `←` "new row" on a row the user created.
- **Cadence-safe**: a plan insert is a cadence-affecting, target-scope operation — the target's
  `filtercadenceitem` rows are cleared transactionally with the insert; a target with
  `overrideexposureorderitem` rows refuses the insert (existing OEO posture).

## Capabilities

### New Capabilities

- `disk-row-adoption`: the user-initiated adoption of a disk-only row into TS — menu gating (which
  rows offer it, split-row exclusion), template auto-match with hold, the project-picker dialog for
  new targets, born-complete values, and the local insert's immediate effects (re-reconcile to `Both`,
  journal, marks).

### Modified Capabilities

- `ts-sync-model`: the journal gains an **insert** kind and push-as-replay gains an insert leg
  (guid-carried, parent-guid FK resolution, ordered target-before-plan); the push review shows
  creates; the closing pull's Id renumbering of inserted rows is defined behavior.
- `edit-direction-marks`: inserted rows mark `→` from their journal entries; a pushed insert is
  guid-masked out of the closing pull's inbound diff (no spurious `←` "new row").
- `cadence-safe-ts-editing`: plan **insertion** joins the cadence-affecting operation set (target-scope
  clear rides the insert transaction); the OEO refusal covers inserts.
- `target-and-plan-flyouts`: disk-only rows are no longer menu-free — they offer the adoption action
  (still no edit items; TS-backed rows unchanged).

## Impact

- **Library** (`Astronomy.Catalog`, separate repo): a guarded **insert primitive** beside
  `TrySetField` — same guard order, transactional cadence clear for plan inserts, OEO refusal,
  read-back verify. Reconciliation itself is untouched.
- App: `Shared/TsJournal` (new kind + payload), `Shared/TsSync` (insert replay leg, fold-in of edits
  on unpushed inserts, review creates section, inbound guid-mask), `Services/SyncMarks` (insert
  entries), `MainWindow.Flyouts.cs` (menu items + adoption/project-picker dialog), a new adoption
  planner/service (eligibility, template match, value assembly), `TsEditGate`/VM funnel wiring,
  tests throughout.
- **Not changing**: `write-back` keeps its existing-rows-only contract (once a plan exists, the normal
  pass stamps it); `reconciliation-grid` (an adopted row re-reconciles under existing rules);
  templates are never created or edited.
- Docs: `SUBSYSTEMS.md` (sync model + marks sections), `ARCHITECTURE.md` key facts,
  `TS-SCHEMA.md` (insert replay note), `CLAUDE.md` invariants mirror, `DOMAIN.md` UI checklist entry.
