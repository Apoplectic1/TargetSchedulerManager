# schema-driven-field-editor — delta

## ADDED Requirements

### Requirement: Sentinel columns render as their meaning with arm-before-write editing
A numeric column carrying a defer-to-default sentinel SHALL render as a "use default" checkbox over a
number box — never as the raw sentinel value. The checkbox SHALL be checked exactly when the column holds
the sentinel (the box then disabled, showing the resolved default when it can be known). Checking SHALL
commit the sentinel; unchecking SHALL only arm the box (enabled, seeded with the resolved default,
focused) — the override value commits only when the user confirms a number, never from the uncheck
gesture alone. The sentinel value itself SHALL be exempt from the schema Min/Max clamp. A failed or
refused commit SHALL restore the full compound state — checkbox, box enablement, and value — to what the
column actually holds.

#### Scenario: Unchecking writes nothing
- **WHEN** the user unchecks "use default" and then light-dismisses the flyout without confirming a number
- **THEN** no write occurred and the column still holds the sentinel

#### Scenario: Failed sentinel write restores the compound state
- **WHEN** checking "use default" commits the sentinel and the write is refused
- **THEN** the checkbox returns to unchecked, the box re-enables, and it shows the last real value

#### Scenario: Failed override while the column holds the sentinel
- **WHEN** the user confirms an override number, the write fails, and the column still holds the sentinel
- **THEN** the cell returns to the checked-default presentation (box disabled, resolved default shown)
