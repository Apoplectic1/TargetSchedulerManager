# Tasks: project-scoped-tonight

## 1. Knob plumbing

- [x] 1.1 `UpDownBox` decimal mode (`DecimalPlaces`, default 0 — existing callers unchanged): Floor
      renders one decimal, clamps 0–90; Duration range widens to 0–999
- [x] 1.2 Toolbar XAML: `Project:` ComboBox right of the Visible-Tonight label ("All projects" default,
      name-sorted project list), knob ranges updated

## 2. Selection fill

- [x] 2.1 VM: project list surfaced for the dropdown from retained `TsPlanData` (rebuilt each load,
      selection preserved by key, falls back to All)
- [x] 2.2 Selection handler: read `minimumtime`/`minimumaltitude` via the existing field-read path and
      fill the boxes; switching selections refills (discards box edits); All restores defaults-as-before

## 3. Scoped press

- [x] 3.1 `VisibleTonightPass.PlanTargets`/`PlanProjects` take an optional selected-project id
      intersecting the processed universe (null = today's behavior)
- [x] 3.2 VM press path: with a project selected, journal changed `minimumtime`/`minimumaltitude`
      first (same bulk exclusion), then run the scoped pass with the box values; status line reports
      constraint writes + enable counts; All mode writes nothing
- [x] 3.3 Draft/Closed selected: constraint write + targets flip per sky, lifecycle state untouched
      (amended universe), status line still meaningful

## 4. Tests

- [x] 4.1 Pass scoping: selected project's targets only; other projects' targets/state untouched;
      null id = unchanged behavior (existing tests keep passing)
- [x] 4.2 Press orchestration: write-only-if-changed gating; All never writes; Draft/Closed
      write-without-enables

## 5. Name tracks the clause (added before archive)

- [x] 5.1 `project.name` joins `TsEditableSchema` (Text, with the clause note) + the inbound-diff
      project columns
- [x] 5.2 `VisibleTonightPass.RenameForAltitude` (pure; null = no edit due) + press applies the rename
      in its own batch gated on the landed `minimumaltitude` outcome; failures count in the summary
- [x] 5.3 Tests: rename theory (rewrite/normalize/decimal/Off; clause-less + accurate + refused ⇒ no
      edit)

## 6. Docs + verify

- [x] 6.1 `UI.md` toolbar section + `SUBSYSTEMS.md` visible-tonight subsystem: dropdown, fill-is-read /
      Tonight-is-write, scoped universe, name-tracks-clause — same commit as the code
- [x] 6.2 Build + tests green; user field-verifies (fill, scoped enables, write in push review, All
      unchanged); archive on their word
