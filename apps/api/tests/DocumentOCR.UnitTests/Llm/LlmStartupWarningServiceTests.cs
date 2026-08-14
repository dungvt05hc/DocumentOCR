using DocumentOCR.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DocumentOCR.UnitTests.Llm;

public class LlmStartupWarningServiceTests
{
    [Fact]
    public async Task StartAsync_EnabledAndFreeTier_LogsFreeTierWarning()
    {
        var logger = new CapturingLogger<LlmStartupWarningService>();
        var sut = new LlmStartupWarningService(
            Options.Create(new LlmOptions { Enabled = true, Tier = LlmTier.Free }), logger);

        await sut.StartAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("bậc miễn phí"));
    }

    [Fact]
    public async Task StartAsync_EnabledAndPaidTier_DoesNotLog()
    {
        var logger = new CapturingLogger<LlmStartupWarningService>();
        var sut = new LlmStartupWarningService(
            Options.Create(new LlmOptions { Enabled = true, Tier = LlmTier.Paid }), logger);

        await sut.StartAsync(CancellationToken.None);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task StartAsync_Disabled_DoesNotLogEvenOnFreeTier()
    {
        var logger = new CapturingLogger<LlmStartupWarningService>();
        var sut = new LlmStartupWarningService(
            Options.Create(new LlmOptions { Enabled = false, Tier = LlmTier.Free }), logger);

        await sut.StartAsync(CancellationToken.None);

        Assert.Empty(logger.Entries);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
