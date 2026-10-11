using AtomBox.Application.Settings;
using AtomBox.Core.Settings;
using AtomBox.Core.Errors;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Services;
using AtomBox.Infrastructure.Configuration;
using System.Diagnostics;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class SettingsViewModel : LocalizedViewModelBase
{
    private readonly SettingsAppService _settings;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IFilePickerService _filePicker;
    private readonly IDesktopPreferencesService _preferences;
    private readonly IDesktopLanguageService _language;
    private readonly AtomBoxStoragePaths _paths;
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
        IDialogService dialogs,
        IMessageService messages,
        IFilePickerService filePicker,
        IDesktopPreferencesService preferences,
        IAtomBoxLocalizedText localization,
        IDesktopLanguageService language,
        AtomBoxStoragePaths paths)
        : base(localization)
    {
        _settings = settings;
        _dialogs = dialogs;
        _messages = messages;
        _filePicker = filePicker;
        _preferences = preferences;
        _language = language;
        _paths = paths;
        _defaultDownloadDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "AtomBox");
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ResetCommand = new AsyncRelayCommand(_ => ResetAsync(), _ => !IsLoading);
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsLoading && IsDirty);
        ChooseDownloadDirectoryCommand = new AsyncRelayCommand(_ => ChooseDownloadDirectoryAsync());
        OpenConfigurationDirectoryCommand = new AsyncRelayCommand(_ => OpenDirectoryAsync(_paths.ConfigurationDirectory));
        OpenStateDirectoryCommand = new AsyncRelayCommand(_ => OpenDirectoryAsync(_paths.StateDirectory));
        OpenLogDirectoryCommand = new AsyncRelayCommand(_ => OpenDirectoryAsync(_paths.LogDirectory));
        ShowErrorDetailsCommand = new AsyncRelayCommand(_ => ShowErrorDetailsAsync(), _ => HasErrorDetails);
        _ = RefreshAsync();
    }

    public string Title => Localization.GetString("Settings", "Title");

    public int LanguageSelectedIndex
    {
        get => _languageSelectedIndex;
        set
        {
            var normalized = Math.Clamp(value, 0, 2);
            if (SetProperty(ref _languageSelectedIndex, normalized) && !_isApplyingSettings)
            {
                _ = ChangeLanguageAsync(normalized);
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
                SaveCommand.RaiseCanExecuteChanged();
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
                ResetCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasErrorDetails => _lastErrorDetails is not null;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ResetCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand ChooseDownloadDirectoryCommand { get; }

    public AsyncRelayCommand OpenConfigurationDirectoryCommand { get; }

    public AsyncRelayCommand OpenStateDirectoryCommand { get; }

    public AsyncRelayCommand OpenLogDirectoryCommand { get; }

    public AsyncRelayCommand ShowErrorDetailsCommand { get; }

    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusMessage = string.Empty;
        ClearErrorDetails();

        var result = await _settings.GetAsync(new GetApplicationSettingsRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("Settings", "SettingsLoadFailed");
            SettingsSummary = Localization.GetString("Settings", "SettingsUnavailable");
            SetErrorDetails(StatusMessage, StatusMessage, result.Error);
            IsLoading = false;
            return;
        }

        var preferences = await _preferences.GetAsync().ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        StatusMessage = string.Empty;
        IsLoading = false;
    }

    private async Task ResetAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            Localization.GetString("Settings", "ResetTitle"),
            Localization.GetString("Settings", "ResetMessage"),
            Localization.GetString("Common", "Reset"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = Localization.GetString("Settings", "Resetting");
        var result = await _settings.ResetAsync(new ResetApplicationSettingsRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("Settings", "ResetFailed");
            SetErrorDetails(StatusMessage, StatusMessage, result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            IsLoading = false;
            return;
        }

        var preferences = await _preferences.ResetAsync().ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        await _language.SetLanguageAsync(LanguagePreference.System).ConfigureAwait(true);
        _messages.Info(Localization.GetString("Settings", "ResetSucceeded"));
        StatusMessage = Localization.GetString("Settings", "ResetSucceeded");
        IsLoading = false;
    }

    private async Task SaveAsync()
    {
        if (_loadedSettings is null || _loadedPreferences is null)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = Localization.GetString("Settings", "Saving");

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

        var result = await _settings.UpdateAsync(new UpdateApplicationSettingsRequest(settings)).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("Settings", "SaveFailed");
            SetErrorDetails(StatusMessage, StatusMessage, result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            IsLoading = false;
            return;
        }

        await _preferences.SaveAsync(preferences).ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        _messages.Info(Localization.GetString("Settings", "SaveSucceeded"));
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

    private async Task ChooseDownloadDirectoryAsync()
    {
        var selectedPath = await _filePicker
            .PickFolderAsync(Localization.GetString("Settings", "ChooseDownloadDirectory"), DefaultDownloadDirectory)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        DefaultDownloadDirectory = selectedPath;
        StatusMessage = Localization.GetString("Settings", "DownloadDirectorySelected");
    }

    private void MarkDirty()
    {
        if (!_isApplyingSettings)
        {
            IsDirty = true;
        }
    }

    private async Task ChangeLanguageAsync(int selectedIndex)
    {
        var previousIndex = ToLanguageIndex(_language.CurrentPreference);
        _isLanguageSwitching = true;
        OnPropertyChanged(nameof(IsLanguageSelectionEnabled));
        try
        {
            await _language.SetLanguageAsync(ToLanguagePreference(selectedIndex)).ConfigureAwait(true);
            if (_loadedPreferences is not null)
            {
                _loadedPreferences = _loadedPreferences with { Language = _language.CurrentPreference };
            }
        }
        catch
        {
            _isApplyingSettings = true;
            LanguageSelectedIndex = previousIndex;
            _isApplyingSettings = false;
            _messages.Error(Localization.GetString("Settings", "LanguageChangeFailed"));
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

    protected override void OnLanguageChanged()
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

    private Task ShowErrorDetailsAsync()
    {
        return _lastErrorDetails is null
            ? Task.CompletedTask
            : _dialogs.ShowErrorDetailsAsync(_lastErrorDetails);
    }

    private void SetErrorDetails(string title, string fallbackSummary, StorageError? error)
    {
        _lastErrorDetails = ErrorDialogRequest.FromError(title, fallbackSummary, error);
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.RaiseCanExecuteChanged();
    }

    private void ClearErrorDetails()
    {
        if (_lastErrorDetails is null)
        {
            return;
        }

        _lastErrorDetails = null;
        OnPropertyChanged(nameof(HasErrorDetails));
        ShowErrorDetailsCommand.RaiseCanExecuteChanged();
    }

    private async Task OpenDirectoryAsync(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            StatusMessage = Localization.Format("Settings", "DirectoryOpened", path);
        }
        catch
        {
            StatusMessage = Localization.GetString("Settings", "OpenDirectoryFailed");
            SetErrorDetails(
                StatusMessage,
                StatusMessage,
                StorageError.Unknown("Failed to open local directory."));
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
        }
    }
}
