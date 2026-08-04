# 2026-08-03 — Docs-architecture audit report

Multi-agent audit of the reference tier (CLAUDE.md router + ARCHITECTURE · SUBSYSTEMS · CONVENTIONS ·
DOMAIN · ROADMAP · TS-SCHEMA · VERIFICATION · README), two axes: **placement** (section vs charter) and
**currency** (claim vs live code). 60 workers (Sonnet 5 at high effort, rounds 1–4 loop-until-dry; Opus 5
diversification round), 107 raw flags → ~45 distinct issues in 10 adjudication groups. All doc-fix groups
were **approved and applied same day** (this commit); this report persists what R27 requires — the
report-only findings and the coverage note.

## Report-only findings (handed off, never auto-applied)

### RP-1 · revisit-plan — VERIFICATION.md "Warnings are build breaks" vs the xUnit1051 NoWarn

- **Location:** `VERIFICATION.md` → Tests, last sentence of the warnings-ratchet paragraph: *"In test
  code, pass `TestContext.Current.CancellationToken` to ct-accepting calls (xUnit1051)."*
- **Finding:** `TargetSchedulerManager.App.Tests.csproj` deliberately suppresses xUnit1051 project-wide
  with a written rationale (`<NoWarn>$(NoWarn);xUnit1051</NoWarn>` — "TsSync.Pull/Push accept one … but
  these are sub-second local-file ops — responsiveness-to-test-cancellation noise, not signal"). The doc
  sentence directs an implementer to plumb tokens the project decided against.
- **Evidence:** `TargetSchedulerManager.App.Tests/TargetSchedulerManager.App.Tests.csproj:19-22`.
- **Options when revisited:** (a) drop the sentence; (b) restate as the standing decision — "xUnit1051 is
  NoWarn'd in App.Tests (sub-second local-file ops); don't re-plumb tokens to satisfy it — AL's test bench
  is where the ratchet bit." Not applied in this pass because the plan/decision conflict is the user's
  call (R4/R9), not a doc-staleness fix.

## Coverage note (R23)

No gaps: 60/60 workers completed, 0 errors, 0 retries needed. The 19 empty results were dry-round workers
correctly returning zero new flags (rounds converged 18 → 14 → 13 → 14 → dry). The Opus diversification
round added 48 flags after Sonnet's ceiling — R21's model-switch requirement earned its keep.

## Applied same day (summary — detail in the commit)

- **G1** structural-verb absolutes updated for the shipped adoption verb (ARCHITECTURE, DOMAIN, ROADMAP).
- **G2** `remaining = Desired − Acquired` (acquired-basis, obs 01b7) corrected in ARCHITECTURE.
- **G3** DOMAIN alignment rules aligned to the 2026-07-29 all-columns-centered decision (incl. checklist).
- **G4** flyout → dialog terminology sweep across all reference docs (menus stay flyouts).
- **G5(b)** CLAUDE.md invariants block trimmed to names + one-line hooks; ARCHITECTURE → *Key facts* is
  the single source (the "edit both" double-maintenance rule retired; "coerced" drift healed).
- **G6** ROADMAP Status condensed to present-tense current state (history → CHANGELOG); DONE phases
  collapsed; `velopack-self-update` spec cite → `self-update`.
- **G7** rationale-free grep-derivable counts de-valued (field counts, RowNumbers tally, DOMAIN-split
  figures, README/dependency/toolbar/test enumerations).
- **G8** missing coverage added: adoption verify recipe, push-review Creates section, close-time
  re-reconcile, capture-config cell tuple, TS-SCHEMA insert-replay narrowed (templates assign-only),
  release pre-flight note; TS-SCHEMA key-space paragraph moved to SUBSYSTEMS (charter home).
- **G9** typos/precision (TP/IS/**ISM**, CanPush wording, NumberBox call site, T3 `description`).
