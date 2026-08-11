using Fluxo.Core.Budgeting;
using Fluxo.Core.Enums;
using Xunit;

namespace Fluxo.Tests.Budgeting;

public sealed class BudgetAllocationBalancerTests
{
    [Fact]
    public void BudgetAllocationBalancer_Balance_WhenBucketIncreases_ReducesOtherBuckets()
    {
        var balancer = new BudgetAllocationBalancer();

        var result = balancer.Balance(50, 30, 20, BudgetAllocationSegment.Needs, 60);

        Assert.Equal((60, 25, 15), result);
    }

    [Fact]
    public void BudgetAllocationBalancer_Balance_WhenBucketDecreases_IncreasesOtherBuckets()
    {
        var balancer = new BudgetAllocationBalancer();

        var result = balancer.Balance(50, 30, 20, BudgetAllocationSegment.Needs, 40);

        Assert.Equal((40, 35, 25), result);
    }
}
