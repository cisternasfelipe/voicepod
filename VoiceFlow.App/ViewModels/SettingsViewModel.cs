using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using VoiceFlow.App.Resources;
using VoiceFlow.App.Services;
using VoiceFlow.Core;
using VoiceFlow.Core.Abstractions;
using VoiceFlow.Core.Models;
using VoiceFlow.Llm;

namespace VoiceFlow.App.ViewModels;

/// <summary>Backs every tab of the settings window.</summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IAudioCaptureService _audio;
    private readonly ITranscriptionService _transcription;
    private readonly IModelDownloader _downloader;
    private readonly ILlmClient _llm;
    private readonly PromptProfileService _profiles;
    private readonly IHistoryRepository _history;
    private readonly StartupRegistration _startup;
    private readonly ILogger<SettingsViewModel> _logger;

    private HotkeyDefinition _hotkey;
    private bool _micTestRunning;
    private bool _suppressProviderApply;

    // General
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _minimizeToTrayOnClose;
    [ObservableProperty] private bool _playSounds;
    [ObservableProperty] private UiLanguage _language;

    // Hotkey
    [ObservableProperty] private string _hotkeyText = string.Empty;
    [ObservableProperty] private bool _isPushToTalk;
    [ObservableProperty] private string? _hotkeyMessage;

    // Audio
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;
    [ObservableProperty] private int _maxRecordingSeconds;
    [ObservableProperty] private double _micLevel;
    [ObservableProperty] private bool _isMicTestRunning;

    // Transcription
    [ObservableProperty] private string _modelDirectory = string.Empty;
    [ObservableProperty] private int _numThreads;
    [ObservableProperty] private SttProvider _provider;
    [ObservableProperty] private string _modelStatus = string.Empty;
    [ObservableProperty] private CloudSttModelInfo? _selectedCloudModel;

    // AI
    [ObservableProperty] private string _baseUrl = string.Empty;
    [ObservableProperty] private LlmProviderOption? _selectedLlmProvider;
    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _model = string.Empty;
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private int _maxTokens;
    [ObservableProperty] private int _timeoutSeconds;
    [ObservableProperty] private string? _connectionMessage;
    [ObservableProperty] private bool _isTestingConnection;
    [ObservableProperty] private bool _enableReasoning;
    [ObservableProperty] private string _reasoningEffort = "low";

    partial void OnProviderChanged(SttProvider value)
    {
        OnPropertyChanged(nameof(SelectedSttProviderOption));
        OnPropertyChanged(nameof(IsCloudStt));
        OnPropertyChanged(nameof(IsLocalStt));
        ApplyModelState(_transcription.State);
    }

    partial void OnSelectedCloudModelChanged(CloudSttModelInfo? value)
    {
        ApplyModelState(_transcription.State);
    }

    partial void OnApiKeyChanged(string value)
    {
        OnPropertyChanged(nameof(HasApiKeyForCloudStt));
        ApplyModelState(_transcription.State);
    }

    public bool IsCloudStt => Provider == SttProvider.OpenRouterCloud;
    public bool IsLocalStt => Provider != SttProvider.OpenRouterCloud;
    public bool HasApiKeyForCloudStt => !string.IsNullOrWhiteSpace(ApiKey);

    public IReadOnlyList<CloudSttModelInfo> CloudModels => CloudSttCatalog.Models;

    public IReadOnlyList<SttProviderOption> SttProviderOptions { get; } =
    [
        new(SttProvider.OpenRouterCloud, "Nube OpenRouter (Recomendado)", "Rápido y preciso. Soporta MAI-Transcribe 2, Whisper, Voxtral, etc."),
        new(SttProvider.Cpu, "Local (CPU)", "Sherpa-ONNX sin conexión a internet"),
        new(SttProvider.DirectMl, "Local (GPU DirectML)", "Aceleración gráfica en Windows sin conexión"),
        new(SttProvider.Cuda, "Local (NVIDIA CUDA)", "Aceleración NVIDIA dedicada sin conexión")
    ];

    public SttProviderOption? SelectedSttProviderOption
    {
        get => SttProviderOptions.FirstOrDefault(o => o.Value == Provider) ?? SttProviderOptions[0];
        set
        {
            if (value is not null && Provider != value.Value)
            {
                Provider = value.Value;
            }
        }
    }

    partial void OnTemperatureChanged(double value)
    {
        OnPropertyChanged(nameof(TemperatureDisplay));
        OnPropertyChanged(nameof(TemperatureDescription));
        OnPropertyChanged(nameof(GlobalTemperatureText));
    }

    partial void OnMaxTokensChanged(int value)
    {
        OnPropertyChanged(nameof(MaxTokensWordsEstimate));
        OnPropertyChanged(nameof(MaxTokensDescription));
    }

    partial void OnReasoningEffortChanged(string value)
    {
        OnPropertyChanged(nameof(IsReasoningLow));
        OnPropertyChanged(nameof(IsReasoningMedium));
        OnPropertyChanged(nameof(IsReasoningHigh));
    }

    public string TemperatureDisplay => Temperature.ToString("0.0#", CultureInfo.InvariantCulture);

    public string TemperatureDescription => Temperature switch
    {
        <= 0.15 => "🎯 " + Strings.SettingsTemperaturePrecise + " · " + (Strings.Culture?.TwoLetterISOLanguageName == "en"
            ? "Exact and literal for dictation. No hallucination or creative rewrites."
            : "Literal y exacto para dictado. Cero invención o agregados innecesarios."),
        <= 0.45 => "⚖️ " + Strings.SettingsTemperatureBalanced + " · " + (Strings.Culture?.TwoLetterISOLanguageName == "en"
            ? "Recommended: natural flow, fixes punctuation and filler words faithfully."
            : "Recomendado: flujo natural, corrige puntuación y muletillas fielmente."),
        <= 0.85 => "🎨 " + Strings.SettingsTemperatureCreative + " · " + (Strings.Culture?.TwoLetterISOLanguageName == "en"
            ? "Expressive vocabulary, stylistic variety and smoother restructuring."
            : "Mayor soltura, riqueza léxica y variedad de estilo al redactar."),
        _ => "🔥 " + (Strings.Culture?.TwoLetterISOLanguageName == "en"
            ? "High creativity: maximum variation in tone, vocabulary and structure."
            : "Creatividad alta: máxima variación de tono, vocabulario y estructura.")
    };

    public string MaxTokensWordsEstimate =>
        $"≈ hasta {(int)(MaxTokens * 0.75)} " + (Strings.Culture?.TwoLetterISOLanguageName == "en" ? "words" : "palabras");

    public string MaxTokensDescription =>
        $"{Strings.SettingsTokensExplanation} ({MaxTokens} tokens {MaxTokensWordsEstimate})";

    public bool IsReasoningLow
    {
        get => string.Equals(ReasoningEffort, "low", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                ReasoningEffort = "low";
            }
        }
    }

    public bool IsReasoningMedium
    {
        get => string.Equals(ReasoningEffort, "medium", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                ReasoningEffort = "medium";
            }
        }
    }

    public bool IsReasoningHigh
    {
        get => string.Equals(ReasoningEffort, "high", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                ReasoningEffort = "high";
            }
        }
    }

    [RelayCommand]
    private void SetTemperaturePreset(string param)
    {
        if (double.TryParse(param, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
        {
            Temperature = Math.Round(val, 2);
        }
    }

    [RelayCommand]
    private void SetMaxTokensPreset(string param)
    {
        if (int.TryParse(param, out var val))
        {
            MaxTokens = val;
        }
    }

    [RelayCommand]
    private void SetReasoningEffort(string effort)
    {
        ReasoningEffort = effort;
    }

    // Paste
    [ObservableProperty] private bool _restoreClipboard;
    [ObservableProperty] private bool _typeCharacterByCharacter;
    [ObservableProperty] private int _pasteDelayMilliseconds;

    // Profiles
    [ObservableProperty] private PromptProfile? _selectedProfile;
    [ObservableProperty] private string _profileName = string.Empty;
    [ObservableProperty] private string _profilePrompt = string.Empty;
    [ObservableProperty] private string _profileModel = string.Empty;
    [ObservableProperty] private string _profileTemperature = string.Empty;
    [ObservableProperty] private bool _profileUsesLlm = true;
    [ObservableProperty] private string? _profileMessage;

    // History
    [ObservableProperty] private int _maxEntries;
    [ObservableProperty] private int _maxAgeDays;
    [ObservableProperty] private bool _saveAudio;
    [ObservableProperty] private string? _historyMessage;

    public SettingsViewModel(
        ISettingsService settings,
        IAudioCaptureService audio,
        ITranscriptionService transcription,
        IModelDownloader downloader,
        ILlmClient llm,
        PromptProfileService profiles,
        IHistoryRepository history,
        StartupRegistration startup,
        ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _audio = audio;
        _transcription = transcription;
        _downloader = downloader;
        _llm = llm;
        _profiles = profiles;
        _history = history;
        _startup = startup;
        _logger = logger;

        _hotkey = settings.Current.Hotkey.ToDefinition();
        _audio.LevelChanged += OnMicLevel;
        _transcription.StateChanged += OnModelStateChanged;

        LoadFromSettings();
    }

    public ObservableCollection<AudioDeviceInfo> Devices { get; } = [];

    public ObservableCollection<PromptProfile> Profiles { get; } = [];

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(UiLanguage.Spanish, Strings.LanguageSpanish),
        new(UiLanguage.English, Strings.LanguageEnglish)
    ];

    /// <summary>Selected entry of the language combo.</summary>
    public LanguageOption? SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => l.Value == Language);
        set
        {
            if (value is not null)
            {
                Language = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public IReadOnlyList<SttProvider> Providers { get; } = [SttProvider.Cpu, SttProvider.DirectMl, SttProvider.Cuda];

    /// <summary>Known OpenAI-compatible endpoints plus a free-form entry.</summary>
    public IReadOnlyList<LlmProviderOption> LlmProviderOptions { get; } =
    [
        .. LlmProviders.All.Select(p => new LlmProviderOption(p.Kind, p.DisplayName)),
        new(LlmProviderKind.Custom, Strings.ProviderCustom)
    ];

    /// <summary>True while the OpenRouter endpoint is selected, to show its hint.</summary>
    public bool IsOpenRouter => LlmProviders.IsOpenRouter(BaseUrl);

    /// <summary>Where to get a key for the selected provider; null for local servers.</summary>
    public string? ApiKeyUrl => LlmProviders.Find(SelectedLlmProvider?.Kind ?? LlmProviderKind.Custom)?.ApiKeyUrl;

    public bool HasApiKeyUrl => !string.IsNullOrWhiteSpace(ApiKeyUrl);

    /// <summary>Raised when the hotkey changed and the host has to re-register it.</summary>
    public event EventHandler? HotkeyChanged;

    /// <summary>Raised when the STT model has to be reloaded (path, threads or provider changed).</summary>
    public event EventHandler? ModelSetupRequested;

    /// <summary>Raised when the user asks for the model catalogue of the current endpoint.</summary>
    public event EventHandler? ModelBrowserRequested;

    private void LoadFromSettings()
    {
        var current = _settings.Current;

        StartWithWindows = current.General.StartWithWindows;
        MinimizeToTrayOnClose = current.General.MinimizeToTrayOnClose;
        PlaySounds = current.General.PlaySounds;
        Language = current.General.Language;

        _hotkey = current.Hotkey.ToDefinition();
        HotkeyText = HotkeyFormatter.Describe(_hotkey);
        IsPushToTalk = current.Hotkey.Mode == HotkeyMode.PushToTalk;

        MaxRecordingSeconds = current.Audio.MaxRecordingSeconds;
        RefreshDevices();

        ModelDirectory = string.IsNullOrWhiteSpace(current.Stt.ModelDirectory)
            ? AppPaths.DefaultModelDirectory
            : current.Stt.ModelDirectory!;
        NumThreads = current.Stt.NumThreads;
        Provider = current.Stt.Provider;
        var cloudModelId = string.IsNullOrWhiteSpace(current.Stt.CloudModel)
            ? CloudSttCatalog.DefaultModelId
            : current.Stt.CloudModel;
        SelectedCloudModel = CloudModels.FirstOrDefault(m => m.Id == cloudModelId) ?? CloudModels.First();
        ApplyModelState(_transcription.State);

        BaseUrl = current.Llm.BaseUrl;
        ApiKey = _settings.GetApiKey() ?? string.Empty;
        Model = current.Llm.Model;
        SyncProviderFromBaseUrl();
        Temperature = current.Llm.Temperature;
        MaxTokens = current.Llm.MaxTokens;
        TimeoutSeconds = current.Llm.TimeoutSeconds;
        EnableReasoning = current.Llm.EnableReasoning;
        ReasoningEffort = string.IsNullOrWhiteSpace(current.Llm.ReasoningEffort) ? "low" : current.Llm.ReasoningEffort;

        RestoreClipboard = current.Paste.RestoreClipboard;
        TypeCharacterByCharacter = current.Paste.Mode == PasteMode.TypeUnicode;
        PasteDelayMilliseconds = current.Paste.PasteDelayMilliseconds;

        MaxEntries = current.History.MaxEntries;
        MaxAgeDays = current.History.MaxAgeDays;
        SaveAudio = current.History.SaveAudio;

        RefreshProfiles();
    }

    private void RefreshDevices()
    {
        Devices.Clear();
        var configured = _settings.Current.Audio.InputDeviceId;

        foreach (var device in _audio.GetInputDevices())
        {
            Devices.Add(device);
        }

        SelectedDevice = Devices.FirstOrDefault(d => d.Id == configured)
                         ?? Devices.FirstOrDefault(d => d.IsDefault)
                         ?? Devices.FirstOrDefault();
    }

    private void RefreshProfiles()
    {
        var selectedId = SelectedProfile?.Id ?? _settings.Current.ActiveProfileId;

        Profiles.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            Profiles.Add(profile);
        }

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selectedId) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(PromptProfile? value)
    {
        if (value is null)
        {
            return;
        }

        ProfileName = value.Name;
        ProfilePrompt = value.SystemPrompt;
        ProfileModel = value.Model ?? string.Empty;
        ProfileTemperature = value.Temperature?.ToString("0.##") ?? string.Empty;
        ProfileUsesLlm = value.UsesLlm;
        ProfileMessage = null;
    }

    public string ActiveProfileName => _settings.Current.GetActiveProfile().Name;

    /// <summary>Applies a combination captured by the hotkey box.</summary>
    public void SetHotkey(HotkeyDefinition definition)
    {
        if (!definition.IsValid)
        {
            HotkeyMessage = Strings.SettingsHotkeyNeedsModifier;
            return;
        }

        _hotkey = definition;
        HotkeyText = HotkeyFormatter.Describe(definition);
        HotkeyMessage = null;
    }

    [RelayCommand]
    private void ResetHotkey() => SetHotkey(HotkeyDefinition.Default);

    [RelayCommand]
    private void ToggleMicTest()
    {
        if (_micTestRunning)
        {
            _audio.Cancel();
            _micTestRunning = false;
            IsMicTestRunning = false;
            MicLevel = 0;
            return;
        }

        try
        {
            _audio.Start(SelectedDevice?.Id, TimeSpan.FromSeconds(30));
            _micTestRunning = true;
            IsMicTestRunning = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Microphone test failed");
            MessageBox.Show(Strings.Format("MessageMicrophoneFailedFormat", ex.Message), "VoiceFlow");
        }
    }

    private void OnMicLevel(object? sender, AudioLevelEventArgs e)
    {
        if (!_micTestRunning)
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() => MicLevel = e.Peak);
    }

    [RelayCommand]
    private void BrowseModelDirectory()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = Strings.SettingsModelPickerTitle,
            InitialDirectory = Directory.Exists(ModelDirectory) ? ModelDirectory : AppPaths.Root
        };

        if (dialog.ShowDialog() == true)
        {
            ModelDirectory = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void DownloadModel() => ModelSetupRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ReloadModelAsync()
    {
        await SaveAsync().ConfigureAwait(true);
        await _transcription.ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        try
        {
            IsTestingConnection = true;
            ConnectionMessage = Strings.SettingsTesting;

            // The test has to use what is on screen, so persist first.
            await SaveAsync().ConfigureAwait(true);

            var result = await _llm.TestConnectionAsync().ConfigureAwait(true);

            ConnectionMessage = result.Success
                ? "✓ " + (string.IsNullOrWhiteSpace(result.Detail)
                    ? Strings.ConnectionOk
                    : Strings.Format("ConnectionOkFormat", result.Detail))
                : "✗ " + result.Error;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>Opens the catalogue of the configured endpoint (hundreds of entries on OpenRouter).</summary>
    [RelayCommand]
    private async Task BrowseModelsAsync()
    {
        // The picker talks to the endpoint that is currently saved, not the one on screen.
        await SaveAsync().ConfigureAwait(true);
        ModelBrowserRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void OpenApiKeyPage()
    {
        var url = ApiKeyUrl;

        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open {Url}", url);
        }
    }

    /// <summary>Applies the model the user picked in the catalogue window.</summary>
    public void ApplyPickedModel(string modelId)
    {
        Model = modelId;
        _ = SaveAsync();
    }

    partial void OnBaseUrlChanged(string value)
    {
        SyncProviderFromBaseUrl();
        OnPropertyChanged(nameof(IsOpenRouter));
    }

    /// <summary>Keeps the provider combo in step with a hand-edited base URL.</summary>
    private void SyncProviderFromBaseUrl()
    {
        var kind = LlmProviders.Detect(BaseUrl);
        var option = LlmProviderOptions.FirstOrDefault(o => o.Kind == kind);

        if (option is not null && !ReferenceEquals(option, SelectedLlmProvider))
        {
            _suppressProviderApply = true;
            SelectedLlmProvider = option;
            _suppressProviderApply = false;
        }

        OnPropertyChanged(nameof(ApiKeyUrl));
        OnPropertyChanged(nameof(HasApiKeyUrl));
    }

    partial void OnSelectedLlmProviderChanged(LlmProviderOption? value)
    {
        OnPropertyChanged(nameof(ApiKeyUrl));
        OnPropertyChanged(nameof(HasApiKeyUrl));

        if (_suppressProviderApply || value is null)
        {
            return;
        }

        var preset = LlmProviders.Find(value.Kind);

        if (preset is null)
        {
            return;
        }

        BaseUrl = preset.BaseUrl;

        // Only fill the model when it is empty or still the previous provider suggestion.
        var suggestions = LlmProviders.All
            .Select(p => p.SuggestedModel)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preset.SuggestedModel)
            && (string.IsNullOrWhiteSpace(Model) || suggestions.Contains(Model, StringComparer.OrdinalIgnoreCase)))
        {
            Model = preset.SuggestedModel!;
        }
    }

    partial void OnModelChanged(string value)
    {
        OnPropertyChanged(nameof(GlobalModelName));
    }

    public string GlobalModelName => string.IsNullOrWhiteSpace(Model) ? "gpt-4o-mini" : Model;

    public string GlobalTemperatureText => TemperatureDisplay;

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var edited = SelectedProfile.Clone();
        edited.Name = string.IsNullOrWhiteSpace(ProfileName) ? SelectedProfile.Name : ProfileName.Trim();
        edited.SystemPrompt = ProfilePrompt;
        edited.Model = null; // Inherits the global default AI model configured in the IA tab
        edited.UsesLlm = ProfileUsesLlm;
        edited.Temperature = null; // Inherits the global default temperature configured in the IA tab

        await _profiles.UpdateAsync(edited).ConfigureAwait(true);
        RefreshProfiles();
        ProfileMessage = Strings.SettingsProfileSaved;
    }

    [RelayCommand]
    private void RestoreProfilePrompt()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var original = PromptProfileService.GetDefaultPrompt(SelectedProfile);

        if (original is null)
        {
            ProfileMessage = Strings.SettingsProfileNoDefault;
            return;
        }

        ProfilePrompt = original;
        ProfileMessage = Strings.SettingsProfileRestored;
    }

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        var profile = await _profiles.CreateAsync(Strings.SettingsProfileNewName).ConfigureAwait(true);
        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == profile.Id);
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var copy = await _profiles.DuplicateAsync(SelectedProfile.Id).ConfigureAwait(true);
        RefreshProfiles();
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == copy.Id);
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var answer = MessageBox.Show(
            Strings.Format("SettingsProfileConfirmDeleteFormat", SelectedProfile.Name),
            "VoiceFlow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _profiles.DeleteAsync(SelectedProfile.Id).ConfigureAwait(true);
            RefreshProfiles();
        }
        catch (InvalidOperationException ex)
        {
            ProfileMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task SetActiveProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await _profiles.SetActiveAsync(SelectedProfile.Id).ConfigureAwait(true);
        OnPropertyChanged(nameof(ActiveProfileName));
        ProfileMessage = Strings.Format("SettingsProfileActiveFormat", SelectedProfile.Name);
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var answer = MessageBox.Show(
            Strings.SettingsConfirmClearHistory,
            "VoiceFlow",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await _history.ClearAsync().ConfigureAwait(true);
        HistoryMessage = Strings.HistoryCleared;
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        var current = _settings.Current;
        var hotkeyChanged =
            current.Hotkey.ToDefinition() != _hotkey
            || current.Hotkey.Mode != (IsPushToTalk ? HotkeyMode.PushToTalk : HotkeyMode.Toggle);

        var modelChanged =
            !PathsEqual(current.Stt.ModelDirectory, ModelDirectory)
            || current.Stt.NumThreads != NumThreads
            || current.Stt.Provider != Provider
            || current.Stt.CloudModel != (SelectedCloudModel?.Id ?? CloudSttCatalog.DefaultModelId);

        current.General.StartWithWindows = StartWithWindows;
        current.General.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
        current.General.PlaySounds = PlaySounds;
        current.General.Language = Language;

        current.Hotkey.Apply(_hotkey);
        current.Hotkey.Mode = IsPushToTalk ? HotkeyMode.PushToTalk : HotkeyMode.Toggle;

        current.Audio.InputDeviceId = SelectedDevice?.Id;
        current.Audio.MaxRecordingSeconds = MaxRecordingSeconds;

        current.Stt.ModelDirectory = PathsEqual(ModelDirectory, AppPaths.DefaultModelDirectory) ? null : ModelDirectory;
        current.Stt.NumThreads = NumThreads;
        current.Stt.Provider = Provider;
        current.Stt.CloudModel = SelectedCloudModel?.Id ?? CloudSttCatalog.DefaultModelId;

        current.Llm.BaseUrl = BaseUrl.Trim();
        current.Llm.Model = Model.Trim();
        current.Llm.Temperature = Math.Round(Temperature, 2);
        current.Llm.MaxTokens = MaxTokens;
        current.Llm.TimeoutSeconds = TimeoutSeconds;
        current.Llm.EnableReasoning = EnableReasoning;
        current.Llm.ReasoningEffort = ReasoningEffort;
        _settings.SetApiKey(string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim());

        current.Paste.RestoreClipboard = RestoreClipboard;
        current.Paste.Mode = TypeCharacterByCharacter ? PasteMode.TypeUnicode : PasteMode.Clipboard;
        current.Paste.PasteDelayMilliseconds = PasteDelayMilliseconds;

        current.History.MaxEntries = MaxEntries;
        current.History.MaxAgeDays = MaxAgeDays;
        current.History.SaveAudio = SaveAudio;

        await _settings.SaveAsync().ConfigureAwait(true);
        _startup.Apply(StartWithWindows);

        // Warm up the (possibly new) capture device for the next dictation.
        _audio.Prepare(SelectedDevice?.Id);

        if (hotkeyChanged)
        {
            HotkeyChanged?.Invoke(this, EventArgs.Empty);
        }

        if (modelChanged)
        {
            await _transcription.ReloadAsync().ConfigureAwait(true);
        }
    }

    public void SetHotkeyRegistrationError(string? error) => HotkeyMessage = error;

    private void OnModelStateChanged(object? sender, SttModelState state) =>
        Application.Current?.Dispatcher.BeginInvoke(() => ApplyModelState(state));

    private void ApplyModelState(SttModelState state)
    {
        if (Provider == SttProvider.OpenRouterCloud)
        {
            var hasKey = !string.IsNullOrWhiteSpace(ApiKey);
            var modelName = SelectedCloudModel?.DisplayName ?? (SelectedCloudModel?.Id ?? CloudSttCatalog.DefaultModelId);
            ModelStatus = hasKey
                ? $"Nube lista ({modelName})"
                : "Falta configurar la clave API de OpenRouter en la pestaña IA";
            return;
        }

        var present = _downloader.IsModelPresent(ModelDirectory);

        ModelStatus = state.Status switch
        {
            SttModelStatus.Ready => Strings.ModelStateReady,
            SttModelStatus.Loading => Strings.ModelStateLoading,
            SttModelStatus.Downloading => Strings.ModelStateDownloading,
            SttModelStatus.Failed => Strings.Format("ModelStateFailedFormat", state.Message),
            _ => present ? Strings.ModelStateDownloadedNotLoaded : Strings.ModelStateNotDownloaded
        };
    }

    private static bool PathsEqual(string? left, string? right) =>
        string.Equals(
            (left ?? string.Empty).TrimEnd('\\', '/'),
            (right ?? string.Empty).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_micTestRunning)
        {
            _audio.Cancel();
            _micTestRunning = false;
        }

        _audio.LevelChanged -= OnMicLevel;
        _transcription.StateChanged -= OnModelStateChanged;
    }
}

/// <summary>One entry of the interface-language combo.</summary>
public sealed record LanguageOption(UiLanguage Value, string Display);

/// <summary>One entry of the AI provider combo.</summary>
public sealed record LlmProviderOption(LlmProviderKind Kind, string Display);

/// <summary>One entry of the STT engine provider combo.</summary>
public sealed record SttProviderOption(SttProvider Value, string DisplayName, string Description);
