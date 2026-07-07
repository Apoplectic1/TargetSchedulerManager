# schema-driven-field-editor — delta (exposure-zero-literal)

## MODIFIED Requirements

### Requirement: Each field commits independently through the guarded gate
A changed control SHALL commit on change/focus-loss via `TsEditGate.ApplyAsync` with the entity's audit
label. On a verified write the control keeps the value and any in-grid mirror of the field updates in place
(no grid reload). On refusal or failure the control SHALL revert to the last-known value and the outcome
SHALL be surfaced with the existing refusal/failure wording. Closing or light-dismissing the flyout SHALL
never leave an uncommitted change pending.

Effective-exposure resolution behind the exposure sentinel control SHALL treat 0 as a literal zero-second
exposure: only the −1 defer-to-template sentinel resolves through the template (matching the Library's
adjudicated contract — TS's planner tests `!= -1`). A verified write of 0 SHALL mirror the in-grid Seconds
cell to 0 at once, like any other committed value; a resolved effective exposure of 0 SHALL NOT be discarded
as unknown (null is reserved for a missing row/template or a fault, where the cell is left for the next
reload).

#### Scenario: Successful edit
- **WHEN** the user changes Priority from Default to High and focus leaves the control
- **THEN** one gate write for `target.priority` = 2 occurs, is read-back verified, appears in the diagnostics log, and the flyout stays open showing High

#### Scenario: Refused edit reverts
- **WHEN** a write is refused (e.g. db busy with an open sidecar)
- **THEN** the control returns to its prior value and the existing refusal message is shown

#### Scenario: Out-of-bounds input is bounded
- **WHEN** the user types 500 into Rotation (Max 360)
- **THEN** the committed value is clamped to the schema bounds (no out-of-range write reaches the gate)

#### Scenario: Committed zero exposure mirrors at once
- **WHEN** the user commits 0 in the plan flyout's Exposure control and the gate verifies the write
- **THEN** the row's Seconds cell updates in place without a grid reload, rendering exactly what the next
  load renders (0 on a plan+disk row; the TS-only plane's pre-existing row-model rendering shows zero
  seconds as "—") — the invariant is mirror == reload, with 0 resolved literally on both sides

#### Scenario: Sentinel write still resolves through the template
- **WHEN** the user checks "template default" (a −1 write) and the gate verifies it
- **THEN** the Seconds cell mirrors the template's default exposure resolved via the plan→template join, unchanged from today
