namespace VoiceFlow.Core.Models;

/// <summary>
/// A finished recording, already normalised to what the recognizer expects:
/// mono 16 kHz float samples in the range [-1, 1].
/// </summary>
public sealed class RecordedAudio
{
    public const int TargetSampleRate = 16_000;

    public RecordedAudio(float[] samples, int sampleRate = TargetSampleRate)
    {
        Samples = samples;
        SampleRate = sampleRate;
    }

    public static RecordedAudio Empty { get; } = new([]);

    public float[] Samples { get; }

    public int SampleRate { get; }

    public TimeSpan Duration => TimeSpan.FromSeconds(Samples.Length / (double)SampleRate);

    public bool IsEmpty => Samples.Length == 0;

    /// <summary>16-bit little endian PCM, for writing a WAV file.</summary>
    public byte[] ToPcm16()
    {
        var buffer = new byte[Samples.Length * 2];
        for (var i = 0; i < Samples.Length; i++)
        {
            var clamped = Math.Clamp(Samples[i], -1f, 1f);
            var value = (short)Math.Round(clamped * short.MaxValue);
            buffer[i * 2] = (byte)(value & 0xFF);
            buffer[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return buffer;
    }

    /// <summary>Encodes the recorded audio into a standard 16-bit PCM RIFF/WAVE byte array.</summary>
    public byte[] ToWavBytes()
    {
        var pcm = ToPcm16();
        const short channels = 1;
        const short bitsPerSample = 16;
        var blockAlign = (short)(channels * (bitsPerSample / 8));
        var byteRate = SampleRate * blockAlign;

        var header = new byte[44];
        header[0] = (byte)'R'; header[1] = (byte)'I'; header[2] = (byte)'F'; header[3] = (byte)'F';
        var chunkSize = 36 + pcm.Length;
        header[4] = (byte)(chunkSize & 0xFF);
        header[5] = (byte)((chunkSize >> 8) & 0xFF);
        header[6] = (byte)((chunkSize >> 16) & 0xFF);
        header[7] = (byte)((chunkSize >> 24) & 0xFF);
        header[8] = (byte)'W'; header[9] = (byte)'A'; header[10] = (byte)'V'; header[11] = (byte)'E';
        header[12] = (byte)'f'; header[13] = (byte)'m'; header[14] = (byte)'t'; header[15] = (byte)' ';
        header[16] = 16; header[17] = 0; header[18] = 0; header[19] = 0;
        header[20] = 1; header[21] = 0;
        header[22] = (byte)channels; header[23] = 0;
        header[24] = (byte)(SampleRate & 0xFF);
        header[25] = (byte)((SampleRate >> 8) & 0xFF);
        header[26] = (byte)((SampleRate >> 16) & 0xFF);
        header[27] = (byte)((SampleRate >> 24) & 0xFF);
        header[28] = (byte)(byteRate & 0xFF);
        header[29] = (byte)((byteRate >> 8) & 0xFF);
        header[30] = (byte)((byteRate >> 16) & 0xFF);
        header[31] = (byte)((byteRate >> 24) & 0xFF);
        header[32] = (byte)(blockAlign & 0xFF);
        header[33] = (byte)((blockAlign >> 8) & 0xFF);
        header[34] = (byte)(bitsPerSample & 0xFF); header[35] = (byte)((bitsPerSample >> 8) & 0xFF);
        header[36] = (byte)'d'; header[37] = (byte)'a'; header[38] = (byte)'t'; header[39] = (byte)'a';
        header[40] = (byte)(pcm.Length & 0xFF);
        header[41] = (byte)((pcm.Length >> 8) & 0xFF);
        header[42] = (byte)((pcm.Length >> 16) & 0xFF);
        header[43] = (byte)((pcm.Length >> 24) & 0xFF);

        var result = new byte[44 + pcm.Length];
        Buffer.BlockCopy(header, 0, result, 0, 44);
        Buffer.BlockCopy(pcm, 0, result, 44, pcm.Length);
        return result;
    }
}

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public sealed class AudioLevelEventArgs : EventArgs
{
    public AudioLevelEventArgs(float peak, float rms)
    {
        Peak = peak;
        Rms = rms;
    }

    /// <summary>Peak amplitude of the last buffer, 0..1.</summary>
    public float Peak { get; }

    /// <summary>RMS amplitude of the last buffer, 0..1.</summary>
    public float Rms { get; }
}
