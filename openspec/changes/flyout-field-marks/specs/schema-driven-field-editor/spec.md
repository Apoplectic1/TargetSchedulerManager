# schema-driven-field-editor — Delta

## ADDED Requirements

### Requirement: Field rows carry a leading sync-direction mark
The generated form SHALL render a leading fixed-width mark column before the label column: each field row
shows its own `←`/`→`/`⇄` (blank when clean), resolved through an injected per-field mark resolver (the
same optional-delegate seam style as the commit and effective-value callbacks; when no resolver is
injected, no mark column renders). The mark slot SHALL reserve its width even when blank so field labels
stay mutually aligned. A marked row SHALL carry the field's old→new lines as its mark's tooltip; blank
marks SHALL show no tooltip.

#### Scenario: Pending field is marked in the flyout
- **WHEN** the user opens the template flyout for a template with an unpushed `gain` edit
- **THEN** the Gain row shows `→` with the old→new tooltip and every other clean row shows a blank,
  aligned mark slot

#### Scenario: Exact-field collision is visible where the user edits
- **WHEN** a field has both an unpushed local edit and a recorded rig-side change
- **THEN** its row shows `⇄` and the tooltip lists both directions' lines

### Requirement: Field marks refresh after every commit
After each commit completes — verified, refused, or failed — the form SHALL re-resolve every rendered
field's mark from fresh facts, so a just-committed field shows `→` immediately (and a reverted commit
shows the field's true state). The refresh SHALL resolve all fields in one resolver pass, not one fact
rebuild per field.

#### Scenario: Committing a field lights its mark live
- **WHEN** the user toggles moon avoidance in the template flyout and the write verifies
- **THEN** the Moon avoidance row's mark reads `→` without closing or reopening the flyout

#### Scenario: A refused commit leaves the true state
- **WHEN** a commit is refused and the control reverts
- **THEN** the field's mark still reflects only the facts (no `→` from the refused write)
