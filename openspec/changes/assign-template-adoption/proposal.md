# Proposal: assign-template-adoption

## Why

Field use of `adopt-disk-rows` (obs 3dfe, 2026-08-03) rejected its central premise: adoption should
**assign existing exposure templates, not create new ones** — templates are curated in TS, TSM only points
at them. The shipped flow's four personalities (silent unique match, ambiguous hold, zero-match hold,
creation form) collapse into one always-shown assignment dialog: pick a project (when the target needs
creating), pick a template, accept or cancel. Any plan adjustment happens afterward through the ordinary
plan editor — per-plan overrides beyond exposure seconds cannot exist anyway (`exposureplan` carries only
`exposure`/`desired`/`acquired`/`accepted`/`enabled`; gain/offset/bin live solely on the template).

## What Changes

- **One assignment dialog, always shown** on the adoption action: project dropdown (live only when the
  target must be created; locked to the owning project otherwise — which also pins every later filter of a
  multi-filter target to the same project) + template dropdown (strictly scoped to same filter + same bin,
  preselected to the best match), Accept/Cancel. No editable value fields — the plan is born complete from
  disk facts; edits go through the plan editor afterward.
- **The matcher demotes from gatekeeper to preselector.** Exact expressed-and-equal match preselects; it
  can no longer block. **BREAKING (spec-level):** the silent unique-match path, ambiguity/zero-match holds,
  and the template creation form are removed.
- **Mismatch is a caution, not a refusal**: when the chosen template would not merge with the disk cell
  (purpose or expressed gain/offset disagreement), the dialog states inline that the plan will appear as a
  separate TS row beside the disk row, not `Both` — informed choice, never a surprise, never a block.
- **An empty strict scope refuses honestly**: no same-filter/same-bin template → the dialog says so and
  Accept disables; the fix (create a template) belongs in TS.
- **Deletions**: `TemplateCreateOffer`, `PendingTemplate`, `TemplateFormResult`, `AdoptionHold`, the
  creation-form dialog, the hold dialog, and their VM hooks. Template inserts stop originating from
  adoption (the journal/replay template leg remains as generic sync capability).
- After accept, the UI just refreshes (re-reconcile, marks, dirty badge) — no auto-opened editor.
- **All dialogs open centered** (added mid-change, user call after obs 3eba): open-near-the-row anchor
  seeding is retired — it raced ContentDialog layout and could land the dialog off-screen as an
  invisible input-eating modal. Movability by drag stays.

## Capabilities

### New Capabilities

_None._

### Modified Capabilities

- `disk-row-adoption`: the template-selection requirement is replaced wholesale (auto-match/hold/creation
  form → always-shown assignment dialog with strict-scope dropdown, preselection, mismatch caution, empty-
  scope refusal); the project-picker requirement generalizes to the unified dialog (locked project when the
  target exists); born-complete and sync-model requirements gain no new behavior but their scenarios adjust
  to the dialog flow (e.g. a mismatched assignment legitimately renders split rows, not `Both`).

## Impact

- **App:** `Services/AdoptionPlanner.cs` (match → preselect; delete offer/pending/hold types),
  `ViewModels/MainViewModel.Edits.cs` + `MainViewModel.Sync.cs` (one dialog hook replaces three),
  `MainWindow.Dialogs.cs` (unified `ShowAdoptDialogAsync`; delete creation-form + hold dialogs),
  `MainWindow.Flyouts.cs` (menu wiring unchanged).
- **Tests:** `AdoptionPlannerTests` reshaped (preselection ranking, strict scope, caution predicate);
  creation-form/hold paths removed from `TsInsertSyncTests` fixtures where they exercised template inserts
  via adoption (generic insert-replay tests stay — the sync capability is unchanged).
- **Library:** no changes (`TryInsertRows` template support stays as-is).
- **Docs:** SUBSYSTEMS/DOMAIN/CHANGELOG/ROADMAP in the ship commit.
