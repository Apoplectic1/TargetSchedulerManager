# reconciliation-grid — delta (filter-rank-row-order)

## MODIFIED Requirements

### Requirement: Row order keeps one filter's rows contiguous
Row ordering SHALL be target, project, panel, filter, purpose, exposure, then capture configuration, then plane. The capture-configuration columns SHALL be **excluded** from sort precedence despite sitting to the left of Filter, so that every row describing one filter stays together. This is a deliberate exception to the grid's convention that sort order follows column order, and SHALL be documented as such wherever that convention is stated.

The filter step SHALL compare by a fixed display rank — **H, S, O, L, R, G, B** — not by natural
order; a filter code outside the rank SHALL sort after every ranked code, unranked codes ordering
among themselves naturally. The rank is a display convention only: it SHALL NOT affect matching,
reconciliation keys, or search. When the filter set changes, the rank list is re-specified by the
user; the system SHALL NOT infer an order for new codes beyond the after-ranked rule.

The plane tie-break SHALL order disk-backed rows before plan-only rows, so that when every other key
ties, a plan commitment renders beneath the disk evidence — the same reading as the expanded-rollup
rule.

#### Scenario: A filter's configurations stay adjacent
- **WHEN** a target has frames for two filters, each captured at two gains
- **THEN** the rows read as filter-major — both of the first filter's rows, then both of the second's — rather than grouping every row of one gain together across filters

#### Scenario: Configuration still breaks ties
- **WHEN** two rows agree on target, project, panel, filter, purpose and exposure
- **THEN** their capture configuration determines their relative order

#### Scenario: Filters render in passband rank
- **WHEN** a target expands with rows for filters B, G, H, O, R and S
- **THEN** the filter groups render in the order H, S, O, R, G, B — not alphabetically

#### Scenario: An unranked code sorts after the rank
- **WHEN** a target has rows for filter H and an unrecognized filter code
- **THEN** the H rows render first and the unrecognized code's rows follow every ranked filter's rows

#### Scenario: A full tie puts the plan row under the disk row
- **WHEN** a TS row and a Disk row agree on target, project, panel, filter, purpose, exposure and capture configuration
- **THEN** the Disk row renders above the TS row

## ADDED Requirements

### Requirement: An expanded rollup presents disk evidence first, plan commitments last
The source lines beneath an expanded Both rollup SHALL render in two blocks: disk-backed lines first
(pure-disk lines and merged lines carrying both planes), plan-only TS lines last — with exposure
seconds ascending within each block. A merged line counts as disk-backed: it is evidence of actuals,
and only the bare commitments sink to the bottom.

#### Scenario: The TS plan line renders last
- **WHEN** a Both rollup expands into disk lines at 5 s and 60 s and a plan-only TS line at 60 s
- **THEN** the lines render disk 5 s, disk 60 s, then the TS line — the plan line is last even though its exposure ties a disk line's

#### Scenario: A merged line stays with the disk block
- **WHEN** an expanded rollup contains a merged Both line (plan and frames agreeing at one sub length) beside a plan-only TS line
- **THEN** the merged line renders in the disk-backed block, above the plan-only line

#### Scenario: Seconds ascend within each block
- **WHEN** an expanded rollup carries two plan-only TS lines at 60 s and 300 s
- **THEN** they render 60 s before 300 s at the end of the group
