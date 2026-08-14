using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentOCR.Infrastructure.Llm;

/// <summary>
/// Logs a one-time Warning at host startup when the LLM extraction path is enabled and configured
/// on the free tier (<see cref="LlmOptions.Tier"/>) — a free-tier key means the provider may use
/// submitted document content to improve their product, so this must be visible to whoever
/// operates the deployment, not just documented in a file nobody reads at 2am.
/// </summary>
public sealed class LlmStartupWarningService(
    IOptions<LlmOptions> options, ILogger<LlmStartupWarningService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var llmOptions = options.Value;

        if (llmOptions.Enabled && llmOptions.Tier == LlmTier.Free)
        {
            logger.LogWarning(
                "LLM đang chạy ở bậc miễn phí — nội dung gửi đi có thể được nhà cung cấp sử dụng " +
                "để cải thiện sản phẩm. Chỉ dùng cho dữ liệu thử nghiệm, KHÔNG dùng cho chứng từ " +
                "của khách hàng thật.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
