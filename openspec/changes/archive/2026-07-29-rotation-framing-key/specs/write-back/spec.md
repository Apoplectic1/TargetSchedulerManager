# write-back Delta

## MODIFIED Requirements

### Requirement: Write-back scope mirrors the library contract
The app SHALL apply write-back to every existing exposure plan (the planner's contract: update existing
rows, never create or delete plans). Disk truth covers absence: a plan on a target with no disk match stamps
to 0 like any other unmet spec, so stray or diverged counters (`accepted ≠ acquired`) on not-yet-shot targets
heal instead of persisting — a clean 0/0 plan diffs to a no-op and journals nothing. Identity-flagged cells
stay manual. Disk-only targets have no plan rows and SHALL be reported as ignored; disk buckets no plan
targets SHALL remain surfaced as notes/badges, not writes.

**Only frames whose framing serves the target's rotation SHALL credit the stamped count** (the same
rotation-participation rule the pairing test uses — sky framing agrees fold-180 within tolerance;
mechanical/unknown framing and rotation-less targets are not comparable and always credit). A re-framed
target must not stay stamped as though the old framing's frames still satisfied its plans: the scheduler
consuming `acquired` would then under-schedule the re-shoot. Non-serving frames remain visible as separated,
badged rows; a plan none of whose frames serve stamps to 0 like any other unmet spec. On the surgical
single-target path, a cell withheld for framing SHALL be surfaced with its reason, never dropped silently.

#### Scenario: TS-only plan with diverged counters heals to zero
- **WHEN** write-back runs over a TS-only target whose plan reads acquired=0, accepted=64
- **THEN** the local db is updated to acquired=accepted=0 and one journaled write-back entry exists

#### Scenario: Clean TS-only plans journal nothing
- **WHEN** write-back runs over a TS-only target whose plans all read acquired=accepted=0
- **THEN** no writes occur and the journal stays empty

#### Scenario: A re-framed plan stops crediting the old framing's frames
- **WHEN** a target's rotation is 50° and its frames sit 28 at 50° and 451 at 60°
- **THEN** write-back stamps acquired=28 — the 451 old-framing frames no longer count, and the scheduler
  sees the true remaining work

#### Scenario: Mechanical and flipped framings still credit
- **WHEN** a target's rotation is 0° and its frames carry only a mechanical angle, or sit at 180°
- **THEN** both credit the stamped count — mechanical is not comparable and a flip is the same footprint

#### Scenario: The surgical path says why a count did not move
- **WHEN** a single-target write-back meets a cell whose sky framing fails the anchored target's rotation
- **THEN** no write occurs for that cell and a framing-mismatch note names the frames, their framing, and
  the target rotation they fail
