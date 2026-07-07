# Delta Spec: schema-driven-field-editor (cadence-safe-ts-edits)

The editor's atomic cadence clear makes cadence-breaking fields safe to commit like any other, so the
generated form stops excluding them (no confirm gate - user decision 2026-07-07).

## MODIFIED Requirements

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
