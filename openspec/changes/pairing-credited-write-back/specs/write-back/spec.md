# write-back Delta — pairing-credited-write-back

## MODIFIED Requirements

### Requirement: Write-back scope mirrors the library contract
The app SHALL apply write-back to every existing exposure plan (the planner's contract: update existing
rows, never create or delete plans). Disk truth covers absence: a plan on a target with no disk match stamps
to 0 like any other unmet spec, so stray or diverged counters (`accepted ≠ acquired`) on not-yet-shot targets
heal instead of persisting — a clean 0/0 plan diffs to a no-op and journals nothing. Identity-flagged cells
stay manual. Disk-only targets have no plan rows and SHALL be reported as ignored; disk buckets no plan
targets SHALL remain surfaced as notes/badges, not writes — including the buckets separated only by a
capture-configuration disagreement, however many that produces: the report is complete, never sampled.

**A frame SHALL credit a plan's stamped count only when it would pair with that plan under the
reconciliation pairing rules** — the same test that renders the grid's `Both` rows. Two dimensions of that
rule apply here beyond `(target, filter, purpose, seconds)`:

- **Capture configuration (gain / offset / binning):** a dimension separates when both planes express it
  and the values disagree; a plane that does not express a dimension cannot separate on it. A template
  carrying a **use-camera-default sentinel** (`-1`) SHALL never credit on that dimension — an unspecified
  value can never be asserted to agree — so its plans credit nothing while the sentinel stands. Frames at a
  configuration no plan pairs with are the disk-side split the grid already renders; they SHALL NOT credit
  the non-matching plan.
- **Framing:** only frames whose framing serves the target's rotation SHALL credit (sky framing agrees
  fold-180 within tolerance; mechanical/unknown framing and rotation-less targets are not comparable and
  always credit).

A plan none of whose frames pair stamps to 0 like any other unmet spec — the scheduler consuming `acquired`
must see the true remaining work, whether the frames left by re-framing, a configuration change, or never
existed. Non-crediting frames remain visible as separated, badged rows. On the surgical single-target path,
a cell withheld for framing or configuration SHALL be surfaced with its reason, never dropped silently.

#### Scenario: TS-only plan with diverged counters heals to zero
- **WHEN** write-back runs over a TS-only target whose plan reads acquired=0, accepted=64
- **THEN** the local db is updated to acquired=accepted=0 and one journaled write-back entry exists

#### Scenario: Clean TS-only plans journal nothing
- **WHEN** write-back runs over a TS-only target whose plans all read acquired=accepted=0
- **THEN** no writes occur and the journal stays empty

#### Scenario: A non-pairing configuration stops crediting
- **WHEN** a plan's template expresses gain 0 and the target's 18 same-`(filter, purpose, seconds)` frames
  carry gain 53
- **THEN** write-back stamps acquired=accepted=0 — the frames stay a separate disk row, and the grid, the
  stamped counts, and the push review all tell the same story

#### Scenario: An adopted plan that never matched heals on the next load
- **WHEN** a plan created by a cautioned non-pairing adoption arrives from BIRDWATCHER carrying non-zero
  counts
- **THEN** the next load stamps it to 0 with a journaled decrease surfaced in the push review

#### Scenario: A sentinel template credits nothing
- **WHEN** a plan's template carries gain `-1` (use camera default) and matching-configuration frames exist
  on disk
- **THEN** the plan stamps to 0 — the sentinel never pairs — and the sentinel is surfaced as a grid badge,
  not silently tolerated

#### Scenario: An unexpressed dimension does not separate
- **WHEN** a plan's frames carry no recorded value for a dimension the template expresses
- **THEN** that dimension does not withhold credit; pairing is judged on the dimensions both planes express

#### Scenario: A re-framed plan stops crediting the old framing's frames
- **WHEN** a target's rotation is 50° and its frames sit 28 at 50° and 451 at 60°
- **THEN** write-back stamps acquired=28 — the 451 old-framing frames no longer count, and the scheduler
  sees the true remaining work

#### Scenario: Mechanical and flipped framings still credit
- **WHEN** a target's rotation is 0° and its frames carry only a mechanical angle, or sit at 180°
- **THEN** both credit the stamped count — mechanical is not comparable and a flip is the same footprint

#### Scenario: The surgical path says why a count did not move
- **WHEN** a single-target write-back meets a cell whose sky framing or capture configuration fails the
  anchored target's plan
- **THEN** no write occurs for that cell and a note names the frames, the failing dimension, and the plan
  value they fail
