using System.Globalization;
using Astronomy.Catalog.Build;

namespace TargetCatalogManager.Cli;

/// <summary>
/// Immutable parsed command line, shared by every verb: <c>--key value</c> pairs over the
/// <see cref="DevDefaults"/>. A flag with no value (e.g. <c>--apply</c>, or a key followed by another
/// <c>--flag</c>) maps to <c>""</c> so it can't swallow the next option's value; duplicate keys are
/// last-wins. Unknown keys and stray positional tokens warn instead of silently vanishing into defaults —
/// this tool's verbs write to a database, so a typo'd <c>--tolerence</c> must not pass unremarked.
/// </summary>
internal sealed record CliOptions(
    string Catalog,
    string Library,
    string TsDb,
    string? Target,
    bool Apply,
    ResolveOptions Resolve)
{
    private static readonly string[] KnownKeys = ["catalog", "library", "ts", "tolerance", "target", "apply"];

    /// <summary>Parses option tokens (the caller has already consumed any leading verb).</summary>
    public static CliOptions Parse(string[] args)
    {
        Dictionary<string, string> opts = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"warning: ignoring stray argument '{args[i]}'");
                continue;
            }
            string key = args[i][2..];
            if (!KnownKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                // Warn AND ignore — the typo'd pair (key + its value, if any) must not influence the run.
                Console.Error.WriteLine($"warning: unknown option '--{key}' (known: {string.Join(", ", KnownKeys.Select(k => "--" + k))})");
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) i++;
                continue;
            }
            opts[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "";
        }

        ResolveOptions resolve =
            opts.TryGetValue("tolerance", out string? tol)
                && double.TryParse(tol, NumberStyles.Float, CultureInfo.InvariantCulture, out double deg)
                ? new ResolveOptions(deg)
                : ResolveOptions.Default;

        // --apply is a bare flag on a db-writing verb: any value (e.g. "--apply false") stays DRY-RUN and
        // warns, so a misremembered syntax can never arm a write the user meant to suppress.
        bool apply = false;
        if (opts.TryGetValue("apply", out string? applyValue))
        {
            apply = applyValue.Length == 0;
            if (!apply)
                Console.Error.WriteLine(
                    $"warning: --apply takes no value; ignoring '--apply {applyValue}' (dry-run). Pass a bare --apply to commit.");
        }

        return new CliOptions(
            Catalog: opts.GetValueOrDefault("catalog", DevDefaults.Catalog),
            Library: opts.GetValueOrDefault("library", DevDefaults.Library),
            TsDb: opts.GetValueOrDefault("ts", DevDefaults.TsDatabase),
            Target: opts.TryGetValue("target", out string? tv) && !string.IsNullOrWhiteSpace(tv) ? tv : null,
            Apply: apply,
            resolve);
    }
}
