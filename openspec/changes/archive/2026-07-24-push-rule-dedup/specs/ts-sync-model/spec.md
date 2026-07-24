# ts-sync-model — delta

## ADDED Requirements

### Requirement: The push review and the push replay share one selection rule
The count entry the push review presents per write-back plan SHALL be selected by the same rule the
replay executes (acquired, else accepted, else the desired-only fallback) — one definition, both
consumers — so the review can never show a count change the replay does not perform. A desired-only
group SHALL display no count pair (its counts already matched disk). Likewise the review's staleness
warning SHALL negate the same baseline-match definition the pull skip rule reads; with no baseline
recorded, the warning SHALL stay silent (there is nothing to have changed *since*) even though the skip
rule treats the same state as "must pull".

#### Scenario: Desired-only group shows no phantom count change
- **WHEN** a write-back group journaled only a desired ratchet (counts already matched disk)
- **THEN** the review line for that plan shows the desired change and no count pair

#### Scenario: Staleness warning agrees with the skip rule when a baseline exists
- **WHEN** a baseline is recorded and the remote's size or mtime differs from it at push time
- **THEN** the review warns that the remote changed since the baseline — the same comparison the skip rule uses

#### Scenario: No baseline, no staleness claim
- **WHEN** a push review is built with no baseline recorded
- **THEN** no "remote changed since baseline" warning is shown, even though the pull skip rule would pull in the same state
