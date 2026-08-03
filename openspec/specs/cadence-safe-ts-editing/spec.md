# cadence-safe-ts-editing Specification

## Purpose

Library (`Astronomy.Catalog`) contract for editing TS fields whose change invalidates the derived
`filtercadenceitem` rows TS restores verbatim: clear-scope metadata, transactional invalidation,
unchanged-value skip, and override-order refusal.

## Requirements

### Requirement: Editable fields declare a cadence clear scope
`TsField` SHALL carry a clear-scope value (`None` | `Target` | `Project`) in place of the boolean
`CadenceSafe`, declaring which derived `filtercadenceitem` rows an edit of that column invalidates.
`TsEditableSchema` SHALL declare `exposureplan.enabled` with scope `Target` and
`project.filterswitchfrequency` with scope `Project`; all other fields SHALL declare `None`.
`TsEditableSchema.IsCadenceBreaking` SHALL be equivalent to "scope is not `None`".

#### Scenario: Reference exposes the scope
- **WHEN** a consumer looks up `exposureplan.enabled` / `project.filterswitchfrequency` / any other field in `TsEditableSchema`
- **THEN** the returned field's clear scope is `Target` / `Project` / `None` respectively, and `IsCadenceBreaking` returns true exactly for the first two

### Requirement: Cadence-affecting edits atomically invalidate derived rows
The column UPDATE and the deletion of the invalidated `filtercadenceitem` rows SHALL execute in a single
SQLite transaction (both applied or neither) whenever the editor writes a field whose scope is not `None`
and the new value differs from the stored value. Scope `Target` SHALL delete rows whose `targetid` is the edited
exposure plan's `TargetId`; scope `Project` SHALL delete rows whose `targetid` belongs to any target of the
edited project. Read-back verification of the updated column SHALL be preserved. The editor SHALL NOT write
`filtercadenceitem` in any way other than these deletions, and SHALL NOT modify `overrideexposureorderitem`.

#### Scenario: Disabling an exposure plan clears its target's cadence rows
- **WHEN** `SetField(ExposurePlan, <plan key>, "enabled", 0)` runs against a db where that plan's target has `filtercadenceitem` rows and the plan is currently enabled
- **THEN** the plan row reads back `enabled = 0` and the target has zero `filtercadenceitem` rows, while another target's rows are untouched

#### Scenario: Changing filter switch frequency clears cadence for the whole project
- **WHEN** `SetField(Project, <project key>, "filterswitchfrequency", <new value>)` runs against a db where two targets of that project and one target of another project have `filtercadenceitem` rows
- **THEN** both of the project's targets have zero `filtercadenceitem` rows and the other project's target keeps its rows

#### Scenario: Failure applies neither the update nor the delete
- **WHEN** the transaction cannot commit (e.g. the db becomes unwritable mid-edit)
- **THEN** the field retains its prior value and the `filtercadenceitem` rows remain intact

### Requirement: Unchanged values are a no-op
When the requested value equals the stored value (normalized comparison), a cadence-affecting edit SHALL
perform no UPDATE and no `filtercadenceitem` deletion, and SHALL report success with the row found and the
value verified.

#### Scenario: Re-submitting the current value preserves cadence rows
- **WHEN** `SetField(ExposurePlan, <plan key>, "enabled", 1)` runs against a plan already enabled whose target has `filtercadenceitem` rows
- **THEN** the result reports success and the target's `filtercadenceitem` rows are unchanged

### Requirement: Override-order rows refuse a target-scope edit
`TrySetField` SHALL refuse (structured refusal, no write, no deletion) a scope-`Target` edit when the edited
row's target has `overrideexposureorderitem` rows, using a new `RefusalReason` member distinguishable from all
existing reasons. Scope-`Project` edits SHALL NOT be refused for override-order rows (mirroring TS, whose
filter-switch-frequency path leaves them untouched). The existing guard order (schema, read-only, sidecar,
column presence) SHALL be preserved, with this check last.

#### Scenario: Plan edit refused when a custom exposure order exists
- **WHEN** `TrySetField(ExposurePlan, <plan key>, "enabled", 0)` runs and the plan's target has `overrideexposureorderitem` rows
- **THEN** the call returns the new refusal reason, and the plan's `enabled` value, the target's `filtercadenceitem` rows, and its `overrideexposureorderitem` rows are all unchanged

#### Scenario: Project edit proceeds despite a custom exposure order
- **WHEN** `TrySetField(Project, <project key>, "filterswitchfrequency", <new value>)` runs and one of the project's targets has `overrideexposureorderitem` rows
- **THEN** the edit applies, `filtercadenceitem` rows for the project's targets are deleted, and `overrideexposureorderitem` rows are untouched

### Requirement: Row insertion is a guarded library primitive
The library editor SHALL expose a guarded insert primitive for `exposuretemplate`, `target` and
`exposureplan` rows: the
caller supplies the full column payload including a minted guid; the primitive applies the existing guard
order (schema compatibility, read-only, open sidecar, column presence) before writing, executes the
INSERT in a single transaction, and read-back-verifies the inserted row. Refusals are structured, never
throws for guardable conditions. The primitive SHALL NOT invent or default contract-relevant values — a
missing required payload column is a caller bug and refuses loudly.

#### Scenario: Guards precede the insert
- **WHEN** an insert is requested while the db has an open `-wal` sidecar
- **THEN** the call returns the existing sidecar refusal and no row is written

#### Scenario: Verified insert
- **WHEN** a plan insert commits
- **THEN** the row reads back with the supplied payload (guid included) and the result reports success

### Requirement: Plan insertion is a cadence-affecting target-scope operation
Inserting an `exposureplan` row SHALL delete the parent target's `filtercadenceitem` rows in the same
transaction as the INSERT (both applied or neither) — a new plan changes the target's filter rotation
exactly like enabling one. A plan insert SHALL be refused with the existing override-order refusal when
the parent target has `overrideexposureorderitem` rows (no write, no deletion). Target and template
insertion clear nothing (a new target has no cadence rows; templates have none) and are never refused for
override-order.

#### Scenario: Plan insert clears its target's cadence
- **WHEN** a plan is inserted under a target holding `filtercadenceitem` rows
- **THEN** the plan row exists and the target has zero `filtercadenceitem` rows, other targets untouched

#### Scenario: OEO target refuses the insert
- **WHEN** a plan insert is requested under a target with `overrideexposureorderitem` rows
- **THEN** the call returns the override-order refusal and neither the plan nor any deletion is applied

#### Scenario: Failure applies nothing
- **WHEN** the transaction cannot commit mid-insert
- **THEN** no plan row exists and the `filtercadenceitem` rows remain intact
