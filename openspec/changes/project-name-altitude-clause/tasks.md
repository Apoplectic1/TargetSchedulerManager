# project-name-altitude-clause — Tasks

## 1. AL — the one grammar (`..\Library`, ships first)

- [x] 1.1 `MosaicConvention`: tighten the clause regex to the spaced form (` - N`, integer/decimal);
  add `Compose(base, altitudeDeg)` (`0.#`, invariant), `TryReadAltitudeClause(name, out deg)`, and
  `ExtractBaseName(name)` (strips one trailing spaced clause; alone also strips the retired
  `" - Above N"` legacy suffix so recomposition heals it). Consumer-agnostic doc strings.
- [x] 1.2 AL tests: compose (integer/decimal/zero), try-read (spaced hit; hyphen-digit `Sh2-155`,
  bare-number `Abell 2218`, and legacy `Above` all miss), extract-base (clause, legacy, none,
  `Veil - 3 - 30` round-trip), tightened strip (project side strips, bare `Sh2-155` untouched;
  `Mosaic - Pleiades - 50` vs `Mosaic - Pleiades` still match through `TargetResolver`'s compare).
- [x] 1.3 AL suite green; confirm no other `StripAltitudeClause` consumer depended on the loose form.

## 2. App — composition at the two commit sites

- [x] 2.1 Project editor: seed the `name` field with `ExtractBaseName`; commit composes with stored
  altitude; a `minimumaltitude` commit journals the recomposed name as a second guarded write
  (two push-review lines). Whitespace-only base reverts at the control.
- [x] 2.2 `VisibleTonightPass`: replace `RenameForAltitude` with composition — after constraint writes
  settle, rename when `stored != Compose(ExtractBaseName(stored), storedAltitude)`; composes for
  clause-less/legacy/stale names with or without an altitude change; refused altitude write never
  renames (composes from the value actually stored).
- [x] 2.3 `IsPairingKey` (`MainWindow.Flyouts.cs`): project `name` + `minimumaltitude` join the
  close-time no-pull re-reconcile trigger; no live mirror for either.
- [x] 2.4 App tests: editor decompose/compose round-trip incl. nonconforming heal + two-entry journal;
  Set-press composition matrix (accurate name no-op · clause-less gains · legacy heals · stale rewrites ·
  refused write leaves alone · All-mode untouched); trigger-list membership.

## 3. App — tripwire

- [x] 3.1 Nonconformance check beside the existing TS-internal ambiguity checks: one action item per
  project where `name != Compose(ExtractBaseName(name), minimumaltitude)` — stored name, stored
  altitude, expected composition, dialog-or-Set remedy; rides the tripwire count.
- [x] 3.2 Tests: clause-less flagged, disagreeing flagged with both values, conforming library silent,
  count reaches the status line.

## 4. Verify + docs (same commit as code)

- [x] 4.1 Full suites green (AL + App), `openspec validate --strict` passes.
- [ ] 4.2 Display verification (user): grouping headers + VT dropdown show composed names; dialog shows
  base + altitude; an altitude edit's push review shows both lines; report lists a fabricated
  nonconforming case if convenient.
- [x] 4.3 Docs: ROADMAP — Queued parse-back unit marked superseded by this change; DOMAIN.md — the
  definitional convention joins the TS authoring conventions; UI.md/SUBSYSTEMS touches where the old
  "never invents the convention" wording survives; CHANGELOG entry.
