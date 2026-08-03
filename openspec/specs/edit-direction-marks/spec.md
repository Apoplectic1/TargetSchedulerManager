# edit-direction-marks Specification

## Purpose

Per-row sync-direction marks in the grid's leftmost column: `→` for unpushed outbound writes (derived from
the journal), `←` for inbound changes BIRDWATCHER delivered at this session's pull(s) (a pull-time field
diff, session-sticky), `⇄` for both on one row — the user's warning that a push will overwrite a rig-side
change. Headers roll up the union of their subtree's directions; tooltips carry old→new values. The same
facts also resolve at field granularity for editing surfaces (the flyout rows).

## Requirements

### Requirement: Every grid row carries one sync-direction mark
The grid SHALL show a leftmost unlabeled 3-character column on every row level (target header, mosaic
panel header, filter row, rollup detail line) containing exactly one centered mark: `←` (U+2190) when the
row has inbound changes only, `→` (U+2192) when it has outbound (unpushed) writes only, `⇄` (U+21C4) when
it has both, and blank when it has neither.

#### Scenario: Outbound-only row
- **WHEN** the user commits a desired edit on a filter row and BIRDWATCHER made no change to that plan
- **THEN** that filter row's mark reads `→`

#### Scenario: Inbound-only row
- **WHEN** the open pull finds BIRDWATCHER changed a plan's fields and the user has no unpushed write on it
- **THEN** that plan's filter row marks `←`

#### Scenario: Both directions on one row
- **WHEN** a pull recorded an inbound change on a plan's `desired` and the user then edits that plan's `exposure`
- **THEN** the row's mark reads `⇄`

#### Scenario: Clean row is blank
- **WHEN** a row has no inbound entry and no unpushed journal entry
- **THEN** its mark cell is empty

### Requirement: Outbound marks derive from the journal
A row's outbound state SHALL be derived from the live journal — any unpushed entry (Manual or WriteBack
kind) whose (table, key) matches the row — never from separately stored flags. Filter rows SHALL match on
their 1:1 plan key; write-back stamps therefore mark their plan's row `→` like any manual edit.

#### Scenario: Write-back stamp marks outbound
- **WHEN** the post-load write-back stamps a plan's `acquired` from disk (journaled, unpushed)
- **THEN** that plan's filter row marks `→`

#### Scenario: Unpushed journal survives restart
- **WHEN** the app is closed with unpushed edits and reopened (journal sidecar intact)
- **THEN** the same rows mark `→` again with no user action

### Requirement: Inbound changes are captured by a pull-time field diff
Every pull SHALL diff the pre-pull local db against the freshly pulled copy over an authored field set —
the displayed/editable columns of `target`, `exposureplan`, and `project` (per design D2), plus the
`exposuretemplate` table keyed by integer `Id` over exactly the `TsEditableSchema` editable column set
(derived, not duplicated) — recording per (table, key, column) the old and new values. A first-ever pull
(no local db yet) SHALL record nothing. A row added on the remote SHALL record one inbound entry; a row
deleted on the remote SHALL record nothing. Columns absent from either snapshot (TS schema drift) SHALL
be skipped without error.

#### Scenario: Rig-side count bump arrives as inbound
- **WHEN** the rig's TS raised a plan's `acquired` overnight and the open pull runs
- **THEN** the inbound set holds that plan's `acquired` with the pre-pull and post-pull values

#### Scenario: Rig-side template change arrives as inbound
- **WHEN** a template's `moonavoidanceseparation` was changed in NINA and the open pull runs
- **THEN** the inbound set holds that template's field with the pre-pull and post-pull values

#### Scenario: First run records nothing
- **WHEN** no local db exists and the first pull runs
- **THEN** the inbound set stays empty (nothing was previously seen to diff against)

#### Scenario: Untouched fields record nothing
- **WHEN** a pull runs and a plan's diffed columns are byte-identical before and after
- **THEN** no inbound entry exists for that plan

### Requirement: The inbound set is session-sticky and unions across pulls
The inbound set SHALL live in memory only, start empty at app launch, union in each pull's diff (latest
observation wins per field), and never be cleared by a push. It resets only by process exit.

#### Scenario: Push does not erase overnight info
- **WHEN** rows mark `←` from the open pull and the user pushes an unrelated edit (closing pull runs)
- **THEN** the `←` marks remain after the push

