# reconciliation-grid Delta — pairing-credited-write-back

## ADDED Requirements

### Requirement: Template camera-default sentinels are badged
A template carrying a **use-camera-default sentinel** — `gain`, `offset`, or `readoutmode` = `-1` — SHALL
raise a row-scoped warning badge (`sentinel`) on every plan row using that template, rolled up to ancestors
under the existing row-scoped badge rule. Relying on a camera default is a user-defined error in this
library's authoring convention: the sentinel means the plan's capture configuration is unspecified, so it
can never pair and never credit. The badge SHALL be recomputed from current template state on every
reconciliation (load, pull, or edit-driven re-reconcile) and SHALL disappear when the template no longer
carries the sentinel. TSM SHALL never auto-correct a sentinel: the value persists locally and through push
untouched until the user hand-edits it — the badge exists to say where.

The **defer-to-explicit-value sentinels are exempt** — they defer to values the user authored, not to a
camera default: a plan `exposure` of `-1` (use the template's default exposure) and a template
`ditherevery` of `-1` (defer to the project setting) SHALL NOT raise the badge.

#### Scenario: A sentinel template badges its using rows
- **WHEN** a template's gain is `-1` and three plans use it
- **THEN** each of those plan rows carries the warning-severity `sentinel` badge, their ancestors show it
  in their rollups, and sibling rows using other templates do not

#### Scenario: Fixing the template clears the badge
- **WHEN** the user edits the template's gain from `-1` to an explicit value and the editor closes
- **THEN** the re-reconciled grid shows no `sentinel` badge on the affected rows

#### Scenario: Exempt sentinels raise nothing
- **WHEN** a plan's exposure override is `-1` (template default) and its template's ditherevery is `-1`
  (project default) but its gain/offset/readoutmode are explicit
- **THEN** no `sentinel` badge appears
