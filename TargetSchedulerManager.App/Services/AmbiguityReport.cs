using System.Globalization;
using System.Text;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Reconcile;
using Astronomy.Catalog.Scan;
using Astronomy.Catalog.Schema;
using Astronomy.Catalog.TargetScheduler;
using TargetSchedulerManager.App.Models;

namespace TargetSchedulerManager.App.Services;

/// <summary>What one report build produced: the Markdown text and how many items demand a hand-fix.
/// Informational entries (unplanned-frames notes) are not in <see cref="ActionCount"/>.</summary>
internal sealed record AmbiguityReportResult(string Markdown, int ActionCount);

/// <summary>
/// The printable ambiguity roll-up (decision 2026-07-08: TSM detects, the user repairs by hand in NINA's TS
/// UI — this report is the tripwire's detail). Pure over the already-loaded graph/report/write-back plan: no
/// I/O, no edits, no persistence. Sections group by WHERE the fix is made; each prints an explicit clean
/// marker so an empty report is affirmative. Items carry TS integer Ids where known — fix text must be
/// actionable at the rig without TSM present.
/// </summary>
internal static class AmbiguityReport
{
    public static AmbiguityReportResult Build(
        CatalogGraph graph,
        CatalogBuildReport report,
        WriteBackPlan plan,
        DateTimeOffset generatedAtLocal,
        string tsDbPath,
        string libraryRoot,
        double toleranceDegrees,
        IReadOnlyDictionary<string, string>? skippedFiles = null)
    {
        Dictionary<Guid, Target> targetById = graph.Targets.ToDictionary(t => t.Id);
        Dictionary<Guid, ExposureTemplate> templateById = graph.Templates.ToDictionary(t => t.Id);
        Dictionary<Guid, string> projectNameById = graph.Projects.ToDictionary(p => p.Id, p => p.Name);

        // The rig-side vocabulary: NINA's TS UI shows template names on plan rows (never plan Ids) — a plan's
        // human name is its template + its distinguishing counts.
        Dictionary<long, string> templateNameByTsPlanId = [];
        foreach (ExposurePlan p in graph.Plans)
        {
            if (long.TryParse(p.ImportedFromTsGuid, NumberStyles.Integer, CultureInfo.InvariantCulture, out long tsId)
                && templateById.TryGetValue(p.ExposureTemplateId, out ExposureTemplate? tpl))
                templateNameByTsPlanId[tsId] = tpl.Name;
        }
        string PlanLabel(long tsPlanId) => templateNameByTsPlanId.GetValueOrDefault(tsPlanId, "plan");
        // "project › target" is how the TS UI navigates; a target with no known project shows bare.
        string ProjectOf(Guid targetId) =>
            targetById.GetValueOrDefault(targetId)?.ProjectId is Guid pid
            && projectNameById.TryGetValue(pid, out string? name) ? $"{name} › " : "";

        // Identity-held write-back cells fold into their target's identity item (one target = one fix),
        // rather than printing one line per filter-cell.
        Dictionary<string, int> heldCellsByDirectory = new(StringComparer.OrdinalIgnoreCase);
        foreach (ManualGroup g in plan.Manual.Where(g => g.Reason == ManualReason.IdentityConflict))
        {
            string? dir = targetById.GetValueOrDefault(g.TargetId)?.DirectoryName;
            if (dir is null) continue;
            heldCellsByDirectory[dir] = heldCellsByDirectory.GetValueOrDefault(dir) + 1;
        }

        // One builder per section (review N9) — Build reads as the report's table of contents.
        List<string> identity = BuildIdentitySection(report, heldCellsByDirectory);
        List<string> duplicates = BuildDuplicateSection(report, graph, ProjectOf, toleranceDegrees);
        List<string> plans = BuildPlanSection(plan, graph, targetById, templateById, PlanLabel, ProjectOf);
        List<string> templates = BuildTemplateSection(graph, targetById);
        List<string> unreadable = BuildUnreadableSection(skippedFiles);
        List<string> info = BuildInfoSection(plan);
        info.AddRange(BuildFramingInfo(graph, report, ProjectOf));

        int actionCount = identity.Count + duplicates.Count + plans.Count + templates.Count + unreadable.Count;

        StringBuilder sb = new();
        sb.AppendLine($"# TS / disk ambiguity report — {generatedAtLocal.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine($"TS db: `{tsDbPath}`  ·  library: `{libraryRoot}`  ·  match tolerance {toleranceDegrees.ToString("0.0#", CultureInfo.InvariantCulture)}°");
        sb.AppendLine();
        sb.AppendLine("Conventions (DOMAIN.md): **one name per sky position**, spelled the same in TS and as the disk");
        sb.AppendLine("directory's catalog token; **one exposure plan per (filter, purpose, whole-second exposure) per");
        sb.AppendLine("target**. Every action item below is a slipped convention — fix by hand in NINA's TS UI.");
        sb.AppendLine();
        sb.AppendLine(actionCount == 0
            ? "**All checks clean — 0 action items.**"
            : $"**{actionCount} action item(s)**" + (info.Count > 0 ? $"  (+{info.Count} informational)" : ""));

        Section(sb, "Fix in TS — target identity (rename / coordinates)", identity);
        Section(sb, "Fix in TS — duplicates & twins", duplicates);
        Section(sb, "Fix in TS — exposure plans", plans);
        Section(sb, "Fix in TS — exposure templates", templates);
        Section(sb, "Fix on disk — unreadable files", unreadable);
        Section(sb, "Info (no action needed)", info, clean: "✓ nothing to note");

        sb.AppendLine();
        sb.AppendLine($"Write-back this load: {plan.Writes.Count} plan cell(s) auto-stampable · " +
                      $"{plan.Manual.Count} held · {plan.IgnoredMissing} one-sided target(s) ignored.");
        return new AmbiguityReportResult(sb.ToString(), actionCount);
    }

    // ---- section builders (review N9): one per report section, bodies verbatim from Build ------------------

    /// <summary>Fix-in-TS identity items: name mismatches (with held-cell notes and the panel-claim
    /// variant), ambiguous coordinate matches, unanchored and invalid TS targets.</summary>
    private static List<string> BuildIdentitySection(
        CatalogBuildReport report, Dictionary<string, int> heldCellsByDirectory)
    {
        List<string> identity = [];
        foreach (NameMismatch m in report.NameMismatches)
        {
            int held = heldCellsByDirectory.GetValueOrDefault(m.DiskDirectory);
            string heldNote = held > 0 ? $" {held} filter-cell(s) held un-stamped until fixed." : "";
            // A composite "mosaicDir/panelDir" path is a panel claim: the catalog-token rename is meaningless
            // there (it would name the "Mosaic - …" prefix) — describe the token disagreement instead.
            int slash = m.DiskDirectory.IndexOf('/');
            string fix = slash > 0
                ? $"→ TS panel `{m.TsName}` claimed disk panel `{m.DiskDirectory[(slash + 1)..]}` of " +
                  $"`{m.DiskDirectory[..slash]}` but the panel tokens disagree — confirm which panel this " +
                  $"really is, then align the TS panel name with the panel directory."
                : $"→ Rename TS target `{m.TsName}` → `{CatalogToken(m.DiskDirectory)}` (match the disk directory's catalog token).";
            identity.Add(
                $"**{m.TsName}** — coordinate match to disk `{m.DiskDirectory}` at " +
                $"{m.SeparationDegrees.ToString("0.000", CultureInfo.InvariantCulture)}°, but the name fails token validation.{heldNote}\n" +
                $"  {fix}");
        }
        foreach (AmbiguousMatch a in report.AmbiguousMatches)
        {
            identity.Add(
                $"**{a.TsName}** — {a.CandidateDirectories.Count} disk directories within tolerance " +
                $"[{string.Join(" | ", a.CandidateDirectories)}], nearest {a.NearestSeparationDegrees.ToString("0.000", CultureInfo.InvariantCulture)}°.\n" +
                $"  → Make names disambiguate: the TS name should exactly token-match ONE of the candidates.");
        }
        foreach (UnanchoredTsTarget u in report.UnanchoredTsTargets)
        {
            identity.Add(
                $"**{u.TsName}** — no usable coordinates; cannot anchor to disk (planned-only this load).\n" +
                $"  → Set the target's RA/Dec in TS.");
        }
        foreach (InvalidTsTarget i in report.InvalidTsTargets)
        {
            identity.Add(
                $"**{i.TsName}** — {i.Reason} (values coerced this load; the db row is unchanged).\n" +
                $"  → Correct the RA/Dec/epoch in TS.");
        }
        return identity;
    }

    /// <summary>Fix-in-TS duplicates: disk-claimed duplicate folds, then the planned-only twins the grid
    /// can't badge.</summary>
    private static List<string> BuildDuplicateSection(
        CatalogBuildReport report, CatalogGraph graph, Func<Guid, string> projectOf, double toleranceDegrees)
    {
        List<string> duplicates = [];
        foreach (DuplicateTsTarget d in report.DuplicateTsTargets)
        {
            duplicates.Add(
                $"disk `{d.DiskDirectory}` ← TS targets [{string.Join(" | ", d.TsTargetNames)}] — a duplicate fold.\n" +
                $"  → Consolidate in TS: keep one target, delete the rest. Check `desired` on each first (intent " +
                $"doesn't self-heal; acquired/accepted restamp from disk on the next load).");
        }
        duplicates.AddRange(PlannedOnlyTwins(graph, projectOf, toleranceDegrees));
        return duplicates;
    }

    /// <summary>Fix-in-TS exposure plans: held cells (multi-plan / duplicate-fold) with full per-plan
    /// detail, then the TS-internal same-key check over ALL TS-sourced targets — de-duped against the held
    /// cells (the manual item wins: it carries the disk count).</summary>
    private static List<string> BuildPlanSection(
        WriteBackPlan plan, CatalogGraph graph,
        Dictionary<Guid, Target> targetById, Dictionary<Guid, ExposureTemplate> templateById,
        Func<long, string> planLabel, Func<Guid, string> projectOf)
    {
        List<string> plans = [];
        HashSet<(Guid, string, FilterPurpose, int)> manualKeys = [];
        foreach (ManualGroup g in plan.Manual.Where(g => g.Reason is ManualReason.MultiPlan or ManualReason.DuplicateFold or ManualReason.NoMatchingPlan))
        {
            manualKeys.Add((g.TargetId, g.Filter.ToUpperInvariant(), g.Purpose, g.Seconds));
            // One indented row per plan — the shape the TS UI shows under a target.
            string detail = string.Concat(g.Plans.Select(p =>
                PlanRow(planLabel(p.TsExposurePlanId), p.Desired, p.CatalogAcquired, p.CatalogAccepted)));
            string fix = g.Reason switch
            {
                ManualReason.DuplicateFold =>
                    "→ Consolidate the folded TS targets (see duplicates section); the survivor's plan is stamped next load.",
                ManualReason.NoMatchingPlan =>
                    "→ Equipment-identity question: match binning/duration by hand before trusting the count.",
                _ =>
                    "→ Delete (or re-time) all but one — tell them apart by their desired/acquired counts in the " +
                    "TS UI; the survivor is auto-stamped on the next load.",
            };
            plans.Add(
                $"**{projectOf(g.TargetId)}{g.TargetName} · {Cell(g.Filter, g.Purpose, g.Seconds)}** — " +
                $"{g.Plans.Count} plans share one key; disk has {g.DiskCount} frame(s) at this key; counts are HELD (not auto-stamped)." +
                $"{detail}\n  {fix}");
        }
        plans.AddRange(SameKeyPlans(graph, targetById, templateById, projectOf, manualKeys));
        return plans;
    }

    /// <summary>Fix-in-TS templates: duplicate names within one profile (names are how plans read in the
    /// TS UI), then camera-default sentinels — the cause behind every grid `sentinel` badge (field obs
    /// b22d): the template, exactly which field(s) defer to the camera, and the plans riding on it. The
    /// exempt defer-to-explicit-value sentinels (plan exposure −1, ditherevery −1) never appear here.</summary>
    private static List<string> BuildTemplateSection(CatalogGraph graph, Dictionary<Guid, Target> targetById)
    {
        List<string> templates = [];
        foreach (var g in graph.Templates
                     .GroupBy(t => (t.ProfileId, Name: t.Name.Trim().ToUpperInvariant()))
                     .Where(g => g.Count() > 1))
        {
            templates.Add(
                $"**{g.First().Name.Trim()}** — {g.Count()} templates share this name in one profile " +
                $"[{string.Join(" | ", g.Select(t => $"Id {t.ImportedFromTsGuid ?? "?"} ({t.FilterName})"))}].\n" +
                $"  → Rename so every template's name is unique (names are how plans read in the TS UI).");
        }

        foreach (ExposureTemplate t in graph.Templates.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            List<string> fields = [];
            if (CaptureConfigPairing.PlanGain(t) == CaptureConfigPairing.Sentinel) fields.Add("gain");
            if (CaptureConfigPairing.PlanOffset(t) == CaptureConfigPairing.Sentinel) fields.Add("offset");
            if (t.ReadoutMode == CaptureConfigPairing.Sentinel) fields.Add("readout mode");
            if (fields.Count == 0) continue;

            List<ExposurePlan> plansUsing = [.. graph.Plans.Where(p => p.ExposureTemplateId == t.Id)];
            string[] targets = [.. plansUsing
                .Select(p => targetById.GetValueOrDefault(p.TargetId)?.Name)
                .Where(n => n is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)!];
            string use = plansUsing.Count == 0
                ? "no plans use it yet"
                : $"used by {plansUsing.Count} plan(s) on [{string.Join(" | ", targets)}] — each carries the `sentinel` badge and stamps 0";
            templates.Add(
                $"**{t.Name}** (Id {t.ImportedFromTsGuid ?? "?"}, {t.FilterName}) — camera-default sentinel on " +
                $"{string.Join(" + ", fields)}; {use}.\n" +
                $"  → Set an explicit {string.Join(" and ", fields)} on the template (TSM's template editor or " +
                $"NINA's TS UI); an unspecified value can never pair or credit.");
        }
        return templates;
    }

    /// <summary>Fix-on-disk unreadable files (openspec framing-overlap-column, 4a): every frame the scanner
    /// skipped as unparseable — missing/garbled XISF header, absent mandatory geometry. Action items,
    /// because each one silently lowers the Actual counts until repaired: nothing else in the app shows a
    /// wrong-total cause.</summary>
    private static List<string> BuildUnreadableSection(IReadOnlyDictionary<string, string>? skippedFiles)
    {
        List<string> unreadable = [];
        if (skippedFiles is null) return unreadable;
        foreach ((string path, string reason) in skippedFiles.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            unreadable.Add(
                $"`{path}` — {reason}\n" +
                $"  → Re-export or remove the file; every count this load excludes it.");
        }
        return unreadable;
    }

    /// <summary>Informational framing notes (openspec framing-overlap-column): the overlap facts that carry
    /// no grid badge. An off-plan POINTING — a framing at the plan's own angle whose displacement still
    /// leaves it below the on-footprint threshold — has no badge to decorate (rotation serves), so the
    /// report is where it speaks. A priced framing spanning two sensors gets its qualifier here for the
    /// same reason: the number describes the dominant sensor only.</summary>
    private static List<string> BuildFramingInfo(
        CatalogGraph graph, CatalogBuildReport report, Func<Guid, string> projectOf)
    {
        List<string> lines = [];
        foreach (TargetCells tc in ReconciliationProjection.Project(graph, report))
        {
            foreach (ReconciliationCell c in tc.Cells)
            {
                if (c.FramingOverlapFraction is not double f) continue;
                string pct = Math.Round(f * 100).ToString(CultureInfo.InvariantCulture);
                if (!c.FramingDisagrees)
                    lines.Add(
                        $"Off-plan pointing: **{projectOf(tc.TargetId)}{tc.Name} · {Cell(c.Filter, c.Purpose, c.Seconds)}** — " +
                        $"framing at the plan's own angle but pointed off its center; {pct}% of its footprint " +
                        $"lies where the plan points. (rotation serves — the grid shows no badge)");
                if (c.FramingSpansMultipleSensors)
                    lines.Add(
                        $"Mixed-sensor framing: **{projectOf(tc.TargetId)}{tc.Name} · {Cell(c.Filter, c.Purpose, c.Seconds)}** — " +
                        $"its {pct}% describes the dominant sensor of frames spanning two geometries.");
            }
        }
        return lines;
    }

    /// <summary>Informational (no action): unplanned frames grouped per target with one indented row per
    /// bucket — the TS-UI target→plans shape (frames at durations no plan targets; write-back never creates
    /// plans, so they're pure notes).</summary>
    private static List<string> BuildInfoSection(WriteBackPlan plan)
    {
        List<string> info = [];
        foreach (var g in plan.NeedsReconciliation
                     .Where(n => n.Kind == ReconcileNote.UnplannedFramesKind)
                     .GroupBy(n => n.TargetName)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> buckets = g.Select(n =>
            {
                int cut = n.Detail.IndexOf(" - no TS plan", StringComparison.Ordinal);
                return cut > 0 ? n.Detail[..cut] : n.Detail;
            });
            info.Add($"Unplanned frames: **{g.Key}**" +
                     string.Concat(buckets.Select(b => $"\n  - {b}")));
        }
        return info;
    }

    // ---- TS-internal checks (app-side reporting concern; promote to AL only if a second consumer wants them) --

    /// <summary>≥2 plans on one target sharing (filter, purpose, effective whole-second exposure) — over ALL
    /// TS-sourced targets, not just disk-matched ones (write-back's planner is scoped to Both and misses
    /// planned-only offenders). <paramref name="manualKeys"/> de-dupes against held cells already reported.
    /// No exemptions: every same-key multiplicity is an action item (the alias escape died with the fold
    /// mechanism).</summary>
    private static IEnumerable<string> SameKeyPlans(
        CatalogGraph graph,
        Dictionary<Guid, Target> targetById,
        Dictionary<Guid, ExposureTemplate> templateById,
        Func<Guid, string> projectOf,
        HashSet<(Guid, string, FilterPurpose, int)> manualKeys)
    {
        var groups = graph.Plans
            .Select(p => (Plan: p, Template: templateById.GetValueOrDefault(p.ExposureTemplateId)))
            .Where(x => x.Template is not null)
            .GroupBy(x => (
                x.Plan.TargetId,
                Filter: x.Template!.FilterName.ToUpperInvariant(),
                Purpose: FilterPurposeClassifier.Classify(x.Template.Name),
                Seconds: EffectiveExposure.Seconds(x.Plan, x.Template)))
            .Where(g => g.Count() > 1 && !manualKeys.Contains(g.Key));

        foreach (var g in groups.OrderBy(g => targetById.GetValueOrDefault(g.Key.TargetId)?.Name, StringComparer.OrdinalIgnoreCase))
        {
            Target? t = targetById.GetValueOrDefault(g.Key.TargetId);
            string detail = string.Concat(g.Select(x =>
                PlanRow(x.Template!.Name, x.Plan.DesiredCount, x.Plan.AcquiredCount, x.Plan.AcceptedCount)));
            string anchor = t?.Source == TargetSource.Planned ? " No disk anchor — TS-internal duplicate." : "";
            yield return
                $"**{(t is null ? "" : projectOf(t.Id))}{t?.Name ?? "?"} · {Cell(g.First().Template!.FilterName, g.Key.Purpose, g.Key.Seconds)}** — " +
                $"{g.Count()} plans share one key.{anchor}{detail}\n" +
                $"  → Delete (or re-time) all but one — tell them apart by their desired/acquired counts in the TS UI.";
        }
    }

    /// <summary>Planned-only twin targets: same normalized name, else a pair within the match tolerance.
    /// Invisible in the grid today — duplicate detection only fires when a disk unit is claimed twice.
    /// Twins are told apart by their project (how the TS UI navigates), never by raw ids.</summary>
    private static IEnumerable<string> PlannedOnlyTwins(
        CatalogGraph graph, Func<Guid, string> projectOf, double toleranceDegrees)
    {
        List<Target> planned = [.. graph.Targets.Where(t => t.Source == TargetSource.Planned && t.ParentTargetId is null)];

        HashSet<Guid> nameTwinned = [];
        foreach (var g in planned.GroupBy(t => Normalize(t.Name)).Where(g => g.Count() > 1))
        {
            foreach (Target t in g) nameTwinned.Add(t.Id);
            yield return
                $"**{g.First().Name}** — {g.Count()} planned-only TS targets share this name " +
                $"[in project(s): {string.Join(" | ", g.Select(t => projectOf(t.Id)))}] (no disk anchor; the grid shows them unbadged).\n" +
                $"  → Consolidate in TS: keep one, delete the rest (check `desired` first).";
        }

        for (int i = 0; i < planned.Count; i++)
        {
            for (int j = i + 1; j < planned.Count; j++)
            {
                Target a = planned[i], b = planned[j];
                if (nameTwinned.Contains(a.Id) && nameTwinned.Contains(b.Id)) continue;   // already reported by name
                if (a.RaHours is not double ra1 || a.DecDegreesSigned is not double d1
                    || b.RaHours is not double ra2 || b.DecDegreesSigned is not double d2) continue;
                double sep = SeparationDegrees(ra1, d1, ra2, d2);
                if (sep >= toleranceDegrees) continue;
                yield return
                    $"**{a.Name}** ({projectOf(a.Id)}) and **{b.Name}** ({projectOf(b.Id)}) — " +
                    $"planned-only targets {sep.ToString("0.000", CultureInfo.InvariantCulture)}° apart (inside tolerance); " +
                    $"they will contest the same disk directory once imaged.\n" +
                    $"  → If one object: consolidate — keep one target, delete the other (one TS row per position, " +
                    $"no exceptions). If two objects: they are closer than the match tolerance — confirm coordinates.";
            }
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static void Section(StringBuilder sb, string title, List<string> items, string clean = "✓ none")
    {
        sb.AppendLine();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (items.Count == 0) { sb.AppendLine(clean); return; }
        foreach (string item in items) sb.AppendLine($"- {item}");
    }

    /// <summary>One indented plan row under a target — the TS-UI target→plans shape (template-named, never
    /// plan Ids), with the counts that tell duplicate plans apart.</summary>
    private static string PlanRow(string label, int desired, int acquired, int accepted) =>
        $"\n  - {label} — desired {desired}, acq {acquired} / acc {accepted}";

    // The cell naming convention lives in Format (presentation-conventions); this alias keeps call sites short.
    private static string Cell(string filter, FilterPurpose purpose, int seconds) =>
        Format.Cell(filter, purpose, seconds);

    /// <summary>The catalog token of a disk directory name — the part before the first " - " separator
    /// ("IC 1795 - Fish Head" → "IC 1795"), the rename target for a mismatched TS name.</summary>
    private static string CatalogToken(string directoryName)
    {
        int i = directoryName.IndexOf(" - ", StringComparison.Ordinal);
        return i > 0 ? directoryName[..i] : directoryName;
    }

    private static string Normalize(string name) =>
        new([.. name.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    /// <summary>Great-circle separation in degrees (haversine; RA in decimal hours). Local copy — AL's is internal.</summary>
    private static double SeparationDegrees(double raHours1, double dec1, double raHours2, double dec2)
    {
        const double rad = Math.PI / 180.0;
        double ra1 = raHours1 * 15.0 * rad, ra2 = raHours2 * 15.0 * rad;
        double d1 = dec1 * rad, d2 = dec2 * rad;
        double h = Math.Sin((d2 - d1) / 2.0) * Math.Sin((d2 - d1) / 2.0)
                 + Math.Cos(d1) * Math.Cos(d2) * Math.Sin((ra2 - ra1) / 2.0) * Math.Sin((ra2 - ra1) / 2.0);
        return 2.0 * Math.Asin(Math.Min(1.0, Math.Sqrt(h))) / rad;
    }
}
