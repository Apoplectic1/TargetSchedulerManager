# ts-ambiguity-report Specification

## Purpose

The printable ambiguity roll-up — the tripwire's detail (decision 2026-07-08: TSM detects, the user repairs
by hand in NINA's TS UI). One report gathers every detected TS/disk ambiguity with what · why · the exact
hand fix, in rig vocabulary; the status line carries the action count after each load. (The original
alias-fold informational handling was removed before this capability archived — a multi-claim is always a
flagged duplicate; see `archive/2026-07-23-remove-alias-fold`.)
## Requirements
### Requirement: One printable report rolls up every detected ambiguity
The app SHALL generate, on the user's demand, a single dated Markdown report containing every ambiguity known
to the current load — the build report's target issues (name-mismatch, ambiguous match, duplicate fold,
unanchored, invalid), write-back's held cells, reconcile notes, and the TS-internal checks — grouped into
sections by where the fix is made, with each section printing an explicit clean marker when it has no items.

#### Scenario: Report on the current real data
- **WHEN** the user invokes the report on a load whose write-back held cells for a name-mismatched target and a
  same-key multi-plan target
- **THEN** the report contains an item for each, in the identity and plans sections respectively

#### Scenario: Clean system produces an affirmative report
- **WHEN** every check finds nothing
- **THEN** the report still generates, every section shows its clean marker, and the item count is 0

### Requirement: Every action item states what, why, and the exact hand fix
Each action item SHALL name the entity **as NINA's Target Scheduler UI shows it** — targets as
project › target name, plans by their exposure-template name plus the distinguishing desired/acquired counts —
never by raw database ids or guids (which are meaningless at the rig). Each item SHALL state the rule or check
it tripped and give the concrete edit to perform in the TS UI. A name-mismatch on a composite mosaic/panel
path SHALL describe the panel-token disagreement rather than prescribe a catalog-token rename (which would
name the mosaic prefix). The report SHALL make no edits itself and SHALL create no persistent state other
than the report file.

#### Scenario: Name-mismatch fix instruction
- **WHEN** a disk directory coordinate-matches a TS target but name validation failed
- **THEN** the item shows both names and the separation, and instructs renaming the TS target to the disk
  directory's catalog token — with no database id shown

#### Scenario: Panel-path mismatch describes, never prescribes
- **WHEN** the mismatched disk unit is a mosaic panel (composite directory/panel path)
- **THEN** the item names the TS panel and the claimed disk panel and asks which panel it really is, and does
  not instruct renaming to the mosaic directory's token

#### Scenario: Stray same-key plan fix instruction
- **WHEN** one target carries two plans at the same (filter, purpose, effective seconds)
- **THEN** the item lists each plan by template name with desired/acquired counts (the values that tell them
  apart in the TS UI) and instructs deleting or re-timing all but one

### Requirement: TS-internal checks cover what the grid cannot badge
The report SHALL include three checks computed over the loaded graph independent of disk matching: two or more
exposure plans on one target sharing (filter, purpose, effective whole-second exposure) — across all TS-sourced
targets, not only disk-matched ones; planned-only twin targets (same normalized name, or a
pair within the load's match tolerance, among targets with no disk anchor); and duplicate exposure-template
names within a profile.

#### Scenario: Planned-only twins are visible for the first time
- **WHEN** two TS targets with the same name and coordinates exist and neither has a disk directory
- **THEN** the report carries a twin item naming both TS Ids (the grid shows two unbadged rows today)

#### Scenario: Same-key check spans planned-only targets
- **WHEN** a planned-only target carries duplicate same-key plans
- **THEN** the report flags it even though write-back's planner (scoped to Both) never saw it

### Requirement: The tripwire count is visible without opening the report
After each load the app SHALL surface the action-item count in the status line when it is non-zero, and the
report action SHALL be available from the toolbar whenever a load has completed.

#### Scenario: Non-zero count after load
- **WHEN** a load completes and the checks yield three action items
- **THEN** the status line includes the count and the toolbar action is enabled

#### Scenario: Zero count stays quiet
- **WHEN** a load completes clean
- **THEN** the status line adds nothing, and the toolbar action still generates the affirmative report on demand

### Requirement: The report is a persistent file opened in the default handler
Invoking the report SHALL write a dated Markdown file under the app's local Reports folder and open it with the
system default handler; a failed launch SHALL be non-fatal, leaving the file in place and surfacing its path.

#### Scenario: Generate and open
- **WHEN** the user clicks the report action
- **THEN** a dated `.md` file exists under the Reports folder and the default handler is invoked on it

#### Scenario: Launch failure leaves the file
- **WHEN** the shell launch fails
- **THEN** no error dialog interrupts, the file remains, and the status line shows where it was written

### Requirement: Sentinel templates are report action items naming their cause
The report's exposure-templates section SHALL carry one action item per template whose `gain` or `offset`
is the use-camera-default sentinel (`-1`), naming the template (name and TS Id) and exactly which field(s)
carry the sentinel — the **what and why only**: no roll of the plans or targets using the template (the
grid's badge already marks those rows, and the list clutters the file — user decision 2026-08-04). The
item SHALL state the consequence (plans using it can never pair and stamp 0) and the fix. A zero-use
sentinel template is still an item (the error exists in TS regardless of use). Sentinels that are a
field's designed deferral state (template `readoutmode` `-1` — the source UI's blank "camera decides"
default; plan `exposure` `-1`; template `ditherevery` `-1`) are correct by construction and SHALL NOT
produce items.

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

### Requirement: Mechanical-only framings are enumerated as informational items
The report's informational section SHALL list every in-scope target whose disk framings express only
mechanical rotation (no sky angle recorded), naming the target with its project prefix, the folded
mechanical angle(s), and the number of frames, with a pointer to the measurement fix (plate-solving
the frames). These are informational, not action items — a mechanical angle is a missing measurement,
not a slipped authoring convention.

#### Scenario: Mechanical framing listed
- **WHEN** an in-scope target's framing carries mechanical-only rotation
- **THEN** the informational section names the target, its folded `°(M)` angle(s), and its frame count

#### Scenario: Sky framing not listed
- **WHEN** a target's framings all express sky rotation
- **THEN** no mechanical-rotation line appears for it

