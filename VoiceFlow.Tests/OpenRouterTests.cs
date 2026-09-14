using VoiceFlow.Core.Models;
using VoiceFlow.Llm;
using Xunit;

namespace VoiceFlow.Tests;

public class LlmProviderTests
{
    [Theory]
    [InlineData("https://openrouter.ai/api/v1", LlmProviderKind.OpenRouter)]
    [InlineData("https://openrouter.ai/api/v1/", LlmProviderKind.OpenRouter)]
    [InlineData("https://api.openai.com/v1", LlmProviderKind.OpenAi)]
    [InlineData("https://api.groq.com/openai/v1", LlmProviderKind.Groq)]
    [InlineData("http://localhost:1234/v1", LlmProviderKind.LmStudio)]
    [InlineData("http://localhost:11434/v1", LlmProviderKind.Ollama)]
    [InlineData("https://example.com/v1", LlmProviderKind.Custom)]
    [InlineData("", LlmProviderKind.Custom)]
    [InlineData("no-es-una-url", LlmProviderKind.Custom)]
    public void Detect_RecognisesKnownEndpoints(string baseUrl, LlmProviderKind expected) =>
        Assert.Equal(expected, LlmProviders.Detect(baseUrl));

    [Fact]
    public void IsOpenRouter_IgnoresPathAndCase()
    {
        Assert.True(LlmProviders.IsOpenRouter("https://OpenRouter.ai/api/v1"));
        Assert.False(LlmProviders.IsOpenRouter("https://api.openai.com/v1"));
    }

    [Fact]
    public void OpenRouterPreset_UsesAPrefixedModelId()
    {
        var preset = LlmProviders.Find(LlmProviderKind.OpenRouter);

        Assert.NotNull(preset);
        Assert.Equal(LlmProviders.OpenRouterBaseUrl, preset!.BaseUrl);
        Assert.Contains("/", preset.SuggestedModel);
        Assert.True(preset.NeedsApiKey);
    }

    [Fact]
    public void LocalPresets_DoNotNeedAKey()
    {
        Assert.False(LlmProviders.Find(LlmProviderKind.LmStudio)!.NeedsApiKey);
        Assert.False(LlmProviders.Find(LlmProviderKind.Ollama)!.NeedsApiKey);
    }
}

public class OpenRouterPayloadTests
{
    /// <summary>Trimmed copy of a real https://openrouter.ai/api/v1/models response.</summary>
    private const string OpenRouterModels = """
        {"data":[
          {"id":"openai/gpt-4o-mini","canonical_slug":"openai/gpt-4o-mini","name":"OpenAI: GPT-4o-mini",
           "created":1721260800,"description":"...","context_length":128000,
           "architecture":{"modality":"text+image->text"},
           "pricing":{"prompt":"0.00000015","completion":"0.0000006","request":"0","image":"0"}},
          {"id":"meta-llama/llama-3.3-70b-instruct:free","name":"Meta: Llama 3.3 70B Instruct (free)",
           "context_length":65536,
           "pricing":{"prompt":"0","completion":"0"}},
          {"id":"anthropic/claude-sonnet-4.6","name":"Anthropic: Claude Sonnet 4.6","context_length":200000,
           "pricing":{"prompt":"0.000003","completion":"0.000015"}}
        ]}
        """;

    /// <summary>Plain OpenAI-compatible shape: ids only, as LM Studio or OpenAI return.</summary>
    private const string OpenAiModels = """
        {"object":"list","data":[
          {"id":"gpt-4o-mini","object":"model","created":1721260800,"owned_by":"system"},
          {"id":"gpt-4o","object":"model","created":1715367049,"owned_by":"system"}
        ]}
        """;

    [Fact]
    public void ParseModels_ReadsOpenRouterMetadata()
    {
        var models = OpenAiCompatibleClient.ParseModels(OpenRouterModels);

        Assert.Equal(3, models.Count);

        var mini = models.Single(m => m.Id == "openai/gpt-4o-mini");
        Assert.Equal("OpenAI: GPT-4o-mini", mini.DisplayName);
        Assert.Equal(128_000, mini.ContextLength);

        // Prices arrive per token and are shown per million.
        Assert.Equal(0.15m, mini.PromptPricePerMillion);
        Assert.Equal(0.60m, mini.CompletionPricePerMillion);
        Assert.False(mini.IsFree);
        Assert.Contains("128k ctx", mini.Summary);
    }

