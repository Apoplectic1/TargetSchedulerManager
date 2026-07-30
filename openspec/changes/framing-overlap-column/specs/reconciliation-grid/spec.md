# reconciliation-grid Delta

## ADDED Requirements

### Requirement: The Hours column is a progress gauge, not a signed sum
Every level of the grid — a leaf row, a rollup, a panel header, a target header — SHALL show in its Hours
cell either the **time still owed** or the **captured total**, by one rule: while any exposure plan at or
beneath that level still owes images, the cell SHALL show the remaining time as a **negative** value at
caution emphasis; once nothing is owed — every plan's goal met, or no plans at all — it SHALL show the
**total captured disk time** at success emphasis, unsigned. A positive value is therefore always a total
and never a surplus over a goal.

"Owed" SHALL be measured against **TS's acquired count** (desired − acquired, clamped at zero per plan
cell before summing), not against raw disk frames — write-back stamps acquired from serving frames only,
so the gauge is framing-aware: a plan whose disk directory is full of frames that do not serve its framing
still reads as owed. The debt SHALL survive a disabled plan or target — an automated enable pass may flip
targets nightly, and progress must not churn with the sky. The "remaining" sort key SHALL use the same
acquired basis, so ordering and the gauge can never call the same target differently.

Deepest-level lines SHALL state their plane's plain fact: a disk source line its captured total (quiet, no
emphasis), a plan source line its owed time — and the absent-value dash once complete, since its captured
frames are stated by the disk line beside it. A plan with a desired count of zero SHALL keep its
data-that-should-not-exist emphasis rather than reading as complete.

#### Scenario: A full disk of stray frames still reads as owed
- **WHEN** a plan wants 132 subs, its cell's disk side holds 132 frames, but only 46 serve the plan's
  framing (TS acquired = 46)
- **THEN** the cell's Hours shows the time for the 86 subs still owed, negative at caution emphasis

#### Scenario: A completed level shows what was captured
- **WHEN** every plan beneath a target header has reached its desired count
- **THEN** the header's Hours shows the target's total captured disk time, unsigned, at success emphasis

#### Scenario: A level with no plans is its captured total
- **WHEN** a disk-only target renders its header
- **THEN** its Hours is the captured total at success emphasis — nothing was ever owed

#### Scenario: Disabling a target does not clear its debt
- **WHEN** an incomplete target is disabled (by hand or by an automated visibility pass)
- **THEN** its Hours still shows the owed time — the work is unfinished, merely not scheduled

#### Scenario: A completed plan line yields to its disk sibling
- **WHEN** an expanded rollup shows a plan source line whose goal is met beside its disk source line
- **THEN** the plan line's Hours shows the dash and the disk line states the captured time
