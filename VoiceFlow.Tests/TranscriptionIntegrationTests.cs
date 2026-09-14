using Microsoft.Extensions.Logging.Abstractions;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Stt;
using Xunit;
using Xunit.Abstractions;

namespace VoiceFlow.Tests;

/// <summary>
/// End-to-end check against the real Parakeet model. Skipped automatically when the model
/// has not been downloaded on this machine, so the suite still runs on a clean checkout.
/// </summary>
public class TranscriptionIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public TranscriptionIntegrationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task TranscribesSpanishAudioOffline()
    {
        var modelDirectory = AppPaths.DefaultModelDirectory;

        if (!SherpaTranscriptionService.ModelFilesExist(modelDirectory, out var missing))
        {
            _output.WriteLine($"Model not present ({missing}); skipping.");
            return;
        }

        var settings = new FakeSettingsService();
        using var service = new SherpaTranscriptionService(settings, NullLogger<SherpaTranscriptionService>.Instance);

        await service.InitializeAsync();
        Assert.Equal(SttModelStatus.Ready, service.State.Status);

        var audio = TestAudio.LoadSpanishSample();
        var result = await service.TranscribeAsync(audio);

        _output.WriteLine($"[{result.LatencyMs} ms] {result.Text}");

        Assert.False(result.IsEmpty);
        Assert.True(result.Text.Length > 20, "The transcript is suspiciously short: " + result.Text);
        Assert.Contains("país", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(this, Current);
            return Task.CompletedTask;
        }

        public string? GetApiKey() => null;

        public void SetApiKey(string? apiKey)
        {
        }
    }
}
