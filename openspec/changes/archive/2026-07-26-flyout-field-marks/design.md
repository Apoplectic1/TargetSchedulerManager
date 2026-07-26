# flyout-field-marks — Design

## Context

The grid's column-0 marks (`SyncMarks`, resolved per sweep from the journal + inbound store) aggregate to
row level; the flyouts — where fields are actually edited — show no sync state. All four edit flyouts
render through one control, `TsFieldsEditor` (a two-column Grid: label | control, generated from
`TsEditableSchema`), constructed by `ShowEditFlyoutAsync` which closes the (table, key) into the commit
callback. `SyncMarks` already snapshots per-(table, key, column) facts; `ForPlan`/`ForKeys`/`ForTemplate`
merely aggregate them. Just shipped (`template-change-marks`): `MainViewModel.BuildMarks()` — a fresh
resolver on demand, used by the Templates… picker.

## Goals / Non-Goals

**Goals:**
- Every field row in the schema-generated flyouts shows its own `←`/`→`/`⇄` in a leading column, blank
  when clean, labels staying mutually aligned; tooltip = that field's old→new lines.
- Marks re-resolve after each in-flyout commit — live feedback (`→` appears as you edit).
- The custom mosaic-project flyout's two rows carry the same marks.

**Non-Goals:**
- No new glyph language, colors, or per-field conflict blocking — display only; push review remains the
  conflict adjudicator.
- No marks in right-click menus or other menu surfaces.
- No library changes; no `TsFieldsEditor` test harness (XAML runtime — visual verify, as today).

## Decisions

**D1 — `SyncMarks.ForField(table, tsKey, column)` is the resolver.** Looks up the (table, key) entry
lists both directions, filters to the one column, returns (glyph, tooltip) in the existing line grammar
(unattributed — the flyout *is* the entity, same reasoning as `ForTemplate`). Inbound `NewRowColumn`
entries are ignored here (a new-row fact is row-scoped, not field-scoped). ~15 lines over existing
`Get` lookups; fully unit-testable.

**D2 — `TsFieldsEditor` gains a third Grid column at index 0.** `Auto` width with `MinWidth` ~18 px so
blank marks still reserve the slot — every row shifts uniformly, labels stay aligned with each other. One
centered TextBlock per field row (secondary-ish foreground, same visual weight as the grid's column 0);
`ToolTipService` tooltip only when marked. The title row spans all three columns.

**D3 — `MarkResolver` delegate seam, mirroring `CommitField`/`EffectiveValue`.**
`public delegate (string Glyph, string? Tooltip) MarkResolver(string column);` — optional (null = no mark
column, e.g. hypothetical consumers without sync context). `ShowEditFlyoutAsync` passes
`column => ViewModel.BuildMarks().ForField(table, key, column)`… except rebuilt per refresh pass, not per
column — see D4. The editor never learns the TS key; the closure carries it, like commits.

**D4 — Refresh = one fresh resolver per pass, all rows re-resolved.** To keep the editor dumb *and*
avoid building a `SyncMarks` per column, the seam resolves a whole pass at once:
`MarkResolver(IReadOnlyList<string> columns) → IReadOnlyDictionary<string, (Glyph, Tooltip)>`. The flyout
side implements it as one `BuildMarks()` + N `ForField` calls. The editor keeps its mark TextBlocks in a
per-column map and exposes a private `RefreshMarks()` that invokes the resolver over all rendered columns
and applies the results — called (a) once at construction and (b) after every commit completes (in the
`CommitChain` continuation, success and failure alike — a failed commit can follow a succeeded one).
Cost: one graph-map build per commit, the same price the grid's `RefreshAllMarks` already pays per commit.

**D5 — Mosaic flyout hand-wiring.** Its two custom rows (master enable → the panels' `target.active`;
priority → `project.priority`) get the same leading mark slot. Master enable spans N panel targets — mark
is the union over the panels' target keys for column `active` (a per-panel `⇄`/`→` unions upward, matching
its fan-out write semantics); priority resolves `ForField(Project, key, "priority")`.

**D6 — Per-field `⇄` is the collision signal, and only that.** Both directions pending on the same
(table, key, column) → `⇄` with both lines in the tooltip. No blocking, no confirmation change: the
reviewed push already adjudicates. The flyout just makes the exact collision visible where the user is
about to type.

## Risks / Trade-offs

- [Marks reflect resolver facts, seeds reflect a direct db read — two sources on one row] → They can't
  disagree materially: the journal's last write *is* the local db value post-verify; inbound old values
  are display-only tooltip content.
- [Flyout width grows ~18 px] → All flyouts are `MinWidth 260` StackPanels; no layout risk.
- [Fresh `BuildMarks()` per commit] → Same cost as the grid sweep already paid after every commit
  (`RefreshAllMarks` builds one too); flyout adds at most one more build per commit.
- [Mosaic master-enable union across panels] → Slightly novel (a mark on a fan-out control); tooltip
  lists per-target lines so the union is explainable.

## Migration Plan

None — display only, no persisted state.

## Open Questions

None — scope settled with the user 2026-07-26 (assessment accepted as specced: leading column, all
schema-generated flyouts, live refresh, mosaic hand-wired, tooltips).
