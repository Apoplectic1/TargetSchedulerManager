# Tasks: enable-visible-tonight

> Rescoped 2026-07-23 (user redirect mid-apply): predicate is now the literal 0° geometric horizon with a
> 30-minute default duration via the existing `CoarseVisibility.IsAboveHorizonForAtLeast` — **no library
> or TP changes**. The original group 1 (`.hrz` loader promotion) was implemented, then reverted; both
> sibling repos are clean.

## 1. TSM — site input + Core reference

- [x] 1.1 Add `Astronomy.Core` ProjectReference to the TSM app (pure-managed; build model unchanged)
- [x] 1.2 Add site constants to `DevDefaults` (lat/long/TZ/elevation, values copied from TP's settings
      JSON) plus the 30-minute minimum-duration default, and a `Location` materialization helper
- [x] 1.3 Verify the night-of anchor convention of `NightCalculator` (how "tonight" resolves when invoked
      after midnight) and record it — a test if absent, a doc pointer if already covered

## 2. TSM — visibility pass engine

- [x] 2.1 Implement the pass (D1/D3): skip Draft/Closed; per-target verdict via
      `CoarseVisibility.IsAboveHorizonForAtLeast(target, site, tonight, ScalarHorizonProfile(0), 30 min)`;
      `active ← verdict`; then `state ← any-enabled-child ? Active : Inactive`; no-op edits skipped;
      panels evaluated as ordinary targets
- [x] 2.2 Unit tests against an in-memory TS working copy: each spec scenario (long window enables,
      sliver stays disabled, never-rises disabled, TS `minimumaltitude` ignored, re-enable, disable,
      no-op skip, project derive both directions, Draft/Closed untouched)

## 3. TSM — button + summary UI

- [x] 3.1 Add the "Visible tonight" toolbar button per DOMAIN.md's add-a-UI-element checklist; wire to
      the pass through the existing schema-driven edit path (journal, write-back, dirty badge)
- [x] 3.2 Completion summary (InfoBar-style): targets enabled / disabled / unchanged, projects flipped
- [x] 3.3 Grid refresh after the pass so flipped values and edit marks render immediately

## 4. Verify + docs (same commit as code)

- [x] 4.1 TSM builds green; full test suite passes (`dotnet build` / `dotnet test`)
- [x] 4.2 State the human-verification boundary: button press, flips, badge, summary, and Push review
      need an in-app check by the user (build + tests alone don't prove UI correctness)
- [x] 4.3 Update TSM docs: CLAUDE.md (recently shipped), ARCHITECTURE.md (button + pass + site input),
      ROADMAP.md; DOMAIN.md if the checklist gains an entry
