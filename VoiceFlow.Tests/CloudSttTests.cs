using System.Text;
using VoiceFlow.Core.Models;
using Xunit;

namespace VoiceFlow.Tests;

public class CloudSttCatalogTests
{
    [Fact]
    public void Catalog_HasMaiTranscribe2AsDefault()
    {
        Assert.Equal("microsoft/mai-transcribe-2", CloudSttCatalog.DefaultModelId);

        var defaultModel = CloudSttCatalog.Models.FirstOrDefault(m => m.IsDefault);
        Assert.NotNull(defaultModel);
        Assert.Equal("microsoft/mai-transcribe-2", defaultModel!.Id);
        Assert.Equal("MAI-Transcribe 2", defaultModel.DisplayName);
        Assert.Equal("Microsoft AI", defaultModel.Provider);
    }

    [Fact]
    public void Catalog_ContainsAllExpectedModels()
    {
        Assert.True(CloudSttCatalog.Models.Count >= 16);

        // Verify key models exist
        var expectedIds = new[]
        {
            "microsoft/mai-transcribe-2",
            "openai/whisper-large-v3-turbo",
            "openai/whisper-large-v3",
            "meta/muse-voice-transcribe-1.0",
            "nvidia/nemotron-3.5-asr-streaming-multilingual-0.6b",
            "mistralai/voxtral-small-24b-2507-stt",
            "mistralai/voxtral-mini-3b-2507",
            "qwen/qwen3-asr-1.7b",
            "qwen/qwen3-asr-0.6b",
            "qwen/qwen3-asr-flash-2026-02-10",
            "openai/gpt-4o-mini-transcribe",
            "openai/gpt-transcribe",
            "deepgram/nova-3",
            "google/chirp-3",
            "x-ai/grok-stt-1.0",
            "fish-audio/transcribe-1"
        };

        foreach (var expectedId in expectedIds)
        {
            var found = CloudSttCatalog.Models.FirstOrDefault(m => m.Id == expectedId);
            Assert.True(found is not null, $"Model {expectedId} should exist in CloudSttCatalog.Models");
            Assert.False(string.IsNullOrWhiteSpace(found.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(found.Provider));
            Assert.False(string.IsNullOrWhiteSpace(found.Pricing));
            Assert.False(string.IsNullOrWhiteSpace(found.Description));
        }
    }

    [Fact]
    public void SttSettings_DefaultsToOpenRouterCloudAndMaiTranscribe2()
    {
        var settings = new SttSettings();

        Assert.Equal(SttProvider.OpenRouterCloud, settings.Provider);
        Assert.Equal("microsoft/mai-transcribe-2", settings.CloudModel);
    }

    [Fact]
    public void RecordedAudio_ToWavBytes_ProducesValidRiffWaveHeader()
    {
        var samples = new float[16_000]; // 1 second of silence at 16kHz
        var audio = new RecordedAudio(samples);

        var wav = audio.ToWavBytes();

        Assert.NotNull(wav);
        Assert.Equal(44 + (samples.Length * 2), wav.Length);

        // RIFF header
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

        // Format chunk
        var audioFormat = BitConverter.ToInt16(wav, 20);
        var numChannels = BitConverter.ToInt16(wav, 22);
        var sampleRate = BitConverter.ToInt32(wav, 24);
        var bitsPerSample = BitConverter.ToInt16(wav, 34);

        Assert.Equal(1, audioFormat);       // PCM
        Assert.Equal(1, numChannels);      // Mono
        Assert.Equal(16_000, sampleRate);  // 16 kHz
        Assert.Equal(16, bitsPerSample);   // 16-bit
    }
}
