using VoiceFlow.Audio;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Tests;

/// <summary>Loads the bundled Spanish sample used by the integration tests.</summary>
internal static class TestAudio
{
    public static RecordedAudio LoadSpanishSample() =>
        LoadWav(Path.Combine(AppContext.BaseDirectory, "TestData", "es.wav"));

    /// <summary>Reads a 16-bit PCM mono WAV and resamples it to what the recognizer expects.</summary>
    public static RecordedAudio LoadWav(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sampleRate = BitConverter.ToInt32(bytes, 24);

        var offset = 12;
        while (offset + 8 < bytes.Length)
        {
            var chunkId = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
            var chunkSize = BitConverter.ToInt32(bytes, offset + 4);

            if (chunkId == "data")
            {
                offset += 8;
                var count = Math.Min(chunkSize, bytes.Length - offset) / 2;
                var samples = new float[count];

                for (var i = 0; i < count; i++)
                {
                    samples[i] = BitConverter.ToInt16(bytes, offset + (i * 2)) / 32768f;
                }

                return new RecordedAudio(
                    AudioCaptureService.Resample(samples, sampleRate, RecordedAudio.TargetSampleRate));
            }

            offset += 8 + chunkSize;
        }

        throw new InvalidOperationException("The WAV file has no data chunk.");
    }
}
