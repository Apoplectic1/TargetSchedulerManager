# Tasks — pairing-credited-write-back

## 1. Library: shared pairing predicate (design D2)

- [x] 1.1 Locate the reconciler's existing capture-config merge comparison and `AdoptionPlanner.MismatchReason`'s mirror of it; extract/lift ONE static pairing predicate in `Astronomy.Catalog` (disk cell expressed config vs template gain/offset/bin → pairs / mismatch reason). Semantics: both-express-and-differ separates; unexpressed disk dimension never separates; template `-1` sentinel never pairs.
- [x] 1.2 Re-base the reconciler's comparison onto the predicate (or add a test running identical cases through both and asserting agreement, if extraction is not clean).
- [x] 1.3 Predicate unit tests: equal configs pair; gain differs → no; offset differs → no; binning differs → no; sentinel gain −1 → never pairs even when disk matches the camera's actual value; disk-unexpressed dimension does not separate.

## 2. Library: WriteBackPlanner + SingleTargetPlanner credit by pairing (design D1)

- [x] 2.1 `WriteBackPlanner`: keep the 4-tuple grouping; within a bucket, credit an `InventoryFilter` row toward an auto group's sum only when the predicate pairs it with the group's plan template (composes with the existing `ServesPlanRotation` filter). Empty bucket after filtering stamps 0 via existing machinery.
- [x] 2.2 `SingleTargetPlanner`: apply the same predicate; a cell withheld for configuration surfaces a note naming the failing dimension (existing withheld-cell note shape), never dropped silently.
- [x] 2.3 Tests (field case first): gain-0 template plan + 18 gain-53 frames at same `(target,filter,purpose,seconds)` → stamps acquired=accepted=0; pairing config still credits; sentinel-gain template credits nothing; mixed buckets (some frames pair, some don't) sum only the pairing rows; `UnplannedFrames` notes still emitted for non-paired buckets on `Both` targets; desired ratchet-up unchanged.
- [x] 2.4 Library build + full test suite green (`dotnet build` pure-managed path; note `Astronomy.Core.Tests` vcxproj caveat does not apply to Catalog tests).

## 3. App: adoption seeds by pairing (design D3)

- [x] 3.1 `AdoptionPlanner.Build`: thread the accepted candidate's `WouldPair` into the plan payload — pairs → born-complete (desired=acquired=accepted=disk count), else 0/0/0; enabled either way.
- [x] 3.2 `BuildBulk`: same per cell — each accepted cell seeds by its own candidate's verdict.
- [x] 3.3 Tests: per-cell pairing → born-complete; per-cell cautioned → 0/0/0; bulk mixed outcomes seed per cell (spec scenario: 30/30/30 + 12/12/12 + 0/0/0 in one batch); dialog caution text and seeded counts derive from the same `WouldPair` fact.

## 4. App: sentinel badge (design D4)

- [x] 4.1 Projection: compute row-scoped `sentinel` warning badge on plan rows whose template has `gain`/`offset`/`readoutmode` = −1; ancestors roll it up under the existing row-scoped badge rule; register token + severity in the canonical badge map.
- [x] 4.2 Exemptions honored: plan `exposure` −1 and template `ditherevery` −1 raise nothing.
- [x] 4.3 Tests: sentinel template badges exactly its using rows + rollups (not siblings); explicit-value template raises nothing; exempt sentinels raise nothing; badge disappears after the template value is edited (re-reconcile path); badge text searchable/canonical like other tokens.

## 5. Verify, docs, ship

- [x] 5.1 Both repos build; full test suites green (Library Catalog + App).
- [x] 5.2 Update reference docs in the same commit: `SUBSYSTEMS.md` → TS write-back (credit key = pairing rule), `UI.md`/`DOMAIN.md` as touched (sentinel-is-error convention lands where the badge vocabulary lives), `CHANGELOG.md`.
- [ ] 5.3 Operational heads-up in the ship summary: first load stamps the ~245-bucket historical settling; first push review opens with hundreds of decreases — review once, push once.
- [ ] 5.4 Field verify (user): fresh load → Abell 78 adopted plan stamps 18→0 with journaled decrease; push review shows the settling; sentinel badge visible if any live template carries −1 (may be none today).
