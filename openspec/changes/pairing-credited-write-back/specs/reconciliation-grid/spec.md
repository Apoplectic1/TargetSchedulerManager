# reconciliation-grid Delta — pairing-credited-write-back

## ADDED Requirements

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

#### Scenario: The report names the badge's cause
- **WHEN** the ambiguity report is generated while a `sentinel` badge is showing
- **THEN** the report carries an action item naming the template, the sentinel field(s), and the plans
  using it (the cause and the fix location — field obs b22d 2026-08-04; the report-side requirement lives
  in the `ts-ambiguity-report` delta)
