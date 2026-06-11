using TargetCatalogManager.Cli;
using Xunit;

namespace TargetCatalogManager.Cli.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_NoArgs_AppliesDevDefaults()
    {
        CliOptions o = ParseQuiet([]);

        Assert.Equal(DevDefaults.Catalog, o.Catalog);
        Assert.Equal(DevDefaults.Library, o.Library);
        Assert.Equal(DevDefaults.TsDatabase, o.TsDb);
        Assert.Null(o.Target);
        Assert.False(o.Apply);
        Assert.Equal(0.5, o.Resolve.MatchToleranceDegrees);
    }

    [Fact]
    public void Parse_PathOverrides_Apply()
    {
        CliOptions o = ParseQuiet(["--catalog", @"C:\c.db", "--library", @"C:\lib", "--ts", @"C:\ts.sqlite"]);

        Assert.Equal(@"C:\c.db", o.Catalog);
        Assert.Equal(@"C:\lib", o.Library);
        Assert.Equal(@"C:\ts.sqlite", o.TsDb);
    }

    [Fact]
    public void Parse_Tolerance_InvariantParse()
    {
        Assert.Equal(0.7, ParseQuiet(["--tolerance", "0.7"]).Resolve.MatchToleranceDegrees);
    }

    [Fact]
    public void Parse_ToleranceUnparsable_FallsBackToDefault()
    {
        Assert.Equal(0.5, ParseQuiet(["--tolerance", "abc"]).Resolve.MatchToleranceDegrees);
    }

    [Fact]
    public void Parse_DuplicateKeys_LastWins()
    {
        Assert.Equal(@"C:\two", ParseQuiet(["--library", @"C:\one", "--library", @"C:\two"]).Library);
    }

    [Fact]
    public void Parse_ApplyFlag_DoesNotSwallowFollowingOption()
    {
        // --apply carries no value; the next token starts with "--" so it must stay an option.
        CliOptions o = ParseQuiet(["--apply", "--target", "NGC 6888 - Crescent"]);

        Assert.True(o.Apply);
        Assert.Equal("NGC 6888 - Crescent", o.Target);
    }

    [Fact]
    public void Parse_TargetApplyEitherOrder()
    {
        CliOptions o = ParseQuiet(["--target", "Mosaic - Cygnus Loop", "--apply"]);

        Assert.True(o.Apply);
        Assert.Equal("Mosaic - Cygnus Loop", o.Target);
    }

    [Fact]
    public void Parse_WhitespaceTarget_IsNull()
    {
        Assert.Null(ParseQuiet(["--target", "  "]).Target);
    }

    [Fact]
    public void Parse_UnknownOption_WarnsButStillParses()
    {
        (CliOptions o, string err) = ParseCapturing(["--tolerence", "0.7"]);

        Assert.Contains("unknown option '--tolerence'", err);
        Assert.Equal(0.5, o.Resolve.MatchToleranceDegrees);   // typo'd key never reaches tolerance
    }

    [Fact]
    public void Parse_StrayPositional_Warns()
    {
        (_, string err) = ParseCapturing(["oops"]);

        Assert.Contains("ignoring stray argument 'oops'", err);
    }

    [Fact]
    public void Parse_KnownOptions_NoWarnings()
    {
        (_, string err) = ParseCapturing(["--catalog", @"C:\c.db", "--apply"]);

        Assert.Equal("", err);
    }

    // ---- helpers ------------------------------------------------------------

    private static CliOptions ParseQuiet(string[] args) => ParseCapturing(args).Options;

    /// <summary>Parse with Console.Error captured (warnings are part of the contract under test).</summary>
    private static (CliOptions Options, string Error) ParseCapturing(string[] args)
    {
        TextWriter original = Console.Error;
        using StringWriter capture = new();
        Console.SetError(capture);
        try
        {
            CliOptions o = CliOptions.Parse(args);
            return (o, capture.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
