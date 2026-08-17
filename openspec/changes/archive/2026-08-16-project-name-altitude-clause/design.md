# project-name-altitude-clause — Design

## Context

See `proposal.md` → Why. Current state: the one grammar lives in AL
`MosaicConvention.StripAltitudeClause` with a **loose** regex (`\s*-\s*(Above\s*)?\d+(\.\d+)?\s*$` —
zero spaces tolerated), consumed by `TargetResolver` (scope-key/name compare) and TSM
(`VisibleTonightPass.RenameForAltitude`). The project editor is the ordinary schema-driven form
(`TsEditableSchema` project rows; `name` is a plain Text field). The close-time re-reconcile trigger is
`IsPairingKey` in `MainWindow.Flyouts.cs` (target case gained `name` in `add-target-rename`). Measured
2026-08-16: all 10 live projects already conform in the short spaced form; zero legacy `Above` names;
zero hyphen-digit *project* names (targets like `Sh2-155` exist, but target names never carry clauses).

Two-repo change: grammar in AL (`..\Library`, AL-first release gate), everything else app-side.

## Goals / Non-Goals

**Goals**
- One grammar, one home: compose / try-read / base-extract / strip all in `MosaicConvention`, all
  agreeing on the spaced `" - N"` clause shape.
- Zero parsing in the write direction — the stored name is always a composition.
- Every existing surface (Set, dialog) doubles as the nonconformance remedy.

**Non-Goals**
- No migration machinery (data already conforms) and no auto-repair of inbound names (detect-only).
- No target-name clauses, ever.
- No new UI surface — the existing dialog, Set button, and ambiguity report carry everything.
- The Nebulea→Nebulae spelling fix is user data work, not part of this change.

## Decisions

### D1 — The grammar API: four verbs on `MosaicConvention`, spaced-clause only

`Compose(base, altitudeDeg)` (`0.#`, invariant culture), `TryReadAltitudeClause(name, out deg)`,
`ExtractBaseName(name)`, and the existing `StripAltitudeClause` tightened to require the spaced form
(`\s+-\s\d…` — literally one-or-more spaces, dash, one space? No: exactly the composed shape ` - N`,
tolerant of *extra* internal whitespace but requiring at least one space on each side of the dash).
**Why tighten:** the loose regex was only safe while stripping was symmetric; composed names make it
asymmetric (project carries the clause, directories stay bare), so `"Sh2-155"` would strip to `"Sh2"`
on the bare side only and break the match. Requiring the spaces makes hyphen-digit designations inert.
*Alternative rejected:* keeping the loose strip for the matcher and a tight parse for the dialog — two
grammars is exactly the divergence the one-grammar rule exists to prevent, and the loose matcher is
provably wrong post-composition.

**Legacy `Above` handling:** `ExtractBaseName` alone also strips the retired `" - Above N"` suffix, so
recomposition *heals* a legacy name instead of nesting it. `TryReadAltitudeClause` and
`StripAltitudeClause` (the matcher path) do **not** recognize it — a legacy name is simply
nonconforming until any recomposition touches it. Naming stays consumer-agnostic (no TSM vocabulary in
AL doc strings).

### D2 — Composition sites: exactly two, both existing commit paths

1. **The project editor** (`TsFieldsEditor` / project dialog): the `name` field seeds with
   `ExtractBaseName(stored)`, commits `Compose(base, storedAltitude)`; a `minimumaltitude` commit also
   journals `Compose(currentBase, newAltitude)` as a second write. Two journal entries = two push-review
   lines = two v2 `project-upsert`-relevant fields, all riding existing machinery (Set's precedent).
2. **The Set press** (`VisibleTonightPass.RenameForAltitude` → renamed/simplified to a composition):
   after constraint writes settle, if `stored name != Compose(ExtractBaseName(name), storedAltitude)`,
   journal the rename. This makes Set a remedy for clause-less/legacy/stale names **even when the
   altitude didn't change** — the "never invent" branch dies by decree.

*Alternative rejected:* composing inside the journal/gate layer (one choke point) — the gate is
field-generic and must not grow project-name special cases; two explicit call sites beside the code
that already writes names is the invariants-at-the-enforcement-point pattern.

### D3 — Tripwire: a resolver-adjacent check feeding the existing report

Nonconformance detection (`name != Compose(ExtractBaseName(name), minimumaltitude)`) runs where the
other TS-internal checks run (the `CatalogBuildReport`/ambiguity pipeline that already enumerates
sentinel templates and mechanical rotations), producing one action item per nonconforming project with
the expected composition and the dialog-or-Set remedy. No badge — project rows aren't badge carriers;
the report + tripwire count is the surface (mechanical-rotation precedent).

### D4 — UI update path: no live mirror; project name joins the close-time trigger

`IsPairingKey` (`MainWindow.Flyouts.cs`) gains the project case: `name` and `minimumaltitude` (the
latter because its commit recomposes the name). Deliberately **no live mirror** — project name is
grouping identity and the mosaic parent's match key (`add-target-rename` D2 precedent, extended). The
dialog stays open showing committed field values; closing runs the no-pull re-reconcile; grouping
headers and the Visible-Tonight dropdown pick up the composed name from the reload.

### D5 — Display: composed everywhere, zero display-layer changes expected

Grid grouping headers and the VT dropdown already render the stored TS name — which is now always the
composed form. No decompose-for-display anywhere except the dialog's base-name field. Verify, don't
build.

### D6 — Supersession bookkeeping

The ROADMAP "Queued — project-name clause parses back into min altitude" unit is marked superseded by
this change (the parse-back's entire hazard analysis — decorative-clause false positives — is mooted by
composition). `RenameForAltitude`'s never-invent contract in `visible-tonight-toggle` is MODIFIED, not
removed: the requirement text inverts to always-compose.

## Risks / Trade-offs

- **[Inbound raw names]** BIRDWATCHER-side creates/renames arrive clause-less or stale → detect-only
  tripwire (D3); remedy is one gesture. Accepted: no auto-repair, per the resolver-rejection doctrine.
- **[Base containing `" - N"`]** A user-typed base like `Veil - 3` composes to `Veil - 3 - 30` and
  round-trips correctly (strip removes only the final clause), but the *tripwire read* of
  `Veil - 3` alone (inbound, clause-less-intent) would read altitude 3 and flag disagreement — a
  correct flag under the decree, surfaced for the user to resolve by renaming. Accepted by definition.
- **[Two-repo sequencing]** AL grammar must land before the app consumes it → ordinary
  ProjectReference build ordering locally; the AL-first release gate handles distribution.
- **[Matcher tightening changes match behavior]** Only for names that matched *via* the loose false
  strip — measured: none exist (no hyphen-digit project names). If one ever appears inbound, it now
  simply matches literally instead of via accidental symmetric stripping — strictly more correct.

## Migration Plan

None — measured 2026-08-16, all 10 projects conform, zero disagreements. Ship order: AL grammar first
(tests), then app-side consumers, then the ROADMAP supersession note. Rollback = revert; no persisted
state changes shape.

## Open Questions

None.
