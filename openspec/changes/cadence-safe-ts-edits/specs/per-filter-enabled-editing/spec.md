# Spec: per-filter-enabled-editing

TSM app capability: enable/disable individual exposure plans (filter rows) in the grid, gated by a
cadence-reset confirmation. First consumer of the library's cadence-safe editing contract.

## ADDED Requirements

### Requirement: Filter rows expose an enabled checkbox
Filter rows in the reconciliation grid SHALL present the TS `exposureplan.enabled` state as a checkbox
(following the shipped target-`active` checkbox pattern). Rows without a TS-side exposure plan (disk-only
actuals) SHALL NOT present the checkbox.

#### Scenario: TS-backed filter row shows its enabled state
- **WHEN** the grid renders a filter row whose exposure plan exists in the TS db with `enabled = 1`
- **THEN** the row shows a checked checkbox; an unchecked one when `enabled = 0`; none when the row has no TS plan

### Requirement: Cadence-breaking edits confirm before writing
A click on the enabled checkbox SHALL NOT write immediately: because `TsEditableSchema` marks the field
cadence-breaking, TSM SHALL first show a confirmation dialog stating that TS's filter rotation for that target
resets (regenerated on TS's next planning pass) and that the edit lands in the local copy, reaching
BIRDWATCHER at the reviewed push. Cancel SHALL revert the checkbox with no write. The trigger SHALL be driven
by `TsEditableSchema.IsCadenceBreaking`, not a hard-coded column list.

#### Scenario: Cancel leaves everything untouched
- **WHEN** the user unchecks a filter row's enabled checkbox and cancels the confirmation
- **THEN** the checkbox returns to checked and no write reaches the local TS db (and nothing journals)

#### Scenario: Confirmed toggle journals and replays with its clear
- **WHEN** the user confirms disabling a filter and later pushes
- **THEN** the local write cleared the target's local cadence rows atomically, one journal entry exists, and the push replay performs the same transactional write + clear on BIRDWATCHER

### Requirement: Confirmed edits ride the guarded gate and update in place
A confirmed toggle SHALL route through `TsEditGate.ApplyAsync` (guarded, read-back-verified, audited,
off the UI thread). On success the row SHALL update in place (no grid reload; scroll and expansion preserved).
On any refusal or failure the checkbox SHALL revert and the outcome SHALL be surfaced to the user; the new
override-order refusal SHALL be worded to direct the user to the TS editor.

#### Scenario: Successful disable updates the row in place
- **WHEN** the user confirms disabling a filter row and the gate reports the write applied
- **THEN** the row reflects disabled without a reload, and the edit is present in the diagnostics log

#### Scenario: Override-order refusal reverts and explains
- **WHEN** the gate reports the override-order refusal
- **THEN** the checkbox reverts and the user sees a message naming the custom exposure order and pointing at the TS editor
