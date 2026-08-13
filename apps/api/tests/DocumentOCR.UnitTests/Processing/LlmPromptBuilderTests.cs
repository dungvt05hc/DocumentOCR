using DocumentOCR.Application.Processing;
using Xunit;

namespace DocumentOCR.UnitTests.Processing;

public class LlmPromptBuilderTests
{
    [Fact]
    public void BuildSystemPrompt_Always_ReturnsSameDeterministicText()
    {
        Assert.Equal(LlmPromptBuilder.BuildSystemPrompt(), LlmPromptBuilder.BuildSystemPrompt());
    }

    [Theory]
    [InlineData("do not calculate")]
    [InlineData("return null")]
    [InlineData("NEVER guess")]
    [InlineData("Số tài khoản")]
    [InlineData("Chữ ký số")]
    [InlineData("sourceText")]
    public void BuildSystemPrompt_Always_ContainsCoreAntiHallucinationRules(string expectedFragment)
    {
        var prompt = LlmPromptBuilder.BuildSystemPrompt();

        Assert.Contains(expectedFragment, prompt, StringComparison.OrdinalIgnoreCase);
    }
}
