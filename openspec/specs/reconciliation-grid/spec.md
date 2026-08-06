# reconciliation-grid Specification

## Purpose

Presentation requirements for the reconciliation grid — the header + row-template rendering contract.
Seeded 2026-07-24 (`grid-column-ruler`) with the column-alignment invariant; future grid-presentation
requirements accrue here.

## Requirements

### Requirement: One column geometry across the header and every row kind
The reconciliation grid's column header and every row presentation (target group, mosaic panel, filter
row, and their nested detail lines) SHALL render on one shared column geometry — the same column count,
order, and widths — so cells align vertically across row kinds. The geometry SHALL have a single
authoritative definition; header and row templates SHALL consume it rather than restate it.

#### Scenario: Cells align across row kinds
- **WHEN** a target group row, a filter row, and a mosaic panel row render under the header
- **THEN** every column's cells align vertically with the header's captions

#### Scenario: A width change propagates everywhere
- **WHEN** one column's width is changed in the authoritative definition
- **THEN** the header and all row kinds render the new width with no other edit

### Requirement: Absent values render as the em dash; real zeros render as zeros
A cell whose value is absent (no plan side, no disk side, unknown) SHALL render as the em dash ("—") —
never blank and never a fabricated 0 — while a measured zero (e.g. zero frames on disk for a TS-only
row) SHALL render as 0: the dash means "nothing to say", the zero is a fact. Hours SHALL render with one
decimal, except small non-zero magnitudes (< 0.05 h) with two — so a short-frame total reads as small
rather than missing. These conventions SHALL have a single authoritative definition consumed by every
renderer.

#### Scenario: No plan side shows dashes, not zeros
- **WHEN** a disk-only row renders its Desired/TS cells
- **THEN** they show "—" (no goal exists), while its Actual cell shows the real frame count

#### Scenario: Small hours read as small, not missing
- **WHEN** a row's hours total is 0.03
- **THEN** it renders "0.03", not "0.0"

### Requirement: Badge tokens render at one of two severities, per token
Each match-state badge token SHALL render at one of exactly two severities: **warning** — the state is
authoring the user must repair outside TSM (a duplicate, a name mismatch, an ambiguous match, multiple plans
on one filter/purpose, an accepted/acquired divergence, a coordinate-less TS target, an unrecognised camera
directory, or a capture directory disagreeing with the camera recorded inside its frames) — or **informative** —
the state is a fact carrying no call to action (a mosaic, or a target with neither plans nor scanned frames).
Repair "outside TSM" SHALL be read to include repairs made on disk or in the image-management tooling, not
only in the Target Scheduler's own interface.
Warning tokens SHALL be visually emphasised; informative tokens SHALL be visually quiet. Severity SHALL be
resolved **per token**, so a row carrying both kinds shows each at its own severity rather than promoting the
whole cell to the higher one. The token vocabulary and its severity classification SHALL have a single
authoritative definition consumed by every renderer, and the badge **text** SHALL be unchanged by severity —
the searchable badge vocabulary is unaffected.

#### Scenario: A mixed row shows each token at its own severity
- **WHEN** a row's badges are `mosaic · multi-plan`
- **THEN** `mosaic` renders quiet and `multi-plan` renders emphasised, in one cell

#### Scenario: An informative-only row does not read as a warning
- **WHEN** a mosaic parent's only badge is `mosaic`
- **THEN** the cell renders entirely quiet, with no warning emphasis anywhere in it

#### Scenario: Badge text survives severity colouring
- **WHEN** a row carrying badges is matched against a search term naming one of its tokens
- **THEN** the row still matches — severity changes presentation only, never the badge string

#### Scenario: A camera-provenance token reads as a warning
- **WHEN** a row carries `camera` or `cam≠`
- **THEN** it renders emphasised and counts as flagged, on the same footing as a name mismatch

### Requirement: A header's badge rollup is a distinct union of tokens
A collapsible header row SHALL show the union of its descendant leaves' badge **tokens**, each appearing at
most once, in first-appearance order. Deduplication SHALL operate on individual tokens, not on whole joined
badge strings, so a token common to several leaves cannot repeat in the header.

#### Scenario: A token shared by leaves appears once in the header
- **WHEN** a mosaic target's leaves carry `mosaic` and `mosaic · multi-plan` respectively
- **THEN** the target group header shows `mosaic · multi-plan`, not `mosaic · mosaic · multi-plan`

### Requirement: An unanchored TS target counts as flagged
A TS target that could not be anchored for want of usable coordinates SHALL be classified as flagged —
included in the flagged-only filter and rolled up into its ancestors' flag state — on the same footing as a
duplicate, a name mismatch, an ambiguous match, a multi-plan filter, or an accepted/acquired divergence. Such
a target is unschedulable by the Target Scheduler and can never accrue disk credit, so it is repairable
authoring rather than a neutral fact. The classification SHALL hold whether the target carries exposure plans
or none at all.

