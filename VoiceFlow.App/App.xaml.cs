using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using VoiceFlow.App.Resources;
using VoiceFlow.App.Services;
using VoiceFlow.App.ViewModels;
using VoiceFlow.App.Views;
using VoiceFlow.Audio;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Core.Pipeline;
using VoiceFlow.Interop;
using VoiceFlow.Llm;
using VoiceFlow.Storage;
using VoiceFlow.Stt;

namespace VoiceFlow.App;

public partial class App : Application
{
    /// <summary>Command line switch used by the "start with Windows" entry.</summary>
    public const string TrayStartupSwitch = "--tray";

    private SingleInstanceGuard? _guard;
    private IHost? _host;
    private TrayIconController? _tray;
    private MainWindow? _mainWindow;
    private MainViewModel? _mainViewModel;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            await StartAsync(e).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            BootstrapLog.Write("Startup failed", ex);
            MessageBox.Show(
                Strings.Format("MessageStartupFailedFormat", ex.Message),
                "VoiceFlow",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartAsync(StartupEventArgs e)
    {
        _guard = new SingleInstanceGuard();

        if (!_guard.IsFirstInstance)
        {
            // Hand the request over to the running instance and disappear.
            _guard.SignalExistingInstance();
            _guard.Dispose();
            Shutdown();
            return;
        }

        AppPaths.EnsureDirectory(AppPaths.Root);
        HookExceptionHandlers();

        _host = BuildHost();
        await _host.StartAsync().ConfigureAwait(true);

        var services = _host.Services;
        var settings = services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync().ConfigureAwait(true);

        // The language has to be in place before any window is created.
        ApplyLanguage(settings.Current.General.Language);

        _mainViewModel = services.GetRequiredService<MainViewModel>();
        _mainViewModel.SettingsRequested += (_, _) => ShowSettings();
        _mainViewModel.HistoryRequested += (_, _) => ShowHistory();
        _mainWindow = services.GetRequiredService<MainWindow>();

        var pipeline = services.GetRequiredService<DictationPipeline>();
        pipeline.AudioSink = audio => SaveRecordingAsync(audio, settings.Current);

        // Opening the capture device takes ~400 ms, so it happens now instead of on the hotkey.
        var capture = services.GetRequiredService<IAudioCaptureService>();
        _ = Task.Run(() => capture.Prepare(settings.Current.Audio.InputDeviceId));

        SetUpOverlay(services, pipeline);

        var sounds = services.GetRequiredService<SoundPlayerService>();
        pipeline.StateChanged += (_, args) => sounds.Play(args.State);
        pipeline.Completed += OnDictationCompleted;
        pipeline.HistoryChanged += (_, _) => Dispatcher.BeginInvoke(() => _historyWindow?.RefreshFromHost());

        await services.GetRequiredService<IHistoryRepository>().InitializeAsync().ConfigureAwait(true);

        _tray = services.GetRequiredService<TrayIconController>();
        _tray.OpenRequested += (_, _) => ShowMainWindow();
        _tray.ExitRequested += (_, _) => ExitApplication();
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.HistoryRequested += (_, _) => ShowHistory();
        _tray.ProfileSelected += async (_, id) =>
        {
            settings.Current.ActiveProfileId = id;
            await settings.SaveAsync().ConfigureAwait(true);
        };

        _guard.ShowRequested += (_, _) => Dispatcher.BeginInvoke(ShowMainWindow);
        _guard.StartListening();

        Log.Information("VoiceFlow started (hidden={Hidden})", e.Args.Length > 0);

        RegisterHotkey();

        var startHidden = e.Args.Any(arg =>
            string.Equals(arg, TrayStartupSwitch, StringComparison.OrdinalIgnoreCase));

        if (!startHidden)
        {
            ShowMainWindow();
        }

        await PrepareModelAsync(startHidden).ConfigureAwait(true);

        if (e.Args.Contains("--ui-smoke"))
        {
            ShowSettings();
        }
    }

    /// <summary>
    /// Makes sure the model is on disk (running the first-run assistant when it is not) and
    /// then loads it into memory in the background, so the UI never waits for it.
    /// </summary>
    private async Task PrepareModelAsync(bool startHidden)
    {
        var services = _host!.Services;
        var transcription = services.GetRequiredService<ITranscriptionService>();
        var downloader = services.GetRequiredService<IModelDownloader>();
        var settings = services.GetRequiredService<ISettingsService>().Current;

        var modelDirectory = string.IsNullOrWhiteSpace(settings.Stt.ModelDirectory)
            ? AppPaths.DefaultModelDirectory
            : settings.Stt.ModelDirectory!;

        if (!downloader.IsModelPresent(modelDirectory))
        {
            if (startHidden)
            {
                _tray?.ShowMessage(
                    "VoiceFlow",
                    Strings.MessageModelMissingTray,
                    isError: true);
                return;
            }

            ShowModelSetup();
            return;
        }

        // Loading happens on a worker thread inside the service; do not await it here.
        _ = Task.Run(() => transcription.InitializeAsync());
    }

    private void ShowModelSetup()
    {
        var window = _host!.Services.GetRequiredService<ModelSetupWindow>();
        window.Owner = _mainWindow is { IsVisible: true } ? _mainWindow : null;
        window.ShowDialog();
    }

    private void ShowHistory()
    {
        if (_historyWindow is null || !_historyWindow.IsLoaded)
        {
            _historyWindow = _host!.Services.GetRequiredService<HistoryWindow>();
            _historyWindow.Closed += (_, _) => _historyWindow = null;
        }

        _historyWindow.ShowAndActivate();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = _host!.Services.GetRequiredService<SettingsWindow>();
        var viewModel = (SettingsViewModel)_settingsWindow.DataContext;

        // Re-register the hotkey as soon as the user saves a new combination.
        viewModel.HotkeyChanged += (_, _) => RegisterHotkey(viewModel);
        viewModel.ModelSetupRequested += (_, _) => ShowModelSetup();
        viewModel.ModelBrowserRequested += (_, _) => ShowModelPicker(viewModel);

        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>Shows the model catalogue of the configured endpoint (OpenRouter, OpenAI, …).</summary>
    private void ShowModelPicker(SettingsViewModel settingsViewModel)
    {
        var services = _host!.Services;
        var viewModel = services.GetRequiredService<ModelPickerViewModel>();
        viewModel.InitialModelId = settingsViewModel.Model;

        var window = new ModelPickerWindow(viewModel)
        {
            Owner = _settingsWindow
        };

        if (window.ShowDialog() == true && window.SelectedModelId is { Length: > 0 } modelId)
        {
            settingsViewModel.ApplyPickedModel(modelId);
        }
    }

    /// <summary>
    /// Picks the resource culture for the UI. Numbers and dates stay on the regional
    /// settings of the machine; only the wording changes.
    /// </summary>
    private static void ApplyLanguage(UiLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language == UiLanguage.English ? "en" : "es");

        Strings.Culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        // Structured logging to %LOCALAPPDATA%\VoiceFlow\logs, one file per day, kept for a week.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(AppPaths.EnsureDirectory(AppPaths.LogsDirectory), "voiceflow-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        var services = builder.Services;

        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<IForegroundWindowProvider, ForegroundWindowProvider>();
        services.AddSingleton<ITranscriptionService, SherpaTranscriptionService>();
        services.AddHttpClient<IModelDownloader, ModelDownloader>();
        services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>();
        services.AddSingleton<PromptProfileService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IPasteService, PasteService>();
        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<SoundPlayerService>();
        services.AddSingleton<IHistoryRepository, SqliteHistoryRepository>();
        services.AddSingleton<StartupRegistration>();
        services.AddTransient<HistoryViewModel>();
        services.AddTransient<HistoryWindow>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ModelPickerViewModel>();
        services.AddTransient<SettingsWindow>();
        services.AddSingleton<DictationPipeline>();
        services.AddSingleton<TrayIconController>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddTransient<ModelSetupViewModel>();
        services.AddTransient<ModelSetupWindow>();

        return builder.Build();
    }

    private void RegisterHotkey(SettingsViewModel? settingsViewModel = null)
    {
        var services = _host!.Services;
        var settings = services.GetRequiredService<ISettingsService>().Current;
        var hotkey = services.GetRequiredService<IGlobalHotkeyService>();

        var definition = settings.Hotkey.ToDefinition();
        var result = hotkey.Register(definition, settings.Hotkey.Mode);

        if (result.Success)
        {
            _mainViewModel?.SetHotkeyError(null);
            settingsViewModel?.SetHotkeyRegistrationError(null);
            _tray?.UpdateToolTip($"VoiceFlow — {HotkeyFormatter.Describe(definition)}");
            return;
        }

        var message = DescribeHotkeyFailure(result, definition);
        _mainViewModel?.SetHotkeyError(message);
        settingsViewModel?.SetHotkeyRegistrationError(message);
        _tray?.ShowMessage("VoiceFlow", message, isError: true);
    }

    private static string DescribeHotkeyFailure(HotkeyRegistrationResult result, HotkeyDefinition definition) =>
        result.Failure switch
        {
            HotkeyFailure.InvalidCombination => Strings.HotkeyInvalid,
            HotkeyFailure.AlreadyInUse => Strings.Format("HotkeyInUseFormat", HotkeyFormatter.Describe(definition)),
            HotkeyFailure.Other => Strings.Format(
                "HotkeyRegisterFailedFormat",
                HotkeyFormatter.Describe(definition),
                result.Detail ?? Strings.UnknownError),
            _ => Strings.MessageHotkeyFailed
        };

    /// <summary>Keeps the WAV only when the user asked for it in the history settings.</summary>
    private static async Task<string?> SaveRecordingAsync(RecordedAudio audio, AppSettings settings)
    {
        if (!settings.History.SaveAudio)
        {
            return null;
        }

        try
        {
            var path = Path.Combine(
                AppPaths.EnsureDirectory(AppPaths.AudioDirectory),
                $"dictation-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");

            await WavFile.WriteAsync(path, audio).ConfigureAwait(false);
            return path;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not store the recording");
            return null;
        }
    }

    private void SetUpOverlay(IServiceProvider services, DictationPipeline pipeline)
    {
        var overlayViewModel = services.GetRequiredService<OverlayViewModel>();
        var overlay = services.GetRequiredService<OverlayWindow>();

        overlayViewModel.VisibilityRequested += (_, visible) => Dispatcher.BeginInvoke(() =>
        {
            if (visible)
            {
                overlay.ShowWithoutActivation();
            }
            else
            {
                overlay.Hide();
            }
        });
    }

    /// <summary>Tells the user when the text could not reach the target window.</summary>
    private void OnDictationCompleted(object? sender, DictationCompletedEventArgs e)
    {
        if (e.PasteOutcome == PasteOutcome.Pasted)
        {
            return;
        }

        var message = e.PasteOutcome == PasteOutcome.CopiedOnly
            ? Strings.MessageCopiedNotPasted
            : Strings.Format("MessageDeliveryFailedFormat", e.PasteError ?? Strings.UnknownError);

        Dispatcher.BeginInvoke(() => _tray?.ShowMessage("VoiceFlow", message, isError: true));
    }

    private void ShowMainWindow()
    {
        _mainWindow ??= _host?.Services.GetRequiredService<MainWindow>();
        _mainWindow?.ShowAndActivate();
    }

    private void ExitApplication()
    {
        if (_mainWindow is not null)
        {
            _mainWindow.ForceClose = true;
        }

        Shutdown();
    }

    private void HookExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogFatal(args.ExceptionObject as Exception, "AppDomain");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogFatal(args.Exception, "TaskScheduler");
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception, "Dispatcher");
        _tray?.ShowMessage("VoiceFlow", Strings.MessageUnexpectedError, isError: true);

        // Keep the app alive: a failed dictation must not take the tray icon down with it.
        e.Handled = true;
    }

    private void LogFatal(Exception? exception, string source)
    {
        if (exception is null)
        {
            return;
        }

        var logger = _host?.Services.GetService<ILoggerFactory>()?.CreateLogger("VoiceFlow");

        if (logger is null)
        {
            BootstrapLog.Write($"Unhandled exception from {source}", exception);
            return;
        }

        logger.LogError(exception, "Unhandled exception from {Source}", source);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _host?.Services.GetService<DictationPipeline>()?.Dispose();
        _host?.Services.GetService<IGlobalHotkeyService>()?.Dispose();
        _host?.Services.GetService<IAudioCaptureService>()?.Dispose();
        _mainViewModel?.Dispose();

        if (_host is not null)
        {
            _host.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _host.Dispose();
        }

        _guard?.Dispose();
        base.OnExit(e);
    }
}
