# target-and-plan-flyouts — Delta

## MODIFIED Requirements

### Requirement: A toolbar picker reaches every template
The toolbar SHALL offer a "Templates…" picker listing every exposure template from the loaded graph — name,
filter, and used-by-N-plans count — including templates no visible plan uses; choosing one SHALL open the
schema-generated editor flyout for `TsTable.ExposureTemplate` keyed by the template's TS key. Each picker
row SHALL additionally show the template's own sync-direction mark (`←`/`→`/`⇄`, blank when clean),
resolved from the same journal/inbound facts as the grid's column-0 marks, with the old→new tooltip on
the marked row. Before a load completes the picker SHALL decline with a "load first" status note rather
than show an empty list.

#### Scenario: Template reachable without any plan
- **WHEN** a template exists in TS with zero exposure plans referencing it and the user opens Templates…
- **THEN** the template is listed and opens its editor flyout

#### Scenario: Changed template is marked in the picker
- **WHEN** template 'H900' has an unpushed edit and the user opens Templates…
- **THEN** the 'H900' row shows `→` and its tooltip lists the pending field's old and new values

#### Scenario: Clean templates stay unmarked
- **WHEN** a template has no pending inbound or outbound fields and the user opens Templates…
- **THEN** its picker row shows no mark glyph
