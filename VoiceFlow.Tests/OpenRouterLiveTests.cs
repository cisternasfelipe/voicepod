using Microsoft.Extensions.Logging.Abstractions;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Llm;
using Xunit;
using Xunit.Abstractions;

namespace VoiceFlow.Tests;

/// <summary>
/// Checks against the live OpenRouter API. They skip themselves when the machine is
/// offline, and none of them needs a real key: the point is that an invalid key must be
/// reported as such (its /models endpoint answers without credentials, which used to make
/// the connection test pass for any key at all).
/// </summary>
public class OpenRouterLiveTests
{
    private const string InvalidKey = "sk-or-v1-0000000000000000000000000000000000000000000000000000000000000000";

    private readonly ITestOutputHelper _output;

    public OpenRouterLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ModelCatalogue_IsReadable()
    {
        var client = CreateClient(InvalidKey);

        IReadOnlyList<LlmModelInfo> models;

        try
        {
            models = await client.GetModelsAsync();
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            _output.WriteLine("Offline; skipping.");
            return;
        }

        _output.WriteLine($"{models.Count} modelos, {models.Count(m => m.IsFree)} gratuitos");

        Assert.True(models.Count > 50, $"Se esperaba un catálogo grande, llegaron {models.Count}.");
        Assert.All(models, m => Assert.Contains('/', m.Id));

        // OpenRouter publishes names, context length and prices for every entry.
        Assert.Contains(models, m => m.ContextLength > 0);
        Assert.Contains(models, m => !string.IsNullOrWhiteSpace(m.Name));
        Assert.Contains(models, m => m.PromptPricePerMillion > 0);

        var sample = models.First(m => m.PromptPricePerMillion > 0);
        _output.WriteLine($"ejemplo: {sample.Id} | {sample.DisplayName} | {sample.Summary}");
        Assert.NotEmpty(sample.Summary);
    }

    [Fact]
    public async Task ConnectionTest_RejectsAnInvalidKey()
    {
        var client = CreateClient(InvalidKey);

        LlmConnectionResult result;

        try
        {
            result = await client.TestConnectionAsync();
        }
        catch (Exception ex) when (IsOffline(ex))
        {
            _output.WriteLine("Offline; skipping.");
            return;
        }

        _output.WriteLine($"success={result.Success} error={result.Error}");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);

        // The message has to be the readable one, not the raw JSON: OpenRouter sends the
        // error code as a number, which the parser has to tolerate.
        Assert.DoesNotContain("{", result.Error);
        Assert.Contains("401", result.Error);
    }

    private static OpenAiCompatibleClient CreateClient(string apiKey)
    {
        var settings = new FakeSettings(apiKey);
        settings.Current.Llm.BaseUrl = LlmProviders.OpenRouterBaseUrl;
        settings.Current.Llm.TimeoutSeconds = 30;

        return new OpenAiCompatibleClient(
            new HttpClient(),
            settings,
            NullLogger<OpenAiCompatibleClient>.Instance);
    }

    private static bool IsOffline(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException
        || ex.InnerException is HttpRequestException or TaskCanceledException;

    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _apiKey;

        public FakeSettings(string? apiKey) => _apiKey = apiKey;

        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }

        public string? GetApiKey() => _apiKey;

        public void SetApiKey(string? apiKey)
        {
        }
    }
}
