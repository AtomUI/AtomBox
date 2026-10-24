using AtomBox.Application.Settings;
using AtomBox.Application.State;
using AtomBox.Core.Settings;
using AtomBox.Core.Errors;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.ViewModels;
using AtomBox.Infrastructure.Configuration;
using System.Diagnostics;
using System.Threading.Channels;
using AtomUI.City.Mvvm;
using AtomUI.City.State;
using CommunityToolkit.Mvvm.Input;
using CityUiDispatcher = AtomUI.City.Core.Threading.IUiDispatcher;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class SettingsViewModel : DesktopViewModelBase
{
    private readonly SettingsAppService _settings;
    private readonly IAtomBoxApplicationState _applicationState;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IFilePickerService _filePicker;
    private readonly IDesktopPreferencesService _preferences;
    private readonly IDesktopLanguageService _language;
    private readonly AtomBoxStoragePaths _paths;
    private readonly IAtomBoxLocalizedText _localization;
    private readonly CityUiDispatcher _dispatcher;
    private Channel<int>? _languageChanges;
    private Task? _languageWorker;
    private string _statusMessage = string.Empty;
    private string _settingsSummary = string.Empty;
    private string _defaultDownloadDirectory;
    private decimal _defaultConcurrency = 3;
    private int _startupPageSelectedIndex;
    private int _closeBehaviorSelectedIndex;
    private int _languageSelectedIndex;
    private bool _isLanguageSwitching;
    private bool _enableUploadFingerprintIndex;
    private ApplicationSettings? _loadedSettings;
    private DesktopPreferences? _loadedPreferences;
    private bool _isLoading;
    private bool _isDirty;
    private bool _isApplyingSettings;
    private ErrorDialogRequest? _lastErrorDetails;

    public SettingsViewModel(
        SettingsAppService settings,
        IAtomBoxApplicationState applicationState,
        IDialogService dialogs,
        IMessageService messages,
        IFilePickerService filePicker,
        IDesktopPreferencesService preferences,
        IAtomBoxLocalizedText localization,
        IDesktopLanguageService language,
        AtomBoxStoragePaths paths,
        DesktopCommandFactory commands,
        CityUiDispatcher dispatcher)
    {
        _settings = settings;
        _applicationState = applicationState;
        _dialogs = dialogs;
        _messages = messages;
        _filePicker = filePicker;
        _preferences = preferences;
        _language = language;
        _paths = paths;
        _localization = localization;
        _dispatcher = dispatcher;
        _defaultDownloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "AtomBox");
        RefreshCommand = commands.CreateAsync(this, nameof(RefreshCommand), RefreshAsync);
        ResetCommand = commands.CreateAsync(this, nameof(ResetCommand), ResetAsync, () => !IsLoading);
        SaveCommand = commands.CreateAsync(this, nameof(SaveCommand), SaveAsync, () => !IsLoading && IsDirty);
        ChooseDownloadDirectoryCommand = commands.CreateAsync(
            this,
            nameof(ChooseDownloadDirectoryCommand),
            ChooseDownloadDirectoryAsync);
        OpenConfigurationDirectoryCommand = commands.CreateAsync(
            this,
            nameof(OpenConfigurationDirectoryCommand),
            cancellationToken => OpenDirectoryAsync(_paths.ConfigurationDirectory, cancellationToken));
        OpenStateDirectoryCommand = commands.CreateAsync(
            this,
            nameof(OpenStateDirectoryCommand),
            cancellationToken => OpenDirectoryAsync(_paths.StateDirectory, cancellationToken));
        OpenLogDirectoryCommand = commands.CreateAsync(
            this,
            nameof(OpenLogDirectoryCommand),
            cancellationToken => OpenDirectoryAsync(_paths.LogDirectory, cancellationToken));
        ShowErrorDetailsCommand = commands.CreateAsync(
            this,
            nameof(ShowErrorDetailsCommand),
            ShowErrorDetailsAsync,
            () => HasErrorDetails);
    }

    public string Title => _localization.GetString("Settings", "Title");

    public int LanguageSelectedIndex
    {
        get => _languageSelectedIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (SetProperty(ref _languageSelectedIndex, normalized) && !_isApplyingSettings)
            {
                _languageChanges?.Writer.TryWrite(normalized);
            }
        }
    }

    public bool IsLanguageSelectionEnabled => !_isLanguageSwitching;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string SettingsSummary
    {
        get => _settingsSummary;
        private set => SetProperty(ref _settingsSummary, value);
    }

    public string DefaultDownloadDirectory
    {
        get => _defaultDownloadDirectory;
        private set
        {
            if (SetProperty(ref _defaultDownloadDirectory, value))
            {
                MarkDirty();
            }
        }
    }

    public decimal DefaultConcurrency
    {
        get => _defaultConcurrency;
        set
        {
            var normalizedValue = Math.Clamp(decimal.Round(value, 0, MidpointRounding.AwayFromZero), 1, 16);
            if (SetProperty(ref _defaultConcurrency, normalizedValue))
            {
                MarkDirty();
            }
        }
    }

    public int StartupPageSelectedIndex
    {
        get => _startupPageSelectedIndex;
        set
        {
            if (SetProperty(ref _startupPageSelectedIndex, Math.Clamp(value, 0, 3)))
            {
                MarkDirty();
            }
        }
    }

    public int CloseBehaviorSelectedIndex
    {
        get => _closeBehaviorSelectedIndex;
        set
        {
            if (SetProperty(ref _closeBehaviorSelectedIndex, Math.Clamp(value, 0, 1)))
            {
                MarkDirty();
            }
        }
    }

    public bool EnableUploadFingerprintIndex
    {
        get => _enableUploadFingerprintIndex;
        set
        {
            if (SetProperty(ref _enableUploadFingerprintIndex, value))
            {
                MarkDirty();
            }
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                ResetCommand.NotifyCanExecuteChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasErrorDetails => _lastErrorDetails is not null;

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand ResetCommand { get; }

    public IAsyncRelayCommand SaveCommand { get; }

    public IAsyncRelayCommand ChooseDownloadDirectoryCommand { get; }

    public IAsyncRelayCommand OpenConfigurationDirectoryCommand { get; }

    public IAsyncRelayCommand OpenStateDirectoryCommand { get; }

    public IAsyncRelayCommand OpenLogDirectoryCommand { get; }

    public IAsyncRelayCommand ShowErrorDetailsCommand { get; }

    protected override ValueTask OnActivatedAsync(
        ActivationContext context,
        CancellationToken cancellationToken)
    {
        _localization.LanguageChanged += HandleLanguageChanged;
        context.Scope.Add(EventSubscription.Create(
            () => _localization.LanguageChanged -= HandleLanguageChanged));
        context.Scope.Add(_applicationState.Settings.Observe(
            args => ApplySettingsState(args.NewValue),
            StateSubscriptionOptions.Dispatcher(_dispatcher)));

        return _dispatcher.PostAsync(
            async token =>
            {
                var changes = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true,
                    SingleWriter = false,
                });
                _languageChanges = changes;
                context.Scope.Add(EventSubscription.Create(
                    () => ReleaseLanguageChanges(changes)));
                _languageWorker = context.Scope.StartUniqueAsync(
                    "settings-language-change",
                    workerToken => RunLanguageChangeWorkerAsync(changes.Reader, workerToken),
                    token);

                await RefreshAsync(token).ConfigureAwait(true);
            },
            cancellationToken);
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _languageChanges, null)?.Writer.TryComplete();
        _languageWorker = null;
        return ValueTask.CompletedTask;
    }

    private void ReleaseLanguageChanges(Channel<int> changes)
    {
        Interlocked.CompareExchange(ref _languageChanges, null, changes);
        changes.Writer.TryComplete();
        _languageWorker = null;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        ClearErrorDetails();

        var preferences = await _preferences.GetAsync(cancellationToken).ConfigureAwait(true);
        _loadedPreferences = preferences;
        var state = _applicationState.Settings.Value;
        if (state.Status != ApplicationProjectionStatus.Ready || state.Settings is null)
        {
            StatusMessage = _localization.GetString("Settings", "SettingsLoadFailed");
            SettingsSummary = _localization.GetString("Settings", "SettingsUnavailable");
            SetErrorDetails(
                StatusMessage,
                StatusMessage,
                StorageError.Unknown("Application settings state is unavailable."));
            IsLoading = false;
            return;
        }

        ApplySettings(new ApplicationSettingsResult(state.Settings), preferences);
        StatusMessage = string.Empty;
        IsLoading = false;
    }

    private void ApplySettingsState(ApplicationSettingsState state)
    {
        if (state.Status == ApplicationProjectionStatus.Uninitialized)
        {
            IsLoading = true;
            return;
        }

        if (state.Status == ApplicationProjectionStatus.Faulted || state.Settings is null)
        {
            StatusMessage = _localization.GetString("Settings", "SettingsLoadFailed");
            SettingsSummary = _localization.GetString("Settings", "SettingsUnavailable");
            IsLoading = false;
            return;
        }

        if (_loadedPreferences is not null)
        {
            ApplySettings(new ApplicationSettingsResult(state.Settings), _loadedPreferences);
            StatusMessage = string.Empty;
            IsLoading = false;
        }
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            _localization.GetString("Settings", "ResetTitle"),
            _localization.GetString("Settings", "ResetMessage"),
            _localization.GetString("Common", "Reset"),
            _localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = _localization.GetString("Settings", "Resetting");
        var result = await _settings.ResetAsync(new ResetApplicationSettingsRequest(), cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = _localization.GetString("Settings", "ResetFailed");
            SetErrorDetails(StatusMessage, StatusMessage, result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            IsLoading = false;
            return;
        }

        var preferences = await _preferences.ResetAsync(cancellationToken).ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        await _language.SetLanguageAsync(LanguagePreference.System, cancellationToken).ConfigureAwait(true);
        _messages.Info(_localization.GetString("Settings", "ResetSucceeded"));
        StatusMessage = _localization.GetString("Settings", "ResetSucceeded");
        IsLoading = false;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (_loadedSettings is null || _loadedPreferences is null)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = _localization.GetString("Settings", "Saving");

        var settings = new ApplicationSettings(
            (int)DefaultConcurrency,
            _loadedSettings.DefaultOverwritePolicy,
            _loadedSettings.KeepCompletedTransfers,
            EnableUploadFingerprintIndex);
        var preferences = new DesktopPreferences(
            ToStartupPage(StartupPageSelectedIndex),
            ToCloseBehavior(CloseBehaviorSelectedIndex),
            DefaultDownloadDirectory,
            _loadedPreferences.Language);

        var result = await _settings.UpdateAsync(new UpdateApplicationSettingsRequest(settings), cancellationToken).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = _localization.GetString("Settings", "SaveFailed");
            SetErrorDetails(StatusMessage, StatusMessage, result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            IsLoading = false;
            return;
        }

        await _preferences.SaveAsync(preferences, cancellationToken).ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        _messages.Info(_localization.GetString("Settings", "SaveSucceeded"));
        StatusMessage = string.Empty;
        IsLoading = false;
    }

    private void ApplySettings(ApplicationSettingsResult result, DesktopPreferences preferences)
    {
        _isApplyingSettings = true;
        _loadedSettings = result.Settings;
        _loadedPreferences = preferences;
        DefaultConcurrency = result.Settings.DefaultConcurrency;
        DefaultDownloadDirectory = preferences.DefaultDownloadDirectory;
        StartupPageSelectedIndex = ToStartupPageIndex(preferences.StartupPage);
        CloseBehaviorSelectedIndex = ToCloseBehaviorIndex(preferences.CloseWindowBehavior);
        EnableUploadFingerprintIndex = result.Settings.EnableUploadFingerprintIndex;
        LanguageSelectedIndex = ToLanguageIndex(preferences.Language);
        SettingsSummary = string.Empty;
        IsDirty = false;
        _isApplyingSettings = false;
    }

    private async Task ChooseDownloadDirectoryAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selectedPath = await _filePicker
            .PickFolderAsync(_localization.GetString("Settings", "ChooseDownloadDirectory"), DefaultDownloadDirectory)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        DefaultDownloadDirectory = selectedPath;
        StatusMessage = _localization.GetString("Settings", "DownloadDirectorySelected");
    }

    private void MarkDirty()
    {
        if (!_isApplyingSettings)
        {
            IsDirty = true;
        }
    }

    private async Task RunLanguageChangeWorkerAsync(
        ChannelReader<int> changes,
        CancellationToken cancellationToken)
    {
        await foreach (var selectedIndex in changes.ReadAllAsync(cancellationToken).ConfigureAwait(true))
        {
            var latest = selectedIndex;
            while (changes.TryRead(out var next))
            {
                latest = next;
            }

            await ChangeLanguageAsync(latest, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task ChangeLanguageAsync(int selectedIndex, CancellationToken cancellationToken)
    {
        var previousIndex = ToLanguageIndex(_language.CurrentPreference);
        _isLanguageSwitching = true;
        OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
        try
        {
            await _language.SetLanguageAsync(ToLanguagePreference(selectedIndex), cancellationToken).ConfigureAwait(true);
            if (_loadedPreferences is not null)
            {
                _loadedPreferences = _loadedPreferences with { Language = _language.CurrentPreference };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            _isApplyingSettings = true;
            LanguageSelectedIndex = previousIndex;
            _isApplyingSettings = false;
            _messages.Error(_localization.GetString("Settings", "LanguageChangeFailed"));
        }
        finally
        {
            _isLanguageSwitching = false;
            OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
        }
    }

    private static int ToLanguageIndex(string preference)
    {
        return LanguagePreference.Normalize(preference) switch
        {
            LanguagePreference.SimplifiedChinese => 1,
            LanguagePreference.English => 2,
            _ => 0
        };
    }

    private static string ToLanguagePreference(int selectedIndex)
    {
        return selectedIndex switch
        {
            1 => LanguagePreference.SimplifiedChinese,
            2 => LanguagePreference.English,
            _ => LanguagePreference.System
        };
    }

    private void HandleLanguageChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Title));
    }

    private static int ToStartupPageIndex(StartupPageOption startupPage)
    {
        return startupPage switch
        {
            StartupPageOption.Home => 0,
            StartupPageOption.RemoteStorage => 1,
            StartupPageOption.TransferQueue => 2,
            StartupPageOption.AccountManagement => 3,
            _ => 1
        };
    }

    private static StartupPageOption ToStartupPage(int selectedIndex)
    {
        return selectedIndex switch
        {
            0 => StartupPageOption.Home,
            2 => StartupPageOption.TransferQueue,
            3 => StartupPageOption.AccountManagement,
            _ => StartupPageOption.RemoteStorage
        };
    }

    private static int ToCloseBehaviorIndex(CloseWindowBehavior behavior)
    {
        return behavior == CloseWindowBehavior.MinimizeApplication ? 1 : 0;
    }

    private static CloseWindowBehavior ToCloseBehavior(int selectedIndex)
    {
        return selectedIndex == 1 ? CloseWindowBehavior.MinimizeApplication : CloseWindowBehavior.CloseApplication;
    }

    private Task ShowErrorDetailsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _lastErrorDetails is null
            ? Task.CompletedTask
            : _dialogs.ShowErrorDetailsAsync(_lastErrorDetails);
    }

    private void SetErrorDetails(string title, string fallbackSummary, StorageError? error)
    {
        _lastErrorDetails = ErrorDialogRequest.FromError(title, fallbackSummary, error);
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.NotifyCanExecuteChanged();
    }

    private void ClearErrorDetails()
    {
        if (_lastErrorDetails is null)
        {
            return;
        }

        _lastErrorDetails = null;
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.NotifyCanExecuteChanged();
    }

    private async Task OpenDirectoryAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            StatusMessage = _localization.Format("Settings", "DirectoryOpened", path);
        }
        catch
        {
            StatusMessage = _localization.GetString("Settings", "OpenDirectoryFailed");
            SetErrorDetails(
                StatusMessage,
                StatusMessage,
                StorageError.Unknown("Failed to open local directory."));
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
        }
    }
}
