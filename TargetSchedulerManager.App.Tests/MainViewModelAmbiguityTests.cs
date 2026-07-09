using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

/// <summary>The VM's tripwire surface: count + button gate follow the load, the status suffix speaks only
/// when non-zero, and the report file lands where asked (launch suppressed in tests).</summary>
public class MainViewModelAmbiguityTests
{
    [Fact]
    public void FreshVm_NoLoad_ZeroDisabledAndNoFile()
    {
        MainViewModel vm = new(Gate());

        Assert.Equal(0, vm.AmbiguityCount);
        Assert.False(vm.CanShowAmbiguities);
        Assert.Equal("", vm.AmbiguitySuffix);
        Assert.Null(vm.WriteAmbiguityReport(open: false));
    }

    [Fact]
    public void Load_WithPlannedTwins_CountSuffixAndGate()
    {
        MainViewModel vm = Vm(Twins());

        Assert.Equal(1, vm.AmbiguityCount);                        // one item covers the pair
        Assert.True(vm.CanShowAmbiguities);
        Assert.Equal("  ·  1 ambiguity", vm.AmbiguitySuffix);      // singular form
    }

    [Fact]
    public void Load_Clean_SuffixStaysSilent()
    {
        MainViewModel vm = Vm(new CatalogGraph([], [], [], [], [], []));

        Assert.Equal(0, vm.AmbiguityCount);
        Assert.True(vm.CanShowAmbiguities);                        // report still available (affirmative zero)
        Assert.Equal("", vm.AmbiguitySuffix);
    }

    [Fact]
    public void WriteReport_ToDirectory_FileCarriesHeaderAndItems()
    {
        MainViewModel vm = Vm(Twins());
        string dir = Path.Combine(Path.GetTempPath(), "tsm-tests", Path.GetRandomFileName());

        string? path = vm.WriteAmbiguityReport(dir, open: false);

        Assert.NotNull(path);
        string text = File.ReadAllText(path!);
        Assert.Contains("# TS / disk ambiguity report", text);
        Assert.Contains("**1 action item(s)**", text);
        Assert.Contains("planned-only TS targets share this name", text);
        Directory.Delete(dir, recursive: true);
    }

    // ---- builders --------------------------------------------------------------------------------------------

    private static MainViewModel Vm(CatalogGraph graph)
    {
        MainViewModel vm = new(Gate());
        vm.SetLoadForTest(new LoadResult([], Report(), graph, TimeSpan.Zero));
        return vm;
    }

    private static CatalogGraph Twins() => new([], [], [], [
        Twin("10"), Twin("11"),
    ], [], []);

    private static Target Twin(string tsKey) =>
        new(Guid.NewGuid(), TargetSource.Planned, ProjectId: null, "M31", Enabled: true,
            RaHours: 0.712, DecDegreesSigned: 41.27, Epoch.J2000, RotationDeg: null, RoiPercent: null,
            Priority: null, DirectoryName: null, Catalog: null, CommonName: null, ObjectName: null,
            ScannedAt: null, CreatedAt: 0, ImportedFromTsGuid: tsKey);

    private static TsEditGate Gate() => new(
        SyncTestEnv.NewSync(out _), _ => throw new InvalidOperationException("no editor in ambiguity tests"));

    private static CatalogBuildReport Report() => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        AliasTsTargets: [], UnanchoredTsTargets: [], InvalidTsTargets: []);
}
