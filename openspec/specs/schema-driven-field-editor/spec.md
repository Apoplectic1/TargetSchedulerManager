# schema-driven-field-editor Specification

## Purpose

The reusable editing core: a form generated from `TsEditableSchema` for one TS row, seeded from the db,
committing per field through the guarded gate. Library portion (enum maps) is consumer-neutral.

## Requirements

### Requirement: The reference exposes enum value maps
`TsEditableSchema` SHALL expose, for each `EnumName` used by an editable field, an ordered list of
`(code, label)` pairs suitable for populating a selection control: `TargetPriority`
(−1 Default, 0 Low, 1 Normal, 2 High), `ProjectState` (0 Draft, 1 Active, 2 Inactive, 3 Closed),
`ProjectPriority` (0 Low, 1 Normal, 2 High). Lookup by unknown name SHALL return empty/absent, not throw.

#### Scenario: Enum-typed fields resolve to value maps
- **WHEN** a consumer resolves the `EnumName` of every `TsFieldType.Enum` field in the reference
- **THEN** each yields a non-empty ordered code/label list, and `TargetPriority` includes code −1 labeled "Default"

### Requirement: The form renders every editable field, cadence-breaking ones included
The generated form SHALL render every TsEditableSchema field of the entity's table that is present on the
open db, in schema order, choosing the control by TsFieldType - including cadence-breaking fields, which
SHALL commit like any other (no confirmation; the editor's atomic cadence clear and the reviewed push are the
safety - user decision 2026-07-07). All per-field commit semantics (guarded gate, revert on failure,
immediate in-place mirrors) are unchanged.

#### Scenario: Exposure-plan form includes enabled
- **WHEN** the user opens a plan flyout and toggles enabled
- **THEN** the write applies through the guarded gate (with its transactional cadence clear) and the in-grid checkbox mirrors

#### Scenario: Project form ships filter switch frequency
- **WHEN** the user commits a new filterswitchfrequency in the project flyout
- **THEN** the write applies and every target's cadence rows in that project were cleared atomically with it

### Requirement: The form seeds from current database values
Opening the editor SHALL read each rendered field's current value from the currently-selected TS db
(via the editor's read path), off the UI thread, and populate the controls before they accept input. A read
failure SHALL surface an error and present no editable form (no controls defaulting to fabricated values).

#### Scenario: Values reflect the db, not the grid snapshot
- **WHEN** the flyout opens for a target whose `rotation` was changed in the db after TSM's last load
- **THEN** the Rotation control shows the current db value

### Requirement: Each field commits independently through the guarded gate
A changed control SHALL commit on change/focus-loss via `TsEditGate.ApplyAsync` with the entity's audit
label. On a verified write the control keeps the value and any in-grid mirror of the field updates in place
(no grid reload). On refusal or failure the control SHALL revert to the last-known value and the outcome
SHALL be surfaced with the existing refusal/failure wording. Closing or light-dismissing the flyout SHALL
never leave an uncommitted change pending.

#### Scenario: Successful edit
- **WHEN** the user changes Priority from Default to High and focus leaves the control
- **THEN** one gate write for `target.priority` = 2 occurs, is read-back verified, appears in the diagnostics log, and the flyout stays open showing High

#### Scenario: Refused edit reverts
- **WHEN** a write is refused (e.g. db busy with an open sidecar)
- **THEN** the control returns to its prior value and the existing refusal message is shown

#### Scenario: Out-of-bounds input is bounded
- **WHEN** the user types 500 into Rotation (Max 360)
- **THEN** the committed value is clamped to the schema bounds (no out-of-range write reaches the gate)