#### Scenario: Mid-session pull accumulates
- **WHEN** the user runs Pull-now and the remote changed additional fields since the open
- **THEN** the new fields join the inbound set and previously recorded fields remain

#### Scenario: Discard-and-pull reports the revert
- **WHEN** the user discards unpushed edits at the dirty-open prompt and the pull runs
- **THEN** the reverted fields appear as inbound entries (local value → remote value) on their rows

### Requirement: Write-back stamps mask superseded inbound actuals
When write-back journals a stamp for a plan's `acquired` or `accepted`, those columns SHALL be removed
from that plan's inbound set. Other inbound columns on the same plan — including `desired` — SHALL be
unaffected.

#### Scenario: Disk overrides the rig's actuals
- **WHEN** a pull recorded inbound `acquired` on a plan and write-back then stamps that plan's `acquired` from disk
- **THEN** the row marks `→` (not `⇄`), and after a successful push it goes blank (no stale `←`)

#### Scenario: Desired ratchet does not mask
- **WHEN** a pull recorded an inbound `desired` change on a plan and write-back stamps a desired raise on it
- **THEN** the row marks `⇄` (the rig's goal change stays visible)

### Requirement: Template changes mark every plan row using the template
A pending exposure-template change SHALL mark every filter row whose plan references that template —
outbound (`→`) from unpushed `exposuretemplate` journal entries, inbound (`←`) from pull-diffed
`exposuretemplate` fields — unioned with the row's own plan-level directions into the standard three-glyph
mark. Resolution SHALL derive the plan→template mapping from the retained graph (each plan's template id →
the template's TS key, the integer `Id` string), never from separately stored row state. A template with no
referencing plans SHALL mark no grid row (its mark remains visible in the Templates… picker).

#### Scenario: Local template edit lights all users
- **WHEN** the user enables moon avoidance on template 'H900', used by 49 plans, and the marks sweep runs
- **THEN** every filter row whose plan uses 'H900' marks `→`

#### Scenario: Rig-side template change lights all users
- **WHEN** the open pull diffs a changed `exposuretemplate` field and the user has no unpushed writes
- **THEN** every filter row whose plan uses that template marks `←`

#### Scenario: Template and plan directions union
- **WHEN** a plan row has an unpushed `desired` edit and its template arrived changed from BIRDWATCHER
- **THEN** the row marks `⇄`

#### Scenario: Zero-use template marks no row
- **WHEN** a template referenced by no plan has an unpushed edit
- **THEN** no grid row marks from it

### Requirement: Template-derived tooltip lines are attributed
A row tooltip line that originates from a template change SHALL name the template — e.g.
`→ unpushed — template 'H900': moonavoidanceenabled Off → On` — distinguishing inherited changes from the
row's own field edits, which keep their existing unattributed grammar. When the template's display name
cannot be resolved from the graph, the line SHALL fall back to the raw template key (display fallback
only).

#### Scenario: Inherited line names the template
- **WHEN** the user hovers a row marked `→` solely because its template 'H900' has an unpushed field
- **THEN** the tooltip line contains the template name 'H900' and the field's old and new values

#### Scenario: Direct and inherited lines coexist distinguishably
- **WHEN** a row has both an unpushed `desired` edit and an unpushed template field
- **THEN** the tooltip lists the `desired` line without attribution and the template line with it

### Requirement: Headers count a template field once
Header rollup SHALL include template directions: the union of pending template fields over the distinct
templates referenced by the header's plans (resolved through the graph, so plans folded into rollup cells
still contribute). Each pending (template, field) pair SHALL count once per header in the direction
summary, regardless of how many of the header's plans share that template.

#### Scenario: Shared template counts once at the header
- **WHEN** a target group holds six plans all using 'H900' and 'H900' has one unpushed field
- **THEN** the collapsed group header marks `→` and its tooltip counts 1 unpushed field

#### Scenario: Folded plan's template still rolls up
- **WHEN** the only plan using a changed template is folded into a multi-plan rollup cell (no row key)
- **THEN** its target header still marks from the template change

### Requirement: Marks resolve at field granularity for editing surfaces
The marks resolver SHALL answer per-(table, key, column): the field's direction glyph (`←` inbound-only,
`→` outbound-only, `⇄` both, blank clean) and a tooltip of that field's old→new lines in the standard
grammar, unattributed (the consuming surface names the entity). A per-field `⇄` means exactly that an
unpushed local write and a rig-side change collide on that one field — the signal that a push will
overwrite the rig's value there. Row-scoped inbound facts (the new-row entry) SHALL NOT surface through
the per-field resolution.