#### Scenario: A coordinate-less TS target survives the flagged-only filter
- **WHEN** the flagged-only filter is active and a TS target has no usable coordinates
- **THEN** its rows remain visible, and its warning-severity badge is consistent with the filter that kept it

#### Scenario: An unanchored target with no plans is still flagged
- **WHEN** a coordinate-less TS target has neither exposure plans nor scanned frames, rendering as a single
  bare row
- **THEN** that row is flagged and its ancestors' flag state reflects it

#### Scenario: A target with no plans and no frames but valid coordinates stays unflagged
- **WHEN** a target has usable coordinates but neither exposure plans nor scanned frames
- **THEN** it renders informative and is **not** flagged — it is queued work, not broken authoring

### Requirement: The capture configuration is visible wherever it separates rows
The grid SHALL display camera, gain, offset and binning as their own columns, positioned together between Project and Filter. Because these values decide whether rows separate, a row that stands apart from its siblings SHALL always show why: the values responsible SHALL be legible on the row itself, never left to be inferred from a difference in counts.

#### Scenario: A separated row shows the value that separated it
- **WHEN** one filter's frames render as two rows differing only in gain
- **THEN** both rows display their gain, so the reason for the separation is readable without expanding anything further

#### Scenario: A TS row shows the template's configuration
- **WHEN** a TS row renders
- **THEN** its gain, offset and binning cells show the exposure template's values, and its camera cell shows the em dash

### Requirement: Row order keeps one filter's rows contiguous
Row ordering SHALL be target, project, panel, filter, purpose, exposure, then capture configuration, then plane. The capture-configuration columns SHALL be **excluded** from sort precedence despite sitting to the left of Filter, so that every row describing one filter stays together. This is a deliberate exception to the grid's convention that sort order follows column order, and SHALL be documented as such wherever that convention is stated.

#### Scenario: A filter's configurations stay adjacent
- **WHEN** a target has frames for two filters, each captured at two gains
- **THEN** the rows read as filter-major — both of the first filter's rows, then both of the second's — rather than grouping every row of one gain together across filters

#### Scenario: Configuration still breaks ties
- **WHEN** two rows agree on target, project, panel, filter, purpose and exposure
- **THEN** their capture configuration determines their relative order

### Requirement: A rollup row shows a uniform value, or that its children disagree
A collapsible row SHALL render each capture-configuration cell as the shared value when all of its descendants agree, and as a `mixed` marker at caution emphasis when they do not. A rollup SHALL NOT render such a cell blank merely because its children disagree: silence reads as "nothing to say" when the fact to convey is "these differ".

#### Scenario: A uniform value surfaces on the rollup
- **WHEN** every frame beneath a target header was captured on one camera at one binning
- **THEN** the header's camera and binning cells show those values

#### Scenario: Disagreement surfaces before expanding
- **WHEN** the rows beneath a header carry two different offsets
- **THEN** the header's offset cell reads `mixed` at caution emphasis, identifying the inconsistent dimension before the header is expanded

#### Scenario: A rollup distinguishes which dimension differs
- **WHEN** a header's descendants share a camera and binning but differ in gain and offset
- **THEN** the camera and binning cells show their values while only the gain and offset cells read `mixed`

### Requirement: A badge marks the rows it describes and their ancestors
A badge arising from a specific row's frames SHALL appear on that row and on every collapsible row above it, and SHALL NOT appear on sibling rows it does not describe. This SHALL hold alongside badges describing a whole target, which continue to appear on all of that target's rows.

#### Scenario: A per-row badge does not spread to siblings
- **WHEN** one of a target's several filter rows draws frames from an unrecognised camera directory
- **THEN** that row carries the `camera` badge, its ancestors show it in their rollup, and the target's other filter rows do not carry it

#### Scenario: A target-scope badge still marks every row
- **WHEN** a target is a duplicate or has a name mismatch
- **THEN** every one of its rows carries that badge, unchanged by this requirement

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

### Requirement: Template camera-default sentinels are badged
A template carrying a **use-camera-default sentinel on `gain` or `offset`** (`-1`) SHALL raise a
row-scoped warning badge (`sentinel`) on every plan row using that template, rolled up to ancestors under
the existing row-scoped badge rule. Gain and offset are the fields the user's authoring convention decides
explicitly — their sentinel is an affirmative act in the source UI, **the designed representation of an
incorrect state** (relying on a camera default the convention forbids) — and they are pairing dimensions,
so the sentinel means the plan can never pair and never credit. The badge SHALL be recomputed from current
template state on every reconciliation (load, pull, or edit-driven re-reconcile) and SHALL disappear when
the template no longer carries the sentinel. TSM SHALL never auto-correct a sentinel: the value persists
locally and through push untouched until the user hand-edits it — the badge exists to say where.

