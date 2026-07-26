# edit-direction-marks — Delta

## ADDED Requirements

### Requirement: Marks resolve at field granularity for editing surfaces
The marks resolver SHALL answer per-(table, key, column): the field's direction glyph (`←` inbound-only,
`→` outbound-only, `⇄` both, blank clean) and a tooltip of that field's old→new lines in the standard
grammar, unattributed (the consuming surface names the entity). A per-field `⇄` means exactly that an
unpushed local write and a rig-side change collide on that one field — the signal that a push will
overwrite the rig's value there. Row-scoped inbound facts (the new-row entry) SHALL NOT surface through
the per-field resolution.

#### Scenario: Unpushed field resolves outbound
- **WHEN** the user has an unpushed edit on a template's `moonavoidanceenabled` and that field is resolved
- **THEN** the result is `→` with a line carrying the field's old and new values

#### Scenario: Exact-field collision resolves both-ways
- **WHEN** a plan's `desired` was changed on the rig (inbound recorded) and the user also has an unpushed
  `desired` edit on the same plan
- **THEN** that field resolves `⇄` with both directions' lines, while a sibling field with only one
  direction resolves that direction alone

#### Scenario: Clean field is blank
- **WHEN** a field has no inbound entry and no unpushed journal entry
- **THEN** it resolves a blank glyph with no tooltip
