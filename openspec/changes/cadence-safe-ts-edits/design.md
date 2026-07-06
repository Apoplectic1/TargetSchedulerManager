# Design: cadence-safe-ts-edits

## Context

TS persists per-target planner rotation state in `filtercadenceitem` (and user-authored custom ordering in
`overrideexposureorderitem`), both keyed by `targetid` and both referencing exposure plans **by index**
(`referenceIdx`) into the target's enabled-plan list. `FilterCadenceFactory.Generate` restores existing
`filtercadenceitem` rows **verbatim** — it only regenerates when a target has none. Every TS code path that
changes a plan set clears these derived rows itself (`SchedulerDatabaseContext.ToggleExposurePlan`,
`SaveProject` on a `filterSwitchFrequency` change), so a TS db on disk is always internally consistent.

TSM's editor (`TargetSchedulerEditor` in `..\Library\Astronomy.Catalog`) does plain single-column UPDATEs
driven by the declarative `TsEditableSchema`. Today the two cadence-affecting fields are merely flagged
`CadenceSafe: false` and the app has no UI for them. A plain UPDATE of `exposureplan.enabled` would strand
stale index-bearing rows that survive NINA restarts and reboots — silent wrong-filter selection or
silently-skipped targets, precisely on in-progress targets (the ones TSM exists to edit).

Constraints: shared-library discipline (`Astronomy.Catalog` is consumer-neutral); no back-compat shims
(portfolio rule — breaking library changes assume consumers rebuild); fail-fast philosophy (never leave a
silently wrong state behind); TSM must not emulate TS's *scheduling* behavior, only keep the db it writes
internally consistent.

## Goals / Non-Goals

**Goals:**
- Editing a cadence-affecting field through the library leaves the TS db in a state TS itself could have
  produced: the UPDATE and the invalidation of derived rows are atomic.
- The behavior is declared in `TsEditableSchema` (data, not code): adding a future cadence-affecting field is
  one reference row; `project.filterswitchfrequency` becomes UI-only work.
- User-authored data (`overrideexposureorderitem`) is never deleted by TSM — edits that would require it are
  refused with a structured reason.
- TSM ships per-filter `enabled` editing on filter rows, gated by an explicit confirmation.

**Non-Goals:**
- No emulation of TS cadence *regeneration* (we rely only on "empty ⇒ TS regenerates", structural in
  `FilterCadenceFactory.Generate`).
- No OEO clearing, no structural edits (add/delete plans), no write access to `filtercadenceitem` beyond DELETE.
- No synchronization with a *running* TS session (`TargetEditGuard` is in-process and unreachable; the
  accepted remedy for runtime re-sync is restarting the NINA sequence). LIVE-mode risk is handled by dialog
  wording, not code.

## Decisions

### D1 — Clear scope is declarative metadata on `TsField` (replaces `CadenceSafe`)

`TsField.CadenceSafe : bool` becomes `Clears : TsCadenceClear` with values `None` (default), `Target`
(delete `filtercadenceitem` rows of the edited row's target), `Project` (delete them for every target of the
edited project). Mapping: `exposureplan.enabled` → `Target`; `project.filterswitchfrequency` → `Project`.

- *Why not compound editor methods (`SetPlanEnabled(...)`)?* The whole editing stack is reference-driven
  (schema → whitelist → generic `SetField` → `TsEditGate` → UI); a per-field method reintroduces the
  hard-coding the reference was built to remove, and every future consumer/field pays again.
- *Why replace the bool instead of adding alongside?* `CadenceSafe: false` without a scope is no longer a
  meaningful state; carrying both invites drift. Breaking change, no shim (portfolio rule).
- `IsCadenceBreaking(...)` helper survives as `Clears != None` so consumer warn-gating keeps one entry point.

### D2 — Invalidate-only, in one transaction

When `Clears != None` **and the value actually changed**, `UpdateField` wraps in a single SQLite transaction:
the one-column UPDATE plus `DELETE FROM filtercadenceitem WHERE targetid = ...` (scope `Target`: the plan's
`TargetId`, resolved by one SELECT; scope `Project`: `targetid IN (SELECT Id FROM target WHERE projectid = ...)`).
Read-back verification is unchanged.

