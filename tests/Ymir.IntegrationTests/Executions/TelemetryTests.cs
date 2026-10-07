using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Ymir.IntegrationTests.Api;
using Ymir.VibeMaker.Application.Executions;
using Ymir.VibeMaker.Contracts.Executions;

namespace Ymir.IntegrationTests.Executions;

/// <summary>SA §18 的監控指標。</summary>
public class TelemetryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Execution_RecordsStartFinishAndDuration()
    {
        var telemetry = factory.Services.GetRequiredService<VibeMakerTelemetry>();
        var measurements = new ConcurrentBag<(string Name, double Value, string? Status)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            // 平行執行的其他測試也有同名 meter；斷言只檢查「包含」，不受影響
            if (instrument.Meter.Name == VibeMakerTelemetry.Name)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? status = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "status")
                {
                    status = tag.Value as string;
                }
            }

            measurements.Add((instrument.Name, value, status));
        }

        listener.SetMeasurementEventCallback<long>((i, v, t, _) => Record(i, v, t));
        listener.SetMeasurementEventCallback<double>((i, v, t, _) => Record(i, v, t));
        listener.Start();

        using var client = await factory.LoginAsync($"telemetry-{Guid.NewGuid():N}");
        var conversation = await client.CreateConversationAsync(null, "telemetry");
        var (_, sent) = await client.SendMessageAsync(conversation.Id, "hello");
        var events = await client.ReadEventsAsync(sent!.EventStreamUrl);
        Assert.Equal(ExecutionEventNames.ExecutionCompleted, events[^1].EventType);

        listener.RecordObservableInstruments();
        Assert.Contains(measurements, m => m.Name == "ymir.executions.started");
        Assert.Contains(measurements, m => m.Name == "ymir.executions.finished" && m.Status == "Completed");
        Assert.Contains(measurements, m => m.Name == "ymir.execution.duration" && m.Value > 0);
        Assert.Contains(measurements, m => m.Name == "ymir.runtimes.busy");
        Assert.Equal(0, telemetry.BusyRuntimes); // 結束後不再算 busy
    }
}