#### Scenario: Unpushed field resolves outbound
- **WHEN** the user has an unpushed edit on a template's `moonavoidanceenabled` and that field is resolved
- **THEN** the result is `→` with a line carrying the field's old and new values

#### Scenario: Exact-field collision resolves both-ways
- **WHEN** a plan's `desired` was changed on the rig (inbound recorded) and the user also has an unpushed
  `desired` edit on the same plan
- **THEN** that field resolves `⇄` with both directions' lines, while a sibling field with only one
  direction resolves that direction alone

#### Scenario: Clean field is blank
- **WHEN** a field has no inbound entry and no unpushed journal entry
- **THEN** it resolves a blank glyph with no tooltip

### Requirement: Reverted fields read clean on every mark surface
A field whose value has returned to its baseline (per the journal's net-no-op pruning) SHALL carry no
outbound direction anywhere marks are shown — the grid's row and header marks, the Templates… picker, the
per-field flyout marks, and the unpushed count — with no per-surface filtering (all read the pruned
journal). The field's inbound state SHALL be unaffected by the round-trip: a field that showed `←` before
the edits shows `←` again after the revert, never blank.

#### Scenario: Toggle round-trip clears the row and flyout marks
- **WHEN** the user toggles a template field off and back on with the flyout open
- **THEN** the field's flyout mark and every using plan row's `→` clear on the revert commit

#### Scenario: Revert restores the prior inbound mark
- **WHEN** a plan field carries `←` from the open pull, the user edits it (`⇄`), then reverts it to the
  pre-edit value
- **THEN** the field and its row read `←` again — the rig-side fact survives the round-trip

#### Scenario: The unpushed count excludes reverted fields
- **WHEN** two fields are edited and one is reverted to its baseline
- **THEN** the sync badge counts 1 unpushed field

### Requirement: Headers roll up the union of their subtree's directions
A target header SHALL mark with the union of directions over: its own target key, its project key, and
every TS plan key belonging to its target(s) — resolved from the retained graph so plans folded into
multi-plan rollup rows still roll up. A mosaic panel header SHALL do the same over its panel target's
keys; a mosaic parent over all panels plus the shared project key. Project-scope changes SHALL mark the
group header (the mosaic parent for mosaics) only — never the panels beneath.

#### Scenario: Collapsed group reveals a child edit
- **WHEN** a filter row inside a collapsed target group has an unpushed edit
- **THEN** the target header marks `→` while collapsed

#### Scenario: Mixed directions union at the header
- **WHEN** one child plan has inbound changes and another child plan has an unpushed edit
- **THEN** the target header marks `⇄`

#### Scenario: Folded plan still rolls up
- **WHEN** a plan whose grid cell is a multi-plan rollup (no row-level plan key) gains a journal entry
- **THEN** its target header marks `→` even though no leaf row could claim the key

#### Scenario: Mosaic project edit marks the parent only
- **WHEN** the user edits the mosaic project's priority via the parent's flyout
- **THEN** the mosaic parent header marks `→` and no panel header marks from that edit

### Requirement: Marks update in place and follow the push/discard lifecycle
Marks SHALL refresh without rebuilding the row collection (scroll position and in-progress edits
preserved): immediately after every applied edit, after a push, after a discard, and as part of every
load/filter pass. After a fully successful push, applied outbound marks SHALL clear and `⇄` SHALL
collapse to `←` where unmasked inbound remains; after a partial push, rows whose entries were retained
SHALL keep their outbound mark.

#### Scenario: Mark appears on commit
- **WHEN** the user commits an inline desired edit
- **THEN** the row's mark updates in place (no grid rebuild) before any reload

#### Scenario: Successful push collapses marks
- **WHEN** a row marked `⇄` (unmasked inbound + an unpushed edit) and the push applies fully
- **THEN** the row marks `←` afterward

#### Scenario: Partial push keeps failures marked
- **WHEN** a push applies some entries and retains one row's failed entry in the journal
- **THEN** that row still marks `→` after the push

#### Scenario: Discard clears outbound
- **WHEN** the user discards unpushed edits
- **THEN** no row marks `→` from the discarded entries

### Requirement: Marks carry old→new tooltips
A marked filter row's tooltip SHALL list one line per pending field and direction with old and new values
(inbound: pre-pull → post-pull; outbound: first journaled Old → last journaled Value). A marked header's
tooltip SHALL list attributed old→new lines for its own-scope pending fields — target-scope and
project-scope fields, which mark the header only (e.g. `→ unpushed — project '<name>': minimumaltitude
30 → 45`) — and SHALL summarize direction counts for fields rolled up from the plans and templates
beneath (whose old→new detail lives on the leaf rows). Attribution name lookup SHALL fall back to the
raw key when the graph cannot resolve a display name. Blank marks SHALL show no tooltip.

#### Scenario: Leaf tooltip shows the field change
- **WHEN** the user hovers a filter row marked `→` after editing desired 20 → 25
- **THEN** the tooltip contains a line naming `desired` with 20 and 25

#### Scenario: Project edit is attributed at the header
- **WHEN** the user edits a project's minimum altitude via the flyout and hovers the group header's `→`
- **THEN** the tooltip contains an attributed line naming the project and the field's old and new values

#### Scenario: Target edit is attributed at the header
- **WHEN** a pull records a rig-side change to a target's rotation and the user hovers its header's `←`
- **THEN** the tooltip contains an attributed line naming the target and the rotation's old and new values

#### Scenario: Rolled-up fields stay summarized
- **WHEN** the user hovers a target header whose only pending fields belong to child plans and their templates
- **THEN** the tooltip states direction counts (not per-field lines) for those rolled-up fields

### Requirement: Sessions without a pull carry no inbound marks
No `←` SHALL appear when no pull runs in a session (BIRDWATCHER unreachable, or Continue-local chosen
at the dirty-open prompt); outbound marks SHALL work unchanged. TSM SHALL NOT read the remote db to
compute inbound state outside a pull.

#### Scenario: Offline session
- **WHEN** BIRDWATCHER is unreachable at open and the session proceeds on the local db
- **THEN** no row marks `←` and edits still mark `→`

### Requirement: Non-marking rows are explicit
Disk-plane leaf rows SHALL never mark (leaf marks key on the plan key and its template key only;
target/project-level changes mark the header).

#### Scenario: Disk row stays blank
- **WHEN** a target header marks `→` from a target-level edit
- **THEN** the target's disk-plane leaf rows remain blank

### Requirement: Inserted rows mark outbound from their journal entries
An unpushed insert journal entry SHALL mark its row `→` through the same (table, key) matching as field
entries — no separate mechanism — and SHALL roll up into header marks like any plan or target entry. The
tooltip line for an insert SHALL read as a creation (the entity's identity, "new"), not as an old → new
field pair.

#### Scenario: Adopted plan marks its row
- **WHEN** an adoption inserts a plan locally (journaled, unpushed)
- **THEN** the cell's row and its target header mark `→`, with a tooltip line identifying the new plan

#### Scenario: Insert marks survive restart
- **WHEN** the app restarts with unpushed insert entries
- **THEN** the same rows mark `→` again from the persisted journal

### Requirement: Pushed inserts do not echo as inbound new-rows
The closing pull after a push that replayed inserts SHALL NOT record inbound "new row" entries for those
rows, even though their local integer id changed to the remote-minted id. The differ (or a push-time mask)
SHALL correlate the pre-pull local row with the post-pull remote row by guid and treat them as the same
row. A row genuinely added on the remote by another writer SHALL still record its inbound entry.

#### Scenario: No phantom inbound after pushing an adoption
- **WHEN** the user adopts a cell, pushes, and the closing pull lands
- **THEN** the adopted rows read blank (no `←`, no `→`) — never a spurious "new row" inbound entry

#### Scenario: A genuinely remote-added row still reports
- **WHEN** the rig added a different plan remotely and the same closing pull lands
- **THEN** that plan records its inbound new-row entry and marks `←` as today
