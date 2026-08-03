# Tasks — adopt-disk-rows

## 1. Library: guarded insert primitive (`..\Library\Astronomy.Catalog`)

- [x] 1.1 Add the insert primitive beside `TrySetField` — full payload + guid in, existing guard order
      (schema / read-only / sidecar / column presence), single-transaction INSERT, read-back verify,
      structured refusals; caller-framed doc strings (no TSM terms)
- [x] 1.2 Plan insert: delete parent target's `filtercadenceitem` rows in the same transaction; OEO
      refusal (existing reason) when the parent target has `overrideexposureorderitem` rows; target
      insert clears nothing
- [x] 1.3 Library tests: guards precede insert, verified payload round-trip, cadence clear atomicity
      (failure applies neither), OEO refusal leaves everything untouched (cadence-safe delta scenarios)

## 2. App: journal insert kind + replay leg

- [x] 2.1 `TsEditKind.Insert` in `Shared/TsJournal`: payload + guid on the entry, jsonl round-trip,
      dirty/unpushed counting, excluded from net-no-op pruning and collapse-away (design D7)
- [x] 2.2 Replay leg in `Shared/TsSync`: inserts before both field legs, targets before plans, remote
      FK resolution by parent guid, fold-in of field entries addressing an unpushed insert (D2),
      per-entry failure retention + whole-db abort semantics preserved
- [x] 2.3 Closing-pull renumbering: implemented as **guid correlation in the differ** (D3 revised —
      stateless, survives a failed closing pull + restart, and also fixes the local/remote id-collision
      cross-diff); no push-time mask needed
- [x] 2.4 Push review: creates section (entity identity + key values) distinct from write-back summary
      and manual list
- [x] 2.5 Tests: journal round-trip/restart survival, fold-in replay (edit-before-push lands in the
      INSERT), ordering (target before plan), retained failed insert, renumbered-row correlation (same
      guid → no phantom ←; id collision → new row, never a cross-row diff), review names the creations

## 3. App: adoption planner + marks

- [x] 3.1 Adoption planner service: eligibility predicate from the retained graph/report (no plan at
      `(filter, purpose, seconds)`; split rows and disk-only mosaic parents ineligible), template
      auto-match per the merge rule with zero/≥2 refusal messages, value assembly (born-complete
      counts, exposure sentinel-vs-explicit, new-target payload per D6 — graph coords are already
      hours, sky-rotation seeding, rotation stays NULL otherwise)
- [x] 3.2 Wire adoption through the VM funnel + `TsEditGate` (busy exclusion refuses like any edit;
      `AdoptRowAsync` → `ApplyInsertAsync` → reload without pull re-reconciles the cell to `Both`)
- [x] 3.3 `Services/SyncMarks`: insert entries mark `→` via existing (table, key) matching; creation
      tooltip line ("new row (created here)", never the payload JSON); header rollup free via key match
- [x] 3.4 Tests: eligibility matrix (unplanned / split / mosaic-panel / TS-backed / stale-key),
      template match + refusals (gain, ambiguity, non-square bin, Stars purpose), payload assembly
      both cases, rotation seeding, gate key-space + refusal tests (in TsInsertSyncTests)

## 4. App: UI (menu + project-picker dialog)

- [x] 4.1 `Row_RightTapped`: "Add TS plan…" on eligible disk-only filter rows; "Add to TS…" variant
      when the target has no TS row; no item otherwise (flyouts delta); Add glyph, data-gated via
      `ViewModel.IsRowAdoptable`
- [x] 4.2 Project-picker/confirm dialog for new targets: existing non-mosaic projects only, name +
      centroid + rotation-seed facts, born-complete summary, cancel writes nothing
- [x] 4.3 Menu gating + dialog decision logic covered via planner/VM-surface tests; visual/interaction
      pass deferred to user verification (run the app)

## 5. Docs + verify

- [x] 5.1 Docs in the same commits as the code: `SUBSYSTEMS.md` (sync-model insert bullet, write-back
      carve-out amended, marks correlation bullet), `CLAUDE.md` router phrasing, `TS-SCHEMA.md`
      insert-replay note, `DOMAIN.md` extension-point note, `ROADMAP.md` status. No operational reset
      needed — old journals (Manual/WriteBack only) deserialize unchanged; the format only *adds* a kind
- [x] 5.2 Full build + all suites green: App 353 · Catalog 253 · Contracts 61; `openspec validate`
      clean. Needs human verification: menu items on real disk-only rows, the Add-to-TS dialog, adopted
      row reading `Both` + `→`, push-review creates section, and a real push round-trip
