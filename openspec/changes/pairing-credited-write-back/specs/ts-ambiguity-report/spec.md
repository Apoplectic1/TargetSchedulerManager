# ts-ambiguity-report Delta — pairing-credited-write-back

## ADDED Requirements

### Requirement: Sentinel templates are report action items naming their cause
The report's exposure-templates section SHALL carry one action item per template whose `gain`, `offset`,
or `readoutmode` is the use-camera-default sentinel (`-1`), naming the template (name and TS Id) and
exactly which field(s) carry the sentinel — the **what and why only**: no roll of the plans or targets
using the template (the grid's badge already marks those rows, and the list clutters the file — user
decision 2026-08-04). The item SHALL state the consequence **accurately per field class**: a gain or
offset sentinel means the plans can never pair and stamp 0; a readout-mode-only sentinel is an authoring
error whose counts are unaffected (readout mode is not a pairing dimension — the disk plane does not
express it). A zero-use sentinel template is still an item (the error exists in TS regardless of use).
The exempt defer-to-explicit-value sentinels (plan `exposure` `-1`, template `ditherevery` `-1`) SHALL
NOT produce items.

#### Scenario: Sentinel template item names field and consequence, never the using plans
- **WHEN** the report is built while a template carries gain `-1` and two plans on one target use it
- **THEN** the templates section holds an action item naming the template, "gain", the accurate
  consequence, and the fix (set an explicit value) — with no using-plans/targets list — and the report's
  action count includes it

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

### Requirement: Report generation scopes to the current grid filter
When the user generates the report while the grid is filtered (search text, source filter, or
flagged-only), the written report SHALL cover only the currently visible targets: every target-attributable
item — identity, duplicates, plan cells, sentinel templates (via their using plans; zero-use templates are
excluded under scope), unreadable files (by directory), and informational notes — is limited to that set,
the action count reflects the scope, and the report header SHALL state the scope (the active filter and the
visible-target count) so a scoped report can never be mistaken for the full one. With no filter active the
full report is written, unchanged. The automatic tripwire count (status line) SHALL remain global — scope
applies only to the user-invoked report generation. (Field obs a5eb 2026-08-04: search isolating one target
should yield that target's report.)

#### Scenario: Search-isolated target scopes the report
- **WHEN** the grid search isolates one target and the user generates the report
- **THEN** the file contains only items attributable to that target, its header names the search scope, and
  the action count counts only those items

#### Scenario: No filter writes the full report
- **WHEN** no search/source/flagged filter is active
- **THEN** the generated report is the full, unscoped report

#### Scenario: The tripwire stays global
- **WHEN** a search is active and a load completes
- **THEN** the status line's ambiguity count still reflects the whole library, not the filtered view
