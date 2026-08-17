# project-name-altitude-clause — the derived project name

## Why

The project-name altitude clause (`"Nebulae - 45"`) is today a one-way convention: the Visible-Tonight
**Set** press writes `minimumaltitude` and rewrites the clause, but a hand edit of the Name field writes
only the name — so a typed `"… - 45"` silently lies about the constraint, and nothing detects it. The user
decree (2026-08-16) makes the clause **definitional**: every project name is `<base> - N` where N mirrors
`Project.minimumaltitude` exactly, projects only. Measured against the live db the same day: **all 10
projects already conform, zero disagreements** — this change ratifies existing practice; there is no
migration. It supersedes the queued 2026-08-12 parse-back unit (ROADMAP § Queued): with the name *derived*,
the write direction needs no parsing at all.

## What Changes

- **The project name becomes derived: base + composed clause.** The project edit dialog shows **base
  name** and **min altitude** as separate fields; the stored TS name is composed at commit
  (`base + " - " + altitude`, `0.#` format). The Name field can never express an altitude. `- 0` is legal
  and means "image as low as the horizon allows" (TS planner: `max(horizon + offset, minimumAltitude)`).
- **One grammar, tightened, in AL `MosaicConvention`** (`..\Library` — AL-first release gate applies):
  the clause is exactly the spaced `" - N"` (integer or decimal). Compose + try-read siblings join
  `StripAltitudeClause`, and the strip regex tightens from `\s*-\s*` to require the spaced form. Rationale:
  the loose strip was only safe while stripping was *symmetric*; the composed model makes it asymmetric
  (project names carry the clause, capture directories stay bare), so a hyphen-digit designation
  (`"Sh2-155"`) would mis-strip on the bare side only. Consumer-agnostic naming per shared-library
  discipline.
- **An altitude edit recomposes the name** — dialog field or Visible-Tonight Set alike: two journaled
  writes (`minimumaltitude` + `name`), two push-review lines (Set's existing precedent).
  `RenameForAltitude`'s "no clause — never invent one" branch dies: the press always composes.
- **Tripwire, detect-and-flag only** (mechanical-rotation precedent): a project whose name lacks the
  clause or whose clause value disagrees with `minimumaltitude` gets an ambiguity-report line; the remedy
  is one dialog edit or Set press. Inbound BIRDWATCHER-side renames/creations can always arrive
  nonconforming — detection is the only enforcement possible.
- **No live mirror; close-time re-reconcile.** Project name is group identity (and the mosaic parent's
  match key), so a committed base-name or altitude change rides the obs-4798 close-time no-pull
  re-reconcile — project name joins the pairing-key trigger list exactly as target name did
  (`add-target-rename` D2 precedent).
- **Composed name displays everywhere** — grid grouping headers and the Visible-Tonight project dropdown
  (the floor is visible at a glance).

## Capabilities

### New Capabilities

- `project-name-clause`: the definitional convention — every project name is `<base> - N` mirroring
  `minimumaltitude`; the composed-name model (compose at commit, decompose for display), the exact spaced
  grammar, conformance definition, projects-only scope.

### Modified Capabilities

- `schema-driven-field-editor`: the project `name` field edits as **base name**; commit composes the
  stored name from base + current altitude, and a `minimumaltitude` commit recomposes the name (two
  journaled per-field writes).
- `visible-tonight-toggle`: the Set press **always** composes the clause from the written Floor — the
  "clause-less name is never renamed" requirement inverts (that behavior is superseded by the definitional
  convention; legacy-form migration language simplifies to composition).
- `ts-ambiguity-report`: new ambiguity class — a nonconforming project name (clause missing or
  disagreeing with `minimumaltitude`) reports with the hand fix.
- `target-and-plan-flyouts`: project `name` joins the close-time re-reconcile trigger (group identity +
  mosaic parent match key), as target `name` did in `add-target-rename`.

## Impact

- **AL (`..\Library\Astronomy.Catalog\Scan\MosaicConvention.cs`)**: compose/try-read siblings, tightened
  strip regex; consumers (`TargetResolver`, TSM) inherit. AL moves → AL releases before the next TSM
  release (abort gate).
- **TSM app**: project edit dialog (base-name field + composition), `VisibleTonightPass.RenameForAltitude`
  (simplifies), `MainWindow.Flyouts` `IsPairingKey` (project name), ambiguity report (new class), grid/
  dropdown display (composed — no change expected, verify).
- **Catalog export: no contract change** — the recomposed name + altitude ride the existing v2
  `project-upsert` emission on both the push and observed paths.
- **No migration**: live data already conforms (measured 2026-08-16). The Nebulea→Nebulae spelling fix is
  user data work, out of scope.
- **ROADMAP**: the queued parse-back unit is superseded by this change.
