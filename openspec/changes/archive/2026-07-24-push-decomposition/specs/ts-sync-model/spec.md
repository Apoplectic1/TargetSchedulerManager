# ts-sync-model — delta

## ADDED Requirements

### Requirement: Push replay legs are ordered and abort cleanly
The push SHALL replay write-back entries (through the write-back writer, per plan) before manual field
entries (through the guarded field editor, in journal-sequence order), so an explicitly journaled later
edit to the same field outranks the writer's ratchet. A structural refusal detected in the write-back
leg (schema incompatible, read-only, open sidecar) SHALL refuse the whole push before any field write.
A whole-db refusal encountered during the field leg SHALL fail every remaining field entry as
not-attempted — without issuing further writes — while entries already applied stay applied and every
failed entry is retained in the journal.

#### Scenario: Later manual desired edit outranks the write-back ratchet
- **WHEN** one push replays a write-back desired ratchet and a later manual desired edit for the same plan
- **THEN** the manual field value is what the remote holds after the push

#### Scenario: Whole-db refusal mid-field-leg stops the hammering
- **WHEN** the first field write is refused for a whole-db reason (e.g. schema incompatible)
- **THEN** no further field write is attempted, every remaining entry is reported failed as not-attempted, and all failed entries stay journaled for the next push
