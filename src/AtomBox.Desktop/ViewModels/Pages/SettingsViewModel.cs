using AtomBox.Application.Settings;
using AtomBox.Core.Settings;
using AtomBox.Core.Errors;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Services;
using AtomBox.Infrastructure.Configuration;
using System.Diagnostics;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly SettingsAppService _settings;
    private readonly IDialogService _dialogs;
    private readonly IMessageService _messages;
    private readonly IFilePickerService _filePicker;
    private readonly IDesktopPreferencesService _preferences;
    private readonly AtomBoxStoragePaths _paths;
    private string _statusMessage = string.Empty;
    private string _settingsSummary = string.Empty;
    private string _defaultDownloadDirectory;
    private decimal _defaultConcurrency = 3;
    private int _startupPageSelectedIndex;
    private int _closeBehaviorSelectedIndex;
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
        AtomBoxStoragePaths paths)
    {
        _settings = settings;
        _dialogs = dialogs;
        _messages = messages;
        _filePicker = filePicker;
        _preferences = preferences;
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

    public string Title => "应用设置";

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
            StatusMessage = result.Error?.Message ?? "应用设置加载失败。";
            SettingsSummary = "应用设置暂不可用。";
            SetErrorDetails("应用设置加载失败", "应用设置加载失败。", result.Error);
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
            "重置应用设置",
            "确定要恢复默认应用设置吗？\n\n默认并发、覆盖策略和保留历史等全局偏好将恢复为默认值。",
            "重置",
            "取消")).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = "正在重置应用设置...";
        var result = await _settings.ResetAsync(new ResetApplicationSettingsRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "应用设置重置失败。";
            SetErrorDetails("应用设置重置失败", "应用设置重置失败。", result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            IsLoading = false;
            return;
        }

        var preferences = await _preferences.ResetAsync().ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        _messages.Info("应用设置已重置。");
        StatusMessage = "应用设置已重置。";
        IsLoading = false;
    }

    private async Task SaveAsync()
    {
        if (_loadedSettings is null || _loadedPreferences is null)
        {
            return;
        }

        IsLoading = true;
        StatusMessage = "正在保存应用设置...";

        var settings = new ApplicationSettings(
            (int)DefaultConcurrency,
            _loadedSettings.DefaultOverwritePolicy,
            _loadedSettings.KeepCompletedTransfers);
        var preferences = new DesktopPreferences(
            ToStartupPage(StartupPageSelectedIndex),
            ToCloseBehavior(CloseBehaviorSelectedIndex),
            DefaultDownloadDirectory);

        var result = await _settings.UpdateAsync(new UpdateApplicationSettingsRequest(settings)).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = result.Error?.Message ?? "应用设置保存失败。";
            SetErrorDetails("应用设置保存失败", "应用设置保存失败。", result.Error);
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            IsLoading = false;
            return;
        }

        await _preferences.SaveAsync(preferences).ConfigureAwait(true);
        ApplySettings(result.GetValueOrThrow(), preferences);
        _messages.Info("应用设置已保存。");
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
        SettingsSummary = string.Empty;
        IsDirty = false;
        _isApplyingSettings = false;
    }

    private async Task ChooseDownloadDirectoryAsync()
    {
        var selectedPath = await _filePicker
            .PickFolderAsync("选择默认下载目录", DefaultDownloadDirectory)
            .ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        DefaultDownloadDirectory = selectedPath;
        StatusMessage = "已选择默认下载目录，请点击保存后生效。";
    }

    private void MarkDirty()
    {
        if (!_isApplyingSettings)
        {
            IsDirty = true;
        }
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
            StatusMessage = $"已打开目录：{path}";
        }
        catch
        {
            StatusMessage = "打开目录失败。";
            SetErrorDetails(
                "打开目录失败",
                "打开目录失败。",
                StorageError.Unknown("Failed to open local directory."));
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
        }
    }
}
