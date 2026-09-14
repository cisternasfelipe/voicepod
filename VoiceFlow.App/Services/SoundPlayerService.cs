using System.Media;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Pipeline;

namespace VoiceFlow.App.Services;

/// <summary>
/// Optional start/stop cues. System sounds are used so nothing extra has to ship with the
/// installer, and they are skipped entirely when the user turns them off.
/// </summary>
public sealed class SoundPlayerService
{
    private readonly ISettingsService _settings;

    public SoundPlayerService(ISettingsService settings) => _settings = settings;

    public void Play(DictationState state)
    {
        if (!_settings.Current.General.PlaySounds)
        {
            return;
        }

        try
        {
            switch (state)
            {
                case DictationState.Recording:
                    SystemSounds.Asterisk.Play();
                    break;

                case DictationState.Pasting:
                    SystemSounds.Beep.Play();
                    break;

                case DictationState.Error:
                    SystemSounds.Exclamation.Play();
                    break;
            }
        }
        catch (Exception)
        {
            // A missing sound device must never break a dictation.
        }
    }
}
