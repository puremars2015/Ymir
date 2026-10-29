using Cronos;
using Ymir.VibeMaker.Domain;

namespace Ymir.VibeMaker.Application.Schedules;

public static class ScheduleRules
{
    public static DateTimeOffset? Next(string cron, string zone, DateTimeOffset after) =>
        CronExpression.Parse(cron).GetNextOccurrence(after, TimeZoneInfo.FindSystemTimeZoneById(zone))?.ToUniversalTime();

    public static DateTimeOffset Validate(string? cron, string zone, DateTimeOffset? once, DateTimeOffset now, int minimumMinutes)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(zone) || zone.Length > 100 || (!TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out _) && zone != "UTC"))
                throw new DomainValidationException("請使用有效的 IANA 時區。");
            _ = TimeZoneInfo.FindSystemTimeZoneById(zone);
            if (cron is null)
            {
                if (once is null || once < now.AddMinutes(1)) throw new DomainValidationException("提醒時間必須至少在一分鐘之後。");
                return once.Value.ToUniversalTime();
            }
            if (cron.Length > 100) throw new DomainValidationException("Cron 表達式過長。");
            var expression = CronExpression.Parse(cron);
            var next = expression.GetNextOccurrence(now, TimeZoneInfo.FindSystemTimeZoneById(zone))?.ToUniversalTime()
                ?? throw new DomainValidationException("Cron 沒有下一次執行時間。");
            // 檢查完整年度，包含夏令時間；worker 也限制相鄰實際觸發的間隔。
            var cursor = next;
            for (var i = 0; i < 10000 && cursor < next.AddDays(370); i++)
            {
                var following = Next(cron, zone, cursor);
                if (following is null) break;
                if (following.Value - cursor < TimeSpan.FromMinutes(minimumMinutes))
                    throw new DomainValidationException($"排程間隔不可短於 {minimumMinutes} 分鐘。");
                cursor = following.Value;
            }
            return next;
        }
        catch (CronFormatException) { throw new DomainValidationException("Cron 格式不正確，請使用五欄格式。"); }
        catch (TimeZoneNotFoundException) { throw new DomainValidationException("找不到指定時區。"); }
    }
}
