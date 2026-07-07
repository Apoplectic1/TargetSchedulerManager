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

### Requirement: The editor form is generated from the reference
`TsFieldsEditor` SHALL render, for a given `TsTable`, exactly the schema's editable fields for that table that
are not cadence-breaking (`IsCadenceBreaking` = false), in schema order, choosing the control by `TsFieldType`
(Bool→toggle, Whole→integer numeric, Real→decimal numeric, Enum→dropdown from the enum map, Text→text box),
applying `Min`/`Max` as input bounds, showing `Unit` beside the control and `Notes` as a tooltip. No
field-specific UI code SHALL be required to add a future field.

#### Scenario: Target form contents
- **WHEN** the editor is opened for `TsTable.Target`
- **THEN** it shows Enabled (toggle), Priority (dropdown: Default/Low/Normal/High), Rotation (decimal, 0–360 °) — and nothing else (`roi` was removed from the editable surface on user feedback, 2026-07-06)

#### Scenario: Exposure-plan form excludes cadence-breaking fields
- **WHEN** the editor is opened for `TsTable.ExposurePlan`
- **THEN** it shows Desired (integer) and Exposure (decimal, s) but not `enabled` (cadence-breaking today)

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
