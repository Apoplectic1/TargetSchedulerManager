# ts-ambiguity-report — delta spec

## ADDED Requirements

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
