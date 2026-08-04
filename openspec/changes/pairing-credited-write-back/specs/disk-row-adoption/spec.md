# disk-row-adoption Delta — pairing-credited-write-back

## RENAMED Requirements

- FROM: `### Requirement: An adopted plan is born complete`
- TO: `### Requirement: An adopted plan's counts are seeded by pairing`

## MODIFIED Requirements

### Requirement: An adopted plan's counts are seeded by pairing
When the assigned template **pairs** with the disk cell under the reconciliation pairing rules, the created
plan SHALL record `desired` = `acquired` = `accepted` = the cell's disk file count (born complete): TS sees
the history as satisfied and schedules nothing until the user raises `desired`. When the assigned template
would **not** pair — the cautioned outcome — the created plan SHALL record `desired` = `acquired` =
`accepted` = 0: no disk files correspond to the plan being created, and disk is truth from the plan's first
moment, not something a later write-back pass has to correct. Either way the plan SHALL be enabled, and
`desired` remains entirely the user's number afterward.

#### Scenario: Pairing assignment is born complete
- **WHEN** a disk cell with 42 frames is adopted under a template that pairs with it
- **THEN** the plan holds desired 42, acquired 42, accepted 42, enabled

#### Scenario: Non-pairing assignment is born empty
- **WHEN** the user accepts a gain-0 template over an 18-frame gain-53 disk cell (the cautioned split)
- **THEN** the plan holds desired 0, acquired 0, accepted 0, enabled — the pushed row carries no counts the
  disk cannot back

#### Scenario: Raising desired later reopens the plan
- **WHEN** the user later edits the adopted plan's desired to 60
- **THEN** the edit flows through the normal plan-edit path (journal, marks, push) like any plan

### Requirement: Bulk accept writes one atomic batch
Accept SHALL write the included, servable cells as **one atomic local batch** through the guarded edit
funnel: the target payload first when the TS target does not exist (minted guid, chosen project, disk
centroid with RA in hours — the per-cell target-creation rules), then one plan per accepted cell, each
following the existing per-cell plan rules (counts seeded by that cell's own pairing outcome — born
complete when its template pairs, zeros when accepted under the caution — `-1` exposure sentinel on
template-default agreement, enabled). The batch SHALL journal as one group, mark its rows outbound, and
re-reconcile the grid once — each accepted cell rendering `Both` when its assigned template pairs or a TS
row beside the disk row when accepted under the caution. When the target is created, its rotation SHALL be
seeded from a sky rotation expressed by an included cell's framing cluster per the per-cell seeding rule;
mechanical or unknown angles never seed. A structural backstop refusal while building any cell's payload
SHALL abort the entire batch, naming the offending cell — no partial adoption is ever written. Busy
exclusion and discard-undo apply exactly as for per-cell adoption.

#### Scenario: Target plus all plans in one confirmed step
- **WHEN** the user accepts a target-creating bulk adoption of three cells
- **THEN** one TS target and three plans exist locally as one journaled group, each seeded by its own
  pairing outcome, and the refreshed grid shows each cell's pairing outcome

#### Scenario: Mixed pairing outcomes seed per cell
- **WHEN** a bulk adoption accepts two pairing cells of 30 and 12 frames and one cautioned non-pairing cell
- **THEN** the pairing plans hold 30/30/30 and 12/12/12 while the cautioned plan holds 0/0/0

#### Scenario: Backstop refusal aborts the whole batch
- **WHEN** a cell's payload build hits a structural refusal (e.g. the retained graph lost the disk target)
- **THEN** nothing is written — no target, no plans — and the refusal names the cell

#### Scenario: One journal group, one undo
- **WHEN** the user discards unpushed changes after a bulk adoption
- **THEN** the target and every plan from that batch vanish together with the refreshed local copy
