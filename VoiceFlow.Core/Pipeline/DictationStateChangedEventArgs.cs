namespace VoiceFlow.Core.Pipeline;

/// <summary>
/// What happened, without any wording attached: the presentation layer turns these into
/// localised text, so Core stays language-neutral.
/// </summary>
public enum DictationMessageKind
{
    None,
    RecordingTooShort,
    DictationCancelled,
    NoTextRecognized,
    LlmFallback,
    MicrophoneFailed,
    DictationFailed,
    PasteFailed
}

public sealed class DictationStateChangedEventArgs : EventArgs
{
    public DictationStateChangedEventArgs(
        DictationState state,
        DictationMessageKind kind = DictationMessageKind.None,
        string? detail = null)
    {
        State = state;
        Kind = kind;
        Detail = detail;
    }

    public DictationState State { get; }

    public DictationMessageKind Kind { get; }

    /// <summary>Technical detail (an exception or API message) to append to the localised text.</summary>
    public string? Detail { get; }

    public bool HasMessage => Kind != DictationMessageKind.None;
}
