using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VoiceFlow.App.Resources;
using VoiceFlow.Core.Models;
using VoiceFlow.Core.Pipeline;

namespace VoiceFlow.App.ViewModels;

/// <summary>State shown by the floating overlay while a dictation is running.</summary>
public sealed partial class OverlayViewModel : ObservableObject, IDisposable
{
    private readonly DictationPipeline _pipeline;
    private readonly DispatcherTimer _elapsedTimer;
    private readonly Stopwatch _recordingClock = new();

    [ObservableProperty]
    private string _stateText = Strings.StateIdle;

    [ObservableProperty]
    private string? _detailText;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private Brush _stateBrush = Brushes.Gray;

    public OverlayViewModel(DictationPipeline pipeline)
    {
        _pipeline = pipeline;

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };

        _elapsedTimer.Tick += (_, _) =>
            ElapsedText = _recordingClock.IsRunning
                ? $"{_recordingClock.Elapsed.TotalSeconds:0.0} s"
                : string.Empty;

        _pipeline.StateChanged += OnStateChanged;
        _pipeline.LevelChanged += OnLevelChanged;
    }

    /// <summary>Raised when the overlay should become visible or hide again.</summary>
    public event EventHandler<bool>? VisibilityRequested;

    private void OnStateChanged(object? sender, DictationStateChangedEventArgs e) => Dispatch(() =>
    {
        StateText = MainViewModel.Describe(e.State);
        DetailText = e.HasMessage ? MainViewModel.DescribeMessage(e) : null;
        IsRecording = e.State == DictationState.Recording;

        StateBrush = e.State switch
        {
            DictationState.Recording => new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A)),
            DictationState.Transcribing => new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)),
            DictationState.Processing => new SolidColorBrush(Color.FromRgb(0xC9, 0x8A, 0xFF)),
            DictationState.Pasting => new SolidColorBrush(Color.FromRgb(0x4C, 0xC3, 0x8A)),
            DictationState.Error => new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xB4))
        };

        if (e.State == DictationState.Recording)
        {
            _recordingClock.Restart();
            _elapsedTimer.Start();
            VisibilityRequested?.Invoke(this, true);
            return;
        }

        if (e.State is DictationState.Idle or DictationState.Error)
        {
            _recordingClock.Stop();
            _elapsedTimer.Stop();
            Level = 0;

            // Leave the last state on screen briefly so the user can read it.
            var delay = e.HasMessage ? TimeSpan.FromSeconds(3) : TimeSpan.FromMilliseconds(900);
            HideAfter(delay);
            return;
        }

        _recordingClock.Stop();
        VisibilityRequested?.Invoke(this, true);
    });

    private void OnLevelChanged(object? sender, AudioLevelEventArgs e) => Dispatch(() => Level = e.Peak);

    private void HideAfter(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };

        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (!_pipeline.IsBusy)
            {
                VisibilityRequested?.Invoke(this, false);
            }
        };

        timer.Start();
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _pipeline.StateChanged -= OnStateChanged;
        _pipeline.LevelChanged -= OnLevelChanged;
        _elapsedTimer.Stop();
    }
}
