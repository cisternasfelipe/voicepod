using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Abstractions;

public interface ITranscriptionService : IDisposable
{
    SttModelState State { get; }

    event EventHandler<SttModelState>? StateChanged;

    /// <summary>
    /// Loads the recognizer into memory once. Safe to call repeatedly: later calls
    /// await the first load instead of reloading the model.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<TranscriptionResult> TranscribeAsync(RecordedAudio audio, CancellationToken cancellationToken = default);

    /// <summary>Drops the loaded recognizer so the next initialise picks up new settings.</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}

public interface IModelDownloader
{
    /// <summary>True when every model file is present in <paramref name="modelDirectory"/> with the expected size.</summary>
    bool IsModelPresent(string modelDirectory);

    Task DownloadAsync(
        string modelDirectory,
        string baseUrl,
        bool verifyHashes,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default);
}
