# edit-direction-marks — Delta

## ADDED Requirements

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

## MODIFIED Requirements

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

### Requirement: Non-marking rows are explicit
Disk-plane leaf rows SHALL never mark (leaf marks key on the plan key and its template key only;
target/project-level changes mark the header).

#### Scenario: Disk row stays blank
- **WHEN** a target header marks `→` from a target-level edit
- **THEN** the target's disk-plane leaf rows remain blank
