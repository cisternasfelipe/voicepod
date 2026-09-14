using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Abstractions;

public interface IAudioCaptureService : IDisposable
{
    bool IsRecording { get; }

    event EventHandler<AudioLevelEventArgs>? LevelChanged;

    /// <summary>Raised when the configured maximum duration cut the recording short.</summary>
    event EventHandler? MaxDurationReached;

    IReadOnlyList<AudioDeviceInfo> GetInputDevices();

    /// <summary>
    /// Opens the device ahead of time so that <see cref="Start"/> costs a few milliseconds
    /// instead of the ~400 ms an audio client initialisation takes.
    /// </summary>
    void Prepare(string? deviceId);

    /// <summary>Starts capturing from <paramref name="deviceId"/> (null = system default).</summary>
    void Start(string? deviceId, TimeSpan maxDuration);

    /// <summary>Stops capturing and returns the resampled mono 16 kHz recording.</summary>
    RecordedAudio Stop();

    /// <summary>Stops capturing and throws the audio away (used for microphone tests).</summary>
    void Cancel();
}