    [Fact]
    public void ParseModels_FlagsFreeModels()
    {
        var models = OpenAiCompatibleClient.ParseModels(OpenRouterModels);
        var free = models.Single(m => m.Id.EndsWith(":free", StringComparison.Ordinal));

        Assert.True(free.IsFree);
        Assert.Contains("free", free.Summary);
    }

    [Fact]
    public void ParseModels_HandlesPlainOpenAiPayload()
    {
        var models = OpenAiCompatibleClient.ParseModels(OpenAiModels);

        Assert.Equal(2, models.Count);
        Assert.Equal("gpt-4o", models[0].Id);
        Assert.Equal("gpt-4o", models[0].DisplayName);
        Assert.Null(models[0].ContextLength);
        Assert.Empty(models[0].Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":[{\"no-id\":1}]}")]
    public void ParseModels_IsForgivingWithOddPayloads(string body) =>
        Assert.Empty(OpenAiCompatibleClient.ParseModels(body));

    [Fact]
    public void ExtractErrorMessage_HandlesOpenRouterNumericCode()
    {
        // OpenRouter sends {"error":{"message":"...","code":401}} with a numeric code, which
        // used to break deserialisation and leak the raw JSON into the UI.
        var message = OpenAiCompatibleClient.ExtractErrorMessage(
            """{"error":{"message":"User not found.","code":401}}""");

        Assert.Equal("User not found.", message);
    }

    [Fact]
    public void ExtractErrorMessage_HandlesOpenAiStringCode()
    {
        var message = OpenAiCompatibleClient.ExtractErrorMessage(
            """{"error":{"message":"Incorrect API key provided","type":"invalid_request_error","code":"invalid_api_key"}}""");

        Assert.Equal("Incorrect API key provided", message);
    }

    [Fact]
    public void ExtractErrorMessage_FallsBackToTheBody()
    {
        Assert.Equal("upstream is down", OpenAiCompatibleClient.ExtractErrorMessage("upstream is down"));
        Assert.Null(OpenAiCompatibleClient.ExtractErrorMessage(""));
    }

    [Fact]
    public void DescribeOpenRouterKey_SummarisesLabelAndCredit()
    {
        var summary = OpenAiCompatibleClient.DescribeOpenRouterKey(
            """{"data":{"label":"voiceflow","usage":1.25,"limit":10,"limit_remaining":8.75,"is_free_tier":false}}""");

        Assert.NotNull(summary);
        Assert.Contains("voiceflow", summary);
        Assert.Contains("8.75", summary);
    }

    [Fact]
    public void DescribeOpenRouterKey_FallsBackToUsageWhenThereIsNoLimit()
    {
        var summary = OpenAiCompatibleClient.DescribeOpenRouterKey(
            """{"data":{"label":"sin límite","usage":3.5,"limit":null,"limit_remaining":null,"is_free_tier":true}}""");

        Assert.NotNull(summary);
        Assert.Contains("3.5", summary);
        Assert.Contains("free tier", summary);
    }

    [Fact]
    public void DescribeOpenRouterKey_IgnoresUnexpectedPayloads()
    {
        Assert.Null(OpenAiCompatibleClient.DescribeOpenRouterKey("{}"));
        Assert.Null(OpenAiCompatibleClient.DescribeOpenRouterKey("not json"));
    }

    [Fact]
    public void BuildUri_JoinsBaseUrlAndPath()
    {
        Assert.Equal(
            new Uri("https://openrouter.ai/api/v1/chat/completions"),
            OpenAiCompatibleClient.BuildUri("https://openrouter.ai/api/v1/", "chat/completions"));

        Assert.Equal(
            new Uri("https://openrouter.ai/api/v1/key"),
            OpenAiCompatibleClient.BuildUri("https://openrouter.ai/api/v1", "key"));
    }
}