Sentinels that are a field's **designed representation of a correct state are correct by construction,
not violations**, and SHALL NOT raise the badge (user framing 2026-08-04 — these are not "exemptions";
the rule never applied):
a template `readoutmode` of `-1` (the source UI's blank "camera decides" default — never an authoring
act, and not a pairing dimension), a plan `exposure` of `-1` (use the template's default exposure), and a
template `ditherevery` of `-1` (defer to the project setting).

#### Scenario: A sentinel template badges its using rows
- **WHEN** a template's gain is `-1` and three plans use it
- **THEN** each of those plan rows carries the warning-severity `sentinel` badge, their ancestors show it
  in their rollups, and sibling rows using other templates do not

#### Scenario: Fixing the template clears the badge
- **WHEN** the user edits the template's gain from `-1` to an explicit value and the editor closes
- **THEN** the re-reconciled grid shows no `sentinel` badge on the affected rows

#### Scenario: Designed deferral states raise nothing
- **WHEN** a template's readoutmode is `-1` (blank in the source UI), a plan's exposure override is `-1`
  (template default), and the template's ditherevery is `-1` (project default), but gain and offset are
  explicit
- **THEN** no `sentinel` badge appears — those sentinels are the fields' correct deferral representations

### Requirement: A TS target without a rotation is badged
A TS-backed target whose rotation is NULL SHALL carry a target-scope warning badge (`no-rotation`) on
every one of its rows and count as flagged. Rotation is a required parameter of the authoring convention
(user 2026-08-04, obs 7c5e); the badge is **distinct from `framing`** — `framing` marks frames whose sky
rotation fails a rotation the target *has*; `no-rotation` marks the rotation being absent. The normal
producer is adoption from mechanical-only disk framing: a mechanical angle never converts to sky, the
created target's rotation stays NULL (correct — never a fabricated angle), and this badge is the standing
reminder to supply one. Disk-only targets (no TS row) have no rotation requirement and never carry it.
No adoption-time dialog caution exists — the badge is the surface (user decision).

#### Scenario: Mechanical-only adoption yields a badged target
- **WHEN** a whole-target adoption seeds no rotation (all framings mechanical) and the grid re-reconciles
- **THEN** every row of the created target carries the warning `no-rotation` badge and the target is
  flagged, until the user sets a rotation (TSM target editor or TS UI)

#### Scenario: A rotation clears it
- **WHEN** the user sets the target's rotation and the editor closes
- **THEN** the re-reconciled grid shows no `no-rotation` badge on that target

#### Scenario: Disk-only targets are not badged
- **WHEN** a disk-only target's frames carry only mechanical rotation
- **THEN** no `no-rotation` badge appears — there is no TS target to require one

#### Scenario: The report names the badge's cause
- **WHEN** the ambiguity report is generated while a `sentinel` badge is showing
- **THEN** the report carries an action item naming the template, the sentinel field(s), and the plans
  using it (the cause and the fix location — field obs b22d 2026-08-04; the report-side requirement lives
  in the `ts-ambiguity-report` delta)

### Requirement: Filter-keyed row background wash on filter-level rows
Every filter-level row (filter leaf, mixed rollup, and nested detail line) SHALL render a background
wash spanning the **Camera through Actual columns inclusive** (the capture-configuration + filter +
count band; the identity/text columns left of Camera and the Hours/Plans/Badges columns right of
Actual stay unwashed), keyed by its filter code, from the fixed palette:
`O` (0.00, 0.82, 1.00) cyan · `H` (0.77, 0.08, 0.24) crimson · `S` (0.86, 0.00, 1.00) magenta ·
`B` (0.00, 0.00, 1.00) blue · `G` (0.00, 1.00, 0.24) green · `R` (1.00, 0.00, 0.00) red
(normalized RGB, rendered at a low alpha tuned for the dark theme; hues are contrast-separated from
the natural passband colors — at wash alpha luminance vanishes, so neighboring filters split by hue —
with **R the pure-red anchor**: letter-fidelity over passband-fidelity, user call 2026-08-05). Target group headers and mosaic
panel mini-headers span filters and SHALL stay plain. `L` and any filter code outside the palette
SHALL render plain — no wash, no fallback hue, no warning: plain is the designed answer.

The wash is an identity layer beneath the grid's existing state language: cell-scoped fills
(caution / success / critical, `mixed` pills) SHALL render on top of it unchanged, row hover
feedback SHALL remain visible through it, and the wash SHALL NOT participate in search, flagging,
sorting, or any reconciliation key. Final wash strength is settled by the author's visual sign-off.

#### Scenario: Palette filter tints
- **WHEN** a target expands and an H-filter plan row renders
- **THEN** the row's Camera→Actual column band carries the low-alpha H wash, and sibling O/S/R/G/B rows each carry their own palette wash

#### Scenario: L and unknown filters stay plain
- **WHEN** an L-filter row or a row whose filter code is outside the palette renders
- **THEN** the row background is plain, indistinguishable from the pre-wash rendering

#### Scenario: Headers stay plain
- **WHEN** a target group header or mosaic panel mini-header renders above expanded filter rows
- **THEN** it carries no filter wash regardless of the filters beneath it

#### Scenario: State fills render above the wash
- **WHEN** a washed row carries a caution Hours pill or a `mixed` Seconds pill
- **THEN** the pill renders on top of the wash with its meaning and legibility intact
