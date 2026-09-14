using System.Text;
using VoiceFlow.Core.Models;

namespace VoiceFlow.Audio;

/// <summary>Minimal 16-bit PCM WAV writer, used when the user opts into keeping the audio.</summary>
public static class WavFile
{
    public static async Task WriteAsync(string path, RecordedAudio audio, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var pcm = audio.ToPcm16();
        var header = BuildHeader(audio.SampleRate, pcm.Length);

        await using var stream = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(pcm, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] BuildHeader(int sampleRate, int dataLength)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = sampleRate * blockAlign;

        using var buffer = new MemoryStream(44);
        using var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                 // size of the PCM format chunk
        writer.Write((short)1);           // WAVE_FORMAT_PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        writer.Flush();

        return buffer.ToArray();
    }
}
