using Microsoft.Extensions.Logging;

namespace Ymir.IntegrationTests;

/// <summary>把 log 寫到 xUnit 的測試輸出，失敗時可以看到 harness 的診斷訊息。</summary>
internal sealed class TestOutputLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var line = $"[{logLevel}] {typeof(T).Name}: {formatter(state, exception)}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        TestContext.Current.TestOutputHelper?.WriteLine(line);
    }
}
