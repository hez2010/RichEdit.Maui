using Xunit;

namespace RichEdit.Maui.Tests;

public class RichPresentationBudgetTests
{
    [Fact]
    public void FixedNativeOverheadDoesNotBecomeAPerWidgetCost()
    {
        var cost = new RichPresentationCostEstimate();
        for (var pass = 0; pass < 40; pass++)
        {
            var count = pass % 2 == 0 ? 4 : 20;
            cost.Observe(count, 100 + count * 0.1);
        }
        Assert.InRange(cost.MillisecondsPerItem, 0.09, 0.11);
        var before = cost.MillisecondsPerItem;
        cost.Observe(0, 1000);
        cost.Observe(10, double.NaN);
        Assert.Equal(before, cost.MillisecondsPerItem);
    }
    [Fact]
    public void SlowIndivisibleWorkYieldsBeforeStartingAnotherOperation()
    {
        var elapsed = 0d;
        var budget = new RichPresentationBudget(() => elapsed);
        Assert.True(budget.TryStart());
        elapsed = 20;
        Assert.False(budget.TryStart());
        // Every fresh pulse can make progress even when a single operation
        // exceeds the target; the scheduler does not spin without doing work.
        Assert.True(new RichPresentationBudget(() => elapsed).TryStart(30));
    }

    [Fact]
    public void AdmissionAccountsForMeasuredNativeCostAndRemainingTime()
    {
        var elapsed = 1d;
        var budget = new RichPresentationBudget(() => elapsed);
        Assert.True(budget.TryStart(2));
        Assert.True(budget.TryStart(4));
        elapsed = 4;
        Assert.False(budget.TryStart(4));
        Assert.True(budget.TryStart(1));
        elapsed = 8;
        Assert.False(budget.TryStart());
    }
}
