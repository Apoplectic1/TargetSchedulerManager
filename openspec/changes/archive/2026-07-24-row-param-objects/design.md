# row-param-objects — design

## Context

`ReconciliationRow` (29 positional ctor params) is constructed at six mediated sites: two inline
`new ReconciliationRow(...)` in `ReconciliationLoader.BuildRows`/`EmitRows`, three local factories
(`BothRow`/`TsRow`/`DiskRow`), and the tests' single `Make.Leaf` funnel (`Make.Ts`/`Make.Disk` route
through it). No XAML or other code constructs rows.

## Goals / Non-Goals

**Goals:** kill the transposable same-typed runs; identity flows once per emit; public property surface
byte-for-byte identical (bindings + consumers untouched); zero behavior change.

**Non-Goals:** changing what any column shows; touching `AggregateHeaderRow`/`TargetGroupRow`/
`PanelGroupRow` (they aggregate from row properties, which don't change); the M4 view-model split.

## Decisions

### D1 — Two records, in `ReconciliationRow.cs`, public sealed

```csharp
public sealed record RowIdentity(
    string Target, string Project, RowSource Source,
    string? PanelKey, string? PanelLabel, RowSource? PanelSource,
    bool Enabled, string? TsTargetKey, Guid TargetId, string? ProjectTsKey);

public sealed record RowNumbers(
    int PlanSeconds, int DiskSeconds, int? Desired, int? Acquired, int? Accepted,
    int Disk, int PlanCount, double? PlanHours, double? DiskHours);
```

Grouping rationale: `RowIdentity` is exactly the set the three factories re-thread unchanged (the "who");
`RowNumbers` is the numeric column set (the "how much"), ordered as the grid shows them. What stays
scalar on the ctor is what genuinely varies per row within one emit: `filter, purpose, plane, badge,
isFlagged` + the defaulted tail (`secondsMixed, isDetail, detail, planTsKey, planEnabled`).

### D2 — Constructor: 12 parameters; properties re-point, nothing renames

```csharp
public sealed class ReconciliationRow(
    RowIdentity id, string filter, string purpose, RowPlane plane, RowNumbers numbers,
    string badge, bool isFlagged,
    bool secondsMixed = false, bool isDetail = false,
    IReadOnlyList<ReconciliationRow>? detail = null,
    string? planTsKey = null, bool? planEnabled = null) : INotifyPropertyChanged
```

Every property keeps its exact name/type and initializes from the records
(`public string Target { get; } = id.Target;` …). The records are consumed only in initializers, so the
primary-ctor params aren't captured — no hidden fields. The mutable-in-place members
(`PlanSeconds`, `Desired`, `PlanHours`, `PlanEnabled`) keep their private setters and `Apply*` methods
verbatim.

### D3 — `RowNumbers` is always constructed with named arguments

The transposition risk doesn't vanish, it moves into `RowNumbers` — so every construction site (loader
factories, inline sites, `Make.Leaf`) names each argument. This is a convention the sites establish, not
something the type can force; the review's own snippet built it positionally, which would have re-created
the original hazard one level down.

### D4 — Identity built once per `EmitRows`; factories shrink to per-cell content

`RowIdentity id = new(groupName, project, source, panelKey, panelLabel, panelSource, tc.Enabled,
tc.TsTargetKey, tc.TargetId, tc.ProjectTsKey);` at the top of `EmitRows`; the three factories and both
inline sites take `id` and stop re-threading eight arguments each. `Make.Leaf`'s keyword surface stays
identical (same parameter names/defaults), so zero test bodies change — only its `new(...)` body.

## Risks / Trade-offs

- [A transposed pair during the mechanical rewrite] → exactly the bug class this change exists to kill;
  `BuildRowsTests` pins loader output cell-by-cell and `RowTests` pins display text, so a transposition
  fails loudly in the suite.
- [`RowIdentity` reuse tempts future callers to share one instance across emits with different panels] →
  records are immutable; a wrong share is a wrong value caught by the same tests. Doc comment states the
  one-emit scope.

## Migration Plan

None. Pure refactor; clean rebuild. Auto-archives per the 2026-07-24 standing rule.

## Open Questions

None.
