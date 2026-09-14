using NAudio.Wave;
using VoiceFlow.Audio;
using VoiceFlow.Core.Models;
using Xunit;

namespace VoiceFlow.Tests;

public class AudioConversionTests
{
    [Fact]
    public void ReadMonoFrames_AveragesStereoFloatChannels()
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2);
        var buffer = new byte[2 * 2 * sizeof(float)];

        WriteFloat(buffer, 0, 1.0f);
        WriteFloat(buffer, 4, 0.0f);   // frame 0 -> 0.5
        WriteFloat(buffer, 8, -1.0f);
        WriteFloat(buffer, 12, -0.5f); // frame 1 -> -0.75

        var frames = AudioCaptureService.ReadMonoFrames(buffer, buffer.Length, format);

        Assert.Equal(2, frames.Length);
        Assert.Equal(0.5f, frames[0], 5);
        Assert.Equal(-0.75f, frames[1], 5);
    }

    [Fact]
    public void ReadMonoFrames_ReadsSixteenBitPcm()
    {
        var format = new WaveFormat(44_100, 16, 1);
        var buffer = new byte[4];
        BitConverter.GetBytes((short)16384).CopyTo(buffer, 0);
        BitConverter.GetBytes((short)-16384).CopyTo(buffer, 2);

        var frames = AudioCaptureService.ReadMonoFrames(buffer, buffer.Length, format);

        Assert.Equal(2, frames.Length);
        Assert.Equal(0.5f, frames[0], 3);
        Assert.Equal(-0.5f, frames[1], 3);
    }

    [Fact]
    public void Resample_ConvertsFortyEightToSixteenKilohertz()
    {
        var oneSecondAt48K = new float[48_000];
        for (var i = 0; i < oneSecondAt48K.Length; i++)
        {
            oneSecondAt48K[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 48_000.0);
        }

        var resampled = AudioCaptureService.Resample(oneSecondAt48K, 48_000, 16_000);

        // The resampler adds a small amount of latency, so allow a few samples of slack.
        Assert.InRange(resampled.Length, 15_800, 16_200);
    }

    [Fact]
    public void Resample_IsANoOpWhenRatesMatch()
    {
        var samples = new float[] { 0.1f, 0.2f, 0.3f };

        var result = AudioCaptureService.Resample(samples, 16_000, 16_000);

        Assert.Same(samples, result);
    }

    [Fact]
    public void ToPcm16_ClampsAndScalesSamples()
    {
        var audio = new RecordedAudio([0f, 1f, -1f, 2f]);

        var pcm = audio.ToPcm16();

        Assert.Equal(8, pcm.Length);
        Assert.Equal(0, BitConverter.ToInt16(pcm, 0));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(pcm, 2));
        Assert.Equal(-short.MaxValue, BitConverter.ToInt16(pcm, 4));
        Assert.Equal(short.MaxValue, BitConverter.ToInt16(pcm, 6));
    }

    [Fact]
    public void Duration_IsDerivedFromSampleCount()
    {
        var audio = new RecordedAudio(new float[32_000]);

        Assert.Equal(TimeSpan.FromSeconds(2), audio.Duration);
    }

    private static void WriteFloat(byte[] buffer, int offset, float value) =>
        BitConverter.GetBytes(value).CopyTo(buffer, offset);
}
