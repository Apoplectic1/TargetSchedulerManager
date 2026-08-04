# ts-ambiguity-report Delta — pairing-credited-write-back

## ADDED Requirements

### Requirement: Sentinel templates are report action items naming their cause
The report's exposure-templates section SHALL carry one action item per template whose `gain`, `offset`,
or `readoutmode` is the use-camera-default sentinel (`-1`), naming the template (name and TS Id), exactly
which field(s) carry the sentinel, and the plans using it (count plus the owning targets) — so the reader
of the report alone learns both the cause of every grid `sentinel` badge and where the hand fix goes. The
item SHALL state the consequence (plans using the template can never pair and stamp 0 while the sentinel
stands). A zero-use sentinel template is still an item (the error exists in TS regardless of use). The
exempt defer-to-explicit-value sentinels (plan `exposure` `-1`, template `ditherevery` `-1`) SHALL NOT
produce items.

#### Scenario: Sentinel template item names field and blast radius
- **WHEN** the report is built while a template carries gain `-1` and two plans on one target use it
- **THEN** the templates section holds an action item naming the template, "gain", the two plans' targets,
  and the fix (set an explicit value), and the report's action count includes it

#### Scenario: Explicit templates produce no item
- **WHEN** every template expresses gain, offset, and readout mode
- **THEN** the templates section carries no sentinel items (the section's clean marker when nothing else
  fires)

### Requirement: Multi-plan items list each plan with its containing project
Every report item that enumerates competing or context plans — held multi-plan cells, duplicate folds,
no-matching-plan context, and the TS-internal same-key check — SHALL print each plan's row with its
**containing project and TS target** (the `project › target` path the TS UI navigates by), resolved from
the raw TS snapshot so a duplicate fold's plans show their true, possibly different, homes rather than the
one canonical target the fold collapsed onto. A plan whose location cannot be resolved from the snapshot
prints without a path, never a fabricated one. (Field obs 2026-08-04: template name + counts alone did not
say where each plan lives.)

#### Scenario: Folded plans show their different projects
- **WHEN** a duplicate fold holds two plans whose TS targets live in different projects
- **THEN** each plan's row prints its own `project › target` path, so the reader can navigate to both in
  the TS UI

#### Scenario: Same-key plans name their home
- **WHEN** the same-key check reports two plans sharing one key on a TS target
- **THEN** both plan rows carry the owning `project › target` path beside the template name and counts
