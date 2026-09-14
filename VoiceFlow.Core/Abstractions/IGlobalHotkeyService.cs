using VoiceFlow.Core.Models;

namespace VoiceFlow.Core.Abstractions;

/// <summary>Why a registration failed, so the UI can word the message in its own language.</summary>
public enum HotkeyFailure
{
    None,
    InvalidCombination,
    AlreadyInUse,
    Other
}

public sealed class HotkeyRegistrationResult
{
    private HotkeyRegistrationResult(bool success, HotkeyFailure failure, string? detail)
    {
        Success = success;
        Failure = failure;
        Detail = detail;
    }

    public bool Success { get; }

    public HotkeyFailure Failure { get; }

    /// <summary>Technical detail (the Win32 message), if any.</summary>
    public string? Detail { get; }

    public static HotkeyRegistrationResult Ok() => new(true, HotkeyFailure.None, null);

    public static HotkeyRegistrationResult Fail(HotkeyFailure failure, string? detail = null) =>
        new(false, failure, detail);
}

public interface IGlobalHotkeyService : IDisposable
{
    HotkeyDefinition? Current { get; }

    /// <summary>Raised when the combination goes down (both modes).</summary>
    event EventHandler? Pressed;

    /// <summary>Raised when the combination is released; only meaningful in push-to-talk.</summary>
    event EventHandler? Released;

    HotkeyRegistrationResult Register(HotkeyDefinition definition, HotkeyMode mode);

    void Unregister();
}
