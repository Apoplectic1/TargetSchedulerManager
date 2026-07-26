## Context

The Badges column (column 13, 150 px, rightmost) is the grid's triage surface: `mosaic · duplicate · name≠ ·
ambiguous · no-coords · no data · multi-plan · acc≠acq`. It is built in `ReconciliationLoader.BuildRows` as a
single `" · "`-joined string per row and bound as plain text in three row templates
(`MainWindow.xaml:80`/`157`/`203`), each hard-coding `Foreground="{ThemeResource SystemFillColorCautionBrush}"`.
Consequence: every token is amber, so the column cannot distinguish "go fix this" from "just so you know".

Three existing facts shape the design:

1. **The severity split already exists.** `IsFlagged` (`ReconciliationLoader.cs:175`) is exactly
   `duplicate | name≠ | ambiguous | multi-plan | acc≠acq`. The informative set is nearly its complement — the
   one disagreement being `no-coords`, resolved below.
2. **A single row mixes severities.** `mosaic` is target-scope while `multi-plan` is (filter,purpose)-scope, so
   `"mosaic · multi-plan"` is one real string. Per-token colour therefore cannot be a single `Foreground`.
3. **The joined string is load-bearing twice** — `ReconciliationRow.Matches()` (`:329`) searches it, and
   `RowAggregates.Compute` (`:45`) unions it for headers. Tokenising must not remove the flat text.

The grid is a virtualised `ListView` (`MainWindow.xaml:336`), so any cell built imperatively must survive
container recycling.

## Goals / Non-Goals

**Goals:**

- Per-token severity colour in the Badges column, for all three row kinds, with one authoritative
  classification.
- Keep the badge string (and therefore the search vocabulary) byte-identical.
- Keep the column's 150 px width and `TextTrimming="CharacterEllipsis"` behaviour.
- Make the colour and the flagged-only filter agree with each other.
- Centralise the badge token vocabulary, which is currently scattered string literals.

**Non-Goals:**

- No change to which states are *detected* — the badge set and its trigger conditions are untouched.
- No badge pills/borders/icons; this is a foreground-colour change, not a redesign of the cell.
- No library (`Astronomy.Catalog`) change, no schema change, no TS write-path change.
- No converter/MVVM-purism refactor of the other columns (a standing "not doing" for this project).

## Decisions

### Coloured `Run`s inside one `TextBlock`, driven by an attached property

**Chosen.** A new `Controls/BadgeRuns.cs` registers an attached `Tokens` DP; its change handler clears
`TextBlock.Inlines` and appends one `Run` per token with the severity brush, plus separator `Run`s at
informative severity. The three templates each swap `Text="{x:Bind Badge}"` + hard-coded `Foreground` for
`local:BadgeRuns.Tokens="{x:Bind Badge}"`.

*Why over an `ItemsRepeater`/`StackPanel` of `TextBlock`s:* a panel of children cannot ellipsis-trim, so a
long badge list in a 150 px column would clip mid-token or overflow. `TextBlock` trims across its inlines
natively, so the column geometry needs no thought at all.

*Why over row-dominant colour (one brush, `IsFlagged ? amber : quiet`):* six lines instead of fifty, but a
`mosaic · multi-plan` row drags `mosaic` back into amber — reintroducing the exact problem for the rows most
in need of triage.

*Why an attached property over code-behind in `MainWindow`:* the `GridColumns.ApplyRuler` precedent
(2026-07-24) already establishes attached properties as this project's way to reach into row templates, and it
keeps all three templates declarative and symmetric.

*Recycling:* the handler clears `Inlines` before rebuilding, so a recycled container cannot show a previous
row's tokens. When a recycled container receives an identical badge string the DP callback does not fire — and
does not need to, since the existing runs are already correct for that string.

### The informative tier uses a dimmed **brush**, not `Opacity`

The grid dims its quiet columns with `Opacity="0.7"` (Project, Purpose, Plans). That is unavailable here:
`Run : Inline : TextElement`, and **`TextElement` exposes no `Opacity`** — only `Foreground`, font properties,
and `CharacterSpacing`. Setting `Opacity` on the parent `TextBlock` instead would mute the amber runs too,
weakening the one thing that must stand out.

So informative tokens take `TextFillColorSecondaryBrush` — WinUI's standard secondary-text brush, theme-aware
in light and dark — added to `ThemeBrushes` as `Secondary`. Warning tokens take the existing
`ThemeBrushes.CautionText` (`SystemFillColorCautionBrush`), unchanged from today.

*Why not green for the informative tier:* green already means "the plan's committed time is met" in the Hours
and Seconds fills (`ThemeBrushes.Success`). Reusing it for "nothing to fix" would give one colour two
meanings grid-wide. Dimmed says "just a fact" without claiming "good" (author's call, this change).

### `no-coords` becomes genuinely flagged, not merely amber

`no-coords` marks a TS target whose `ra` or `dec` is null (`TargetResolver.cs:183-188`): TSM can never anchor
it to disk, and TS itself cannot schedule it. That is repairable authoring, so it belongs in the warning tier.

But colouring it amber while leaving `isFlagged: false` would make the flagged-only filter *hide* a row just
painted as a warning — colours and filter contradicting each other. So the classification moves with the
colour: `isUnanchored` joins the `flagged` expression (`:175`, the with-plans path) and replaces the literal
`false` in the no-cells fallback (`:265`). Both paths are reachable — an unanchored target with exposure plans
has cells; with none it takes the fallback row.

*Accepted consequence:* flagged counts and header flag rollups rise for a database carrying coordinate-less TS
targets. That is the point — those targets were previously invisible to triage.

`no data` (valid coordinates, no plans, no frames) stays informative and unflagged: queued work, not breakage.

### One vocabulary home: `Models/Badges.cs`

The eight token literals live inline in `ReconciliationLoader`; `" · "` is duplicated there and in
`RowAggregates`. A small internal static class takes ownership of the tokens as consts, the separator, the
severity predicate, and a `Split`/`Join` pair. `Split` returns `(Token, IsWarning)` pairs — a pure function,
so the classification and tokenising are unit-testable and `BadgeRuns` stays a dumb renderer with no logic to
test on a UI thread.

Splitting the string that the same class joined is safe because the separator has exactly one definition. This
also formalises what DOMAIN.md already notes: the badge tokens are a soft contract (the search vocabulary),
which deserves a single home rather than seven scattered literals.

### Header rollup dedupes tokens

`RowAggregates.cs:45` does `.Distinct()` on whole child badge *strings*. For a mosaic whose one filter is
multi-plan, children are `"mosaic"` and `"mosaic · multi-plan"` → the header renders
`mosaic · mosaic · multi-plan`. Since tokenising is already in hand, the rollup becomes
`Badges.Join(children.SelectMany(…Split…).Select(t => t.Token).Distinct())` — first-appearance order
preserved, and DOMAIN.md's "distinct union" description becomes accurate. In scope because the fix falls out
of the same primitive; leaving it would mean shipping a tokeniser next to a known token-level bug.

## Risks / Trade-offs

- **`TextFillColorSecondaryBrush` may read as "disabled" rather than "quiet"** → It is the theme's own
  secondary-text colour, so it degrades gracefully, but only the author's eye on the real grid can settle it.
  Swapping the one brush in `ThemeBrushes.Secondary` is a one-line change if it reads wrong.
- **Flagged counts rise, unannounced, for anyone with coordinate-less TS targets** → Intended, and stated in
  the proposal + spec; the CHANGELOG entry names it so a surprising count has a findable cause.
- **An imperatively-built cell in a virtualised list is a classic staleness trap** → Mitigated by clearing
  `Inlines` on every value change; the identical-string recycling case is correct by construction.
- **Colour becomes a second encoding of `IsFlagged`, so the two can drift** → Mitigated by deriving both from
  `Badges.IsWarning` intent rather than duplicating a token list: the loader sets `IsFlagged` from the same
  set the renderer colours, and a `BadgesTests` case asserts the two stay aligned.
- **Attached-property rendering is not unit-testable** (no XAML/UI thread in the test project) → All logic
  lives in the pure `Badges.Split`/`IsWarning`, which is tested; the visual result is explicitly the author's
  verification step, per this project's build-verifies-code / author-verifies-look-and-feel split.
