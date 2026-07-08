# Edit Direction Marks

## Why

After an edit commits, the grid shows the new value but not *that* it changed — the only trace is the sync
badge's count and the push-review dialog. And nothing at all shows what **BIRDWATCHER** changed since the
user last looked (the rig's TS updates counts every imaging night; someone can also edit plans rig-side).
The user wants per-row visibility of both directions of pending difference: "what will my push write" and
"what arrived changed at open" — including the ⇄ case where both are true on one row, which is today's
only warning that a push will overwrite a rig-side change.

## What Changes

- **New leftmost grid column** (3-character width, unlabeled, mark centered) on every row level — target
  header, mosaic panel header, filter row:
  - `←` (U+2190): inbound — BIRDWATCHER arrived different at this session's pull(s).
  - `→` (U+2192): outbound — unpushed journal writes (manual edits *and* automatic write-back stamps).
  - `⇄` (U+21C4): both on one row.
  - blank: no pending difference in either direction.
- **Pull-time inbound differ**: `TsSync.Pull` snapshots the displayed/editable fields of the local db
  before the backup overwrites it, diffs against the fresh copy, and unions the result into a
  session-sticky in-memory inbound set (with old→new values for tooltips). Covers all pull sites (open,
  Pull-now, discard-and-pull, the closing pull after push). No-pull sessions (offline / Continue-local)
  simply have no inbound marks.
- **Actuals override mask**: when write-back stamps a plan's `acquired`/`accepted` from disk, those fields
  are removed from that plan's inbound set — disk-derived actuals supersede the rig's, so the row reads
  `→`, not `⇄`, and goes clean (not stale-`←`) after push.
- **Rollup**: headers mark with the union of their subtree's directions (mosaic parent ⊇ panels ⊇ filter
  rows). Plan coverage for headers derives from the retained graph (targetId → TS plan keys), so a plan
  folded into a multi-plan rollup row still rolls its mark up. Project-scope entries mark the group header
  (the mosaic parent for mosaics) only — panels stay clean unless individually changed.
- **Tooltips** on the mark: filter rows list per-field `old → new` lines for each direction; headers show
  direction counts.
- **Lifecycle**: `→` appears in place on each committed edit (no grid rebuild, scroll preserved); a
  successful push clears applied `→` (retained failures keep theirs) and `⇄` collapses to `←` where
  unmasked inbound remains; Discard clears `→`; `←` is sticky for the session and resets at next open's
  pull.
- Disk-plane rows never mark (marks key on TS plan/target/project keys; target/project-level changes mark
  the header, not leaves). Exposure-template edits mark no row (accepted gap — templates have their own
  manager surface; they still ride the badge and push review).

## Capabilities

### New Capabilities

- `edit-direction-marks`: per-row sync-direction indicators — mark meanings and scope, the pull-time
  inbound differ and its session lifecycle, the actuals override mask, hierarchy rollup, tooltips, and
  live update/clear behavior.

### Modified Capabilities

None. `ts-sync-model`'s requirements (pull/skip rule, journaling, push replay) and `write-back`'s stamping
contract are unchanged — the differ is an additive observation inside the pull, and the mask is a
marks-capability behavior triggered by the existing stamp path.

## Impact

- **TSM app only; no library (`Astronomy.Catalog`) changes, no schema changes.**
- `Shared\TsSync.cs`: snapshot + diff + session inbound store; mask hook in `RecordWriteBack`.
- New app class(es): the inbound differ/store and a marks resolver (journal + inbound + graph → per-key
  direction and tooltip).
- `ViewModels\Rows\*`: mutable mark properties (`MarkGlyph`/`MarkTooltip`, PropertyChanged) on
  `ReconciliationRow`, `TargetGroupRow`, `PanelGroupRow`.
- `ViewModels\MainViewModel.cs`: mark refresh at load end, after each applied edit, after push, after
  discard.
- `MainWindow.xaml`: the new column definition inserted at position 0 in all four duplicated
  column-definition blocks (group / filter / panel templates + header row), every `Grid.Column` index
  bumped, mark `TextBlock` + tooltip added per row template.
- Tests: differ (temp SQLite dbs), mask, marks resolver, view-model lifecycle hooks.
- Docs: `ARCHITECTURE.md` (inbound differ + marks flow), `DOMAIN.md` (column meaning + glyph convention),
  `ROADMAP.md`.
