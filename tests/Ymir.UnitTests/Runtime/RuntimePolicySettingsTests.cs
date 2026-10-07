using Ymir.VibeMaker.Application.Runtime;

namespace Ymir.UnitTests.Runtime;

public class RuntimePolicySettingsTests
{
    [Theory]
    [InlineData(0, 1, 1, 0)]
    [InlineData(30, 30, 5, 0)]
    [InlineData(1440, 240, 50, 10_000)]
    public void Validate_AcceptsValuesWithinRange(int idle, int timeout, int pending, int daily) =>
        Assert.Null(new RuntimePolicySettings(idle, timeout, pending, daily).Validate());

    [Theory]
    [InlineData(-1, 30, 5, 0)]
    [InlineData(1441, 30, 5, 0)]
    [InlineData(30, 0, 5, 0)]
    [InlineData(30, 241, 5, 0)]
    [InlineData(30, 30, 0, 0)]
    [InlineData(30, 30, 51, 0)]
    [InlineData(30, 30, 5, -1)]
    [InlineData(30, 30, 5, 10_001)]
    public void Validate_RejectsValuesOutOfRange(int idle, int timeout, int pending, int daily) =>
        Assert.NotNull(new RuntimePolicySettings(idle, timeout, pending, daily).Validate());

    [Theory]
    [InlineData("0", true)]
    [InlineData("25.50", true)]
    [InlineData("100000", true)]
    [InlineData("-1", false)]
    [InlineData("100000.01", false)]
    [InlineData("1.234", false)]
    public void Validate_MonthlyBudget(string budget, bool valid) =>
        Assert.Equal(valid, new RuntimePolicySettings(30, 30, 5, 0, decimal.Parse(budget, System.Globalization.CultureInfo.InvariantCulture)).Validate() is null);

    [Fact]
    public void ToPolicy_ZeroBudgetMeansUnlimited()
    {
        Assert.Null(new RuntimePolicySettings(30, 30, 5, 0).ToPolicy().MonthlyBudget);
        Assert.Equal(12.5m, new RuntimePolicySettings(30, 30, 5, 0, 12.5m).ToPolicy().MonthlyBudget);
    }

    [Fact]
    public void ToPolicy_ConvertsMinutes()
    {
        var policy = new RuntimePolicySettings(45, 20, 3, 100).ToPolicy();

        Assert.Equal(TimeSpan.FromMinutes(45), policy.IdleTimeout);
        Assert.Equal(TimeSpan.FromMinutes(20), policy.ExecutionTimeout);
        Assert.Equal(3, policy.MaxPendingExecutionsPerUser);
        Assert.Equal(100, policy.DailyExecutionLimit);
    }

    [Theory]
    [InlineData("""{"idleTimeoutMinutes":45,"executionTimeoutMinutes":20,"maxPendingExecutionsPerUser":3,"dailyExecutionLimit":100}""", true)]
    [InlineData("""{"idleTimeoutMinutes":45}""", false)] // 缺欄位 → 逾時為 0，不合法
    [InlineData("""{"idleTimeoutMinutes":-5,"executionTimeoutMinutes":20,"maxPendingExecutionsPerUser":3,"dailyExecutionLimit":0}""", false)]
    [InlineData("not json", false)]
    public void Parse_IgnoresInvalidStoredValues(string json, bool valid) =>
        Assert.Equal(valid, RuntimePolicyService.Parse(json) is not null);
}
