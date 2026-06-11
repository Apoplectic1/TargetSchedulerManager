using TargetCatalogManager.Cli;

namespace TargetCatalogManager;

/// <summary>
/// Headless TCM entry point: parse + route only — the verbs live in <c>Cli\</c>
/// (<see cref="BuildCommand"/> rebuilds <c>Catalog.db</c> and reports; <see cref="WriteBackCommand"/>
/// pushes disk counts into the local TS copy). Usage:
/// <code>
/// tcm [--catalog PATH] [--library PATH] [--ts PATH] [--tolerance DEG]
/// tcm writeback [--target "&lt;dir&gt;"] [--apply] [same path options]
/// </code>
/// Duplicate options are last-wins; unknown options warn (see <see cref="CliOptions"/>).
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // The verb is positional; accept "writeback" or "--writeback" (dash-tolerant) so a stray leading dash
        // can't silently fall through to a full catalog build.
        if (args.Length > 0 && IsVerb(args[0], "writeback"))
            return await WriteBackCommand.RunAsync(args[1..]);

        // --target / --apply only mean anything under the writeback verb. If they appear without it, the user
        // almost certainly meant writeback — say so instead of silently rebuilding the whole catalog (the easy
        // mistake is `--writeback` without the `--` separator under `dotnet run`, so the verb never reaches
        // the app).
        if (args.Any(a => IsFlag(a, "target") || IsFlag(a, "apply")))
        {
            Console.Error.WriteLine("note: --target/--apply only apply to the 'writeback' verb. Did you mean:");
            Console.Error.WriteLine("  tcm writeback --target \"<dir>\"");
            Console.Error.WriteLine("  dotnet run --project TargetCatalogManager.csproj -- writeback --target \"<dir>\"");
            return 2;
        }

        return await BuildCommand.RunAsync(args);
    }

    /// <summary>Matches a positional verb, tolerating a leading <c>--</c> (so <c>writeback</c> and <c>--writeback</c> both route).</summary>
    private static bool IsVerb(string arg, string verb) =>
        arg.TrimStart('-').Equals(verb, StringComparison.OrdinalIgnoreCase);

    /// <summary>Matches a <c>--name</c> flag regardless of leading dashes or an <c>=value</c> suffix.</summary>
    private static bool IsFlag(string arg, string name) =>
        arg.TrimStart('-').Split('=', 2)[0].Equals(name, StringComparison.OrdinalIgnoreCase);
}
