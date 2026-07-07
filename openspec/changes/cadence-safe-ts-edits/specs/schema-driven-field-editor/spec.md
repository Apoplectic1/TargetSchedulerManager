# Delta Spec: schema-driven-field-editor (cadence-safe-ts-edits)

The confirm flow for cadence-breaking fields now exists, so the generated form stops excluding them and
gates them instead.

## MODIFIED Requirements

### Requirement: The form renders every editable field, gating cadence-breaking ones behind a confirm
The generated form SHALL render every `TsEditableSchema` field of the entity's table that is present on the
open db, in schema order, choosing the control by `TsFieldType`. Fields marked cadence-breaking
(`IsCadenceBreaking`) SHALL commit only after a confirmation dialog stating the cadence reset (scope-aware:
a project-scope field names the whole-project fan-out); declining SHALL revert the control with no write.
All other per-field commit semantics (guarded gate, revert on failure, immediate in-place mirrors) are
unchanged.

#### Scenario: Exposure-plan form includes enabled behind a confirm
- **WHEN** the user opens a plan flyout and toggles `enabled`
- **THEN** the field is present, the cadence-reset confirmation shows first, and cancel reverts the toggle with no write

#### Scenario: Project form ships filter switch frequency with fan-out wording
- **WHEN** the user commits a new `filterswitchfrequency` in the project flyout and confirms
- **THEN** the confirm named the reset of every target's rotation in that project, and the write applied through the guarded gate
