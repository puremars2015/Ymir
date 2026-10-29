using Ymir.VibeMaker.Application.Schedules;
using Ymir.VibeMaker.Domain;

namespace Ymir.UnitTests.Schedules;

public sealed class ScheduleRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    [Fact] public void Weekdays_UseTheRequestedTimeZone() => Assert.Equal(Now.AddHours(1), ScheduleRules.Validate("0 9 * * 1-5", "Asia/Taipei", null, Now, 60));
    [Theory]
    [InlineData("* * * * *")]
    [InlineData("0,30 * * * *")]
    [InlineData("invalid")]
    [InlineData("0 0 30 2 *")]
    public void InvalidOrTooFrequentCron_IsRejected(string cron) => Assert.Throws<DomainValidationException>(() => ScheduleRules.Validate(cron, "Asia/Taipei", null, Now, 60));
    [Fact] public void PastReminder_IsRejected() => Assert.Throws<DomainValidationException>(() => ScheduleRules.Validate(null, "UTC", Now, Now, 60));
    [Fact] public void InvalidZone_IsRejected() => Assert.Throws<DomainValidationException>(() => ScheduleRules.Validate(null, "unknown", Now.AddHours(1), Now, 60));
    [Fact]
    public void OccurrenceIds_AreStableAndDistinct()
    {
        var id = Guid.NewGuid();
        Assert.Equal(ScheduledTaskProcessor.OccurrenceId(id, Now), ScheduledTaskProcessor.OccurrenceId(id, Now.ToOffset(TimeSpan.FromHours(8))));
        Assert.NotEqual(ScheduledTaskProcessor.OccurrenceId(id, Now), ScheduledTaskProcessor.OccurrenceId(id, Now.AddHours(1)));
    }
    [Fact]
    public void ThreeFailures_PauseAndResumeResetsThem()
    {
        var task = ScheduledTask.Create(Guid.NewGuid(), Guid.NewGuid(), ScheduleKind.AgentRun, "work", "model", null, "0 * * * *", "UTC", Now, Now);
        for (var i = 0; i < 3; i++) task.Finish(Now, Now.AddHours(1), "failure", true);
        Assert.Equal(ScheduleStatus.Paused, task.Status);
        task.Resume(Now.AddHours(2));
        Assert.Equal(0, task.ConsecutiveFailures);
        Assert.Equal(ScheduleStatus.Active, task.Status);
    }
}
