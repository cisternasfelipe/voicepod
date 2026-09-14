namespace VoiceFlow.Core.Pipeline;

/// <summary>
/// States of the dictation pipeline. The normal flow is
/// Idle -> Recording -> Transcribing -> Processing -> Pasting -> Idle.
/// <see cref="Error"/> can be entered from any state and always returns to Idle.
/// </summary>
public enum DictationState
{
    Idle,
    Recording,
    Transcribing,
    Processing,
    Pasting,
    Error
}
