using Astronomy.Catalog.TargetScheduler;
using Astronomy.Catalog.Build;
using Astronomy.Catalog.Schema;
using TargetSchedulerManager.App.Services;
using TargetSchedulerManager.App.Shared;
using TargetSchedulerManager.App.ViewModels;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

// The template surface over an injected graph: the picker list (order, used-by counts, zero-use inclusion)
// and the plan→template resolution behind the row menu item. No SQLite, no scan.
public class MainViewModelTemplateTests
{
    [Fact]
    public void ListTemplates_NameOrdered_CountsUsedBy_IncludesZeroUse()
    {
        Guid ha = Guid.NewGuid(), o3 = Guid.NewGuid(), spare = Guid.NewGuid(), target = Guid.NewGuid();
        MainViewModel vm = Vm(
            [Tpl(o3, "O3 300", "O3", "12"), Tpl(ha, "Ha 300", "Ha", "11"), Tpl(spare, "Zz spare", "L", "13")],
            [Plan(target, ha, "p1"), Plan(target, ha, "p2"), Plan(target, o3, "p3")]);

        IReadOnlyList<TemplateInfo> templates = vm.ListTemplates();
        Assert.Equal(["Ha 300", "O3 300", "Zz spare"], templates.Select(t => t.Name).ToArray());
        Assert.Equal(2, templates[0].UsedByPlans);
        Assert.Equal(1, templates[1].UsedByPlans);
        Assert.Equal(0, templates[2].UsedByPlans);              // zero-use stays reachable — the picker's point
        Assert.Equal("11", templates[0].TsKey);
    }

    [Fact]
    public void ListTemplates_NoLoad_ReturnsEmpty()
    {
        Assert.Empty(new MainViewModel(Gate()).ListTemplates());
    }

    [Fact]
    public void ListTemplates_KeylessTemplate_SkippedLoudly()
    {
        Guid keyless = Guid.NewGuid();
        MainViewModel vm = Vm([Tpl(keyless, "Broken", "L", tsKey: null)], []);
        Assert.Empty(vm.ListTemplates());                       // can't edit without a TS key — omitted, logged
    }

    [Fact]
    public void TryGetTemplateForPlan_ResolvesThroughTheGraph()
    {
        Guid ha = Guid.NewGuid(), target = Guid.NewGuid();
        MainViewModel vm = Vm(
            [Tpl(ha, "Ha 300", "Ha", "11")],
            [Plan(target, ha, "plan-1"), Plan(target, ha, "plan-2")]);

        TemplateInfo? template = vm.TryGetTemplateForPlan("plan-1");
        Assert.NotNull(template);
        Assert.Equal("Ha 300", template!.Name);
        Assert.Equal("11", template.TsKey);
        Assert.Equal(2, template.UsedByPlans);                  // the blast radius the title states
    }

    [Fact]
    public void TryGetTemplateForPlan_UnknownPlanOrNoLoad_ReturnsNull()
    {
        Guid ha = Guid.NewGuid(), target = Guid.NewGuid();
        MainViewModel vm = Vm([Tpl(ha, "Ha 300", "Ha", "11")], [Plan(target, ha, "plan-1")]);
        Assert.Null(vm.TryGetTemplateForPlan("no-such-plan"));
        Assert.Null(new MainViewModel(Gate()).TryGetTemplateForPlan("plan-1"));
    }

    // ---- builders -----------------------------------------------------------

    private static MainViewModel Vm(IReadOnlyList<ExposureTemplate> templates, IReadOnlyList<ExposurePlan> plans)
    {
        MainViewModel vm = new(Gate());
        vm.SetLoadForTest(new LoadResult(
            [], Report(), new CatalogGraph([], [], templates, [], plans, []), TsPlanData.Empty, TimeSpan.Zero,
            new Dictionary<string, string>()));
        return vm;
    }

    private static TsEditGate Gate() => new(
        SyncTestEnv.NewSync(out _), _ => throw new InvalidOperationException("no editor in template tests"));

    private static ExposureTemplate Tpl(Guid id, string name, string filter, string? tsKey) =>
        new(id, Guid.NewGuid(), name, filter, Gain: null, OffsetAdu: null, Binning: null, ReadoutMode: null,
            DefaultExposureSeconds: 300.0, ImportedFromTsGuid: tsKey);

    private static ExposurePlan Plan(Guid target, Guid template, string tsKey) =>
        new(Guid.NewGuid(), target, template, ExposureSeconds: null, DesiredCount: 10, AcquiredCount: 0,
            AcceptedCount: 0, Enabled: true, ImportedFromTsGuid: tsKey);

    private static CatalogBuildReport Report() => new(
        DiskTargetCount: 0, TsTargetCount: 0, BothCount: 0, PlannedOnlyCount: 0, ActualOnlyCount: 0,
        NameMismatches: [], AmbiguousMatches: [], DuplicateTsTargets: [],
        UnanchoredTsTargets: [], InvalidTsTargets: []);
}