- *Why invalidate-only rather than mirroring TS's full clear semantics?* Empty-cadence-regenerates is the
  weakest possible dependency on TS internals and is conservative-safe (worst case the rotation restarts).
  Mirroring (e.g. OEO wipes) would couple us to TS behavior details and delete user data.
- *Why atomic when TS itself clears outside its transaction?* Because we can, trivially, in raw SQLite — and a
  crash between UPDATE and DELETE is exactly the stale-rows state this change exists to prevent.
- *Why skip when unchanged?* Mirrors TS (`filterSwitchFrequency` setter only marks a breaking change on
  `!=`); a no-op edit must not cost a cadence reset. The editor already reads the old value; compare via the
  existing normalized-text equality, return `Applied`-equivalent success without writing.

### D3 — OEO rows refuse the edit (new `RefusalReason.HasOverrideOrder`)

For scope `Target` only: if the edited plan's target has `overrideexposureorderitem` rows, `TrySetField`
refuses (no write). OEO is hand-authored in TS and index-coupled to the plan set; deleting it is data loss and
re-authoring it is TS-editor business. Scope `Project` does **not** check OEO — TS's own fsf-change path
leaves OEO untouched.

- *Why refuse instead of warn-and-clear?* TSM's charter is managing values, not destroying TS-side authoring.
  A refusal is loud, reversible, and maps cleanly onto the existing `RefusalReason` → dialog machinery.
- The guarded checks run inside `TrySetField` in the existing order, new check last (schema → read-only →
  sidecar → column-present → OEO).

### D4 — TSM UI: checkbox on filter rows through the existing gate, confirm-first

Filter rows get an `enabled` checkbox (visual pattern of the shipped target-`active` checkbox). Because the
field is cadence-breaking, the click is intercepted **before** any write: a `ContentDialog` states that TS's
filter rotation for the target resets and, when the source is LIVE, adds that a target NINA is *currently
imaging* must be edited in TS instead (external writes can't trip TS's in-process `TargetEditGuard`; any
non-active target is safe). Cancel reverts the checkbox; confirm routes through `TsEditGate.ApplyAsync`
unchanged. A `HasOverrideOrder` refusal surfaces like other refusals, with wording pointing at the TS editor.
In-place row update on success (no reload, scroll preserved — the shipped `desired` pattern).

### D5 — Editing surface stays schema-first

The dialog's "will reset cadence" trigger is `TsEditableSchema.IsCadenceBreaking` (i.e. `Clears != None`),
not a hard-coded column list, so shipping `project.filterswitchfrequency` later inherits the same UI gate.

## Risks / Trade-offs

- **[TS changes its cadence model upstream]** → We depend only on "empty ⇒ regenerate" and two table/column
  names. Both are pinned by source references in `TsEditableSchema` doc comments; the editor's
  `PRAGMA table_info` reflection already detects table/column drift per-db (`IsFieldAvailable`), and the new
  DELETE targets tables whose absence must fail loud, not silently no-op.
- **[Editing the target NINA is actively imaging right now]** → Race is real and unfixable externally (planner
  re-persists its in-memory cadence after each exposure; `TargetEditGuard` unreachable). Mitigated by dialog
  wording (escalated on LIVE), accepted residual risk: at worst the same stale-rows state as today, for one
  target, until the next full replan.
- **[Deleting cadence rows for a project fans out]** → scope `Project` deletes rows for *all* its targets —
  intentional (matches TS), but the confirm dialog for fsf (future UI) must say "resets rotation for N
  targets".
- **[Refusal frustrates OEO users]** → Acceptable: the user doesn't use override orders today; the refusal
  message names the remedy (edit in TS).
- **[Library encodes TS *behavioral* knowledge]** → Already true of `TsEditableSchema` (cadence flags, enum
  codes); this stays in the same file with the same "authored from TS source" maintenance story.

## Open Questions

- None blocking. (Dialog wording finalized at implementation; `Verified` semantics for the skip-when-unchanged
  path defined in specs.)
