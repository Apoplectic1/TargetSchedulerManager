# Delta Spec: target-and-plan-flyouts (template-manager)

Adds the exposure-template editing surface: the toolbar picker, the plan-row trigger, and the shared-scope
title rule. Existing requirements are unchanged.

## ADDED Requirements

### Requirement: A toolbar picker reaches every template
The toolbar SHALL offer a "Templates…" picker listing every exposure template from the loaded graph — name,
filter, and used-by-N-plans count — including templates no visible plan uses; choosing one SHALL open the
schema-generated editor flyout for `TsTable.ExposureTemplate` keyed by the template's TS key. Before a load
completes the picker SHALL decline with a status note rather than show an empty list.

#### Scenario: Template reachable without any plan
- **WHEN** a template exists in TS with zero exposure plans referencing it and the user opens Templates…
- **THEN** it appears in the list ("used by 0 plans") and opens for editing

### Requirement: Plan rows offer their template for editing
Filter rows with a TS plan key SHALL offer a right-click "Edit template…" item that resolves the plan's
template through the loaded graph and opens the same editor flyout; rows whose template cannot be resolved
SHALL not offer the item.

#### Scenario: Edit the template behind a plan
- **WHEN** the user right-clicks the "M 81 · Ha" plan row and picks "Edit template…"
- **THEN** the flyout opens for that plan's template with every editable template field seeded from the local db

### Requirement: Template flyouts state their blast radius
The template editor flyout SHALL be titled with the template's name and its used-by count ("Template
'<name>' — used by N plan(s)"), and journaled template edits SHALL carry that label into the push review —
a template edit affects every plan using it, so the scope is always stated, never implied.

#### Scenario: Shared scope visible at commit and push
- **WHEN** the user edits moon separation on a template used by 12 plans and later opens the push review
- **THEN** both the flyout title and the review line read "Template '<name>' — used by 12 plan(s)"

### Requirement: The full template surface is editable, edit-only
The template flyout SHALL render all cadence-safe `TsEditableSchema` exposuretemplate fields — the existing
seven plus twilight level (`TwilightLevel` enum), minutes offset, the moon avoidance suite (enabled,
separation, width, relax scale, relax max/min altitude, moon-down), dither-every, and maximum humidity —
with the −1 camera-default sentinels rendering as "use default" checkboxes as today. Template creation,
deletion, and duplication SHALL remain out of scope (TS functions).

#### Scenario: Moon suite editable
- **WHEN** the user opens a template and enables moon avoidance with separation 30°
- **THEN** both writes verify, journal, and appear in the next push review under the template's label
