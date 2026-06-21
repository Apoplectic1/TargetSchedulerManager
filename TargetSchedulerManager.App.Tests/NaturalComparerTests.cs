using TargetSchedulerManager.App.Shared;
using Xunit;

namespace TargetSchedulerManager.App.Tests;

public class NaturalComparerTests
{
    [Fact]
    public void OrdersEmbeddedNumbersByValue_NotLexically()
    {
        List<string> names =
            ["IC 1318", "IC 405", "IC 5146", "IC 410", "Abell 2218", "Abell 6", "Abell 78", "Abell 21"];
        names.Sort(NaturalComparer.Instance);
        string[] expected =
            ["Abell 6", "Abell 21", "Abell 78", "Abell 2218", "IC 405", "IC 410", "IC 1318", "IC 5146"];
        Assert.Equal(expected, names);
    }

    [Theory]
    [InlineData("M 8", "M 81", -1)]                 // 8 < 81 by value, not "8" vs "81" lexically
    [InlineData("M 81", "M 8", 1)]
    [InlineData("NGC 7000", "NGC 7000", 0)]
    [InlineData("ic 405", "IC 1318", -1)]           // case-insensitive prefix, then 405 < 1318
    [InlineData("Sh2-142", "Sh2-155", -1)]
    [InlineData("IC 405", "IC 0405", -1)]           // equal value -> fewer leading zeros first
    [InlineData("Abell 6", "Abell 6 & HFG 1", -1)]  // a prefix sorts before the longer string
    public void PairwiseSign(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(NaturalComparer.Instance.Compare(a, b)));
}
