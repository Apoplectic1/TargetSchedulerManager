# target-and-plan-flyouts — Delta

## MODIFIED Requirements

### Requirement: Mosaic parents edit whole-mosaic knobs; panels edit as normal targets
A mosaic parent row (a grouping node with no TS target) SHALL offer the edit triggers when its TS project key
is present, opening a mosaic flyout with exactly two controls: a master "Enable all panels" checkbox
(fan-out `target.active` to every TS-backed panel, each write guarded + audited; indeterminate display when
panels disagree; a failed fan-out re-reads and displays the resulting partial state) and the TS project's
priority (one `project.priority` write — panels at priority Default inherit it in TS scoring). Panel
mini-header rows with a TS key SHALL offer the standard target flyout ("Edit panel target…"). Both mosaic
flyout rows SHALL carry the leading per-field sync-direction mark like schema-generated field rows: the
master enable's mark is the union of the panels' `target.active` field states (tooltip listing per-panel
lines), the priority's mark resolves the project's `priority` field; marks refresh after each commit.

#### Scenario: Mosaic master enable with mixed panels
- **WHEN** a mosaic has some panels enabled and some disabled and the user opens the mosaic flyout
- **THEN** the master checkbox shows indeterminate; checking it writes `target.active = 1` to every TS-backed panel

#### Scenario: Panel target edit
- **WHEN** the user clicks the edit glyph on a TS-backed panel mini-header row
- **THEN** the standard target flyout opens for that panel's TS target

#### Scenario: Fanned-out enable marks the master row
- **WHEN** two panels carry unpushed `active` writes and the user reopens the mosaic flyout
- **THEN** the master enable row shows `→` with a tooltip line per marked panel

#### Scenario: Project priority collision shows on its row
- **WHEN** the mosaic project's `priority` was changed on the rig and the user also has an unpushed
  priority write
- **THEN** the priority row shows `⇄` with both directions' lines
