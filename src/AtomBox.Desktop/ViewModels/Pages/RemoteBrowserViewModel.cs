using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using AtomBox.Application.Accounts;
using AtomBox.Application.Browsing;
using AtomBox.Application.Transfers;
using AtomBox.Core.Accounts;
using AtomBox.Core.Errors;
using AtomBox.Core.Previews;
using AtomBox.Core.RemoteItems;
using AtomBox.Core.Results;
using AtomBox.Core.Settings;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomUI.Controls;
using AtomUI.Desktop.Controls;
using AtomUI.Icons.AntDesign;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AtomBox.Desktop.ViewModels.Pages;

public sealed class RemoteBrowserViewModel : LocalizedViewModelBase
{
    private readonly AccountAppService _accounts;
    private readonly RemoteBrowserAppService _browser;
    private readonly TransferAppService _transfers;
    private readonly IMessageService _messages;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly IFilePickerService _filePicker;
    private readonly IDesktopPreferencesService _preferences;
    private readonly IAccountDialogWorkflow _accountDialogWorkflow;
    private readonly StatusBarViewModel _statusBar;
    private readonly EventHandler _accountsChangedHandler;
    private string _title = string.Empty;
    private string _statusMessage = string.Empty;
    private string _currentPath = "/";
    private bool _isLoading;
    private int _loadingDepth;
    private bool _isAccountSelection;
    private RemoteItemRowViewModel? _selectedItem;
    private RemoteListRowViewModel? _selectedRow;
    private RemoteAccountChoiceViewModel? _selectedAccount;
    private BucketChoiceViewModel? _selectedBucket;
    private StorageAccountSummary? _currentAccount;
    private NavigationResourceGroup _resourceGroup = NavigationResourceGroup.ObjectStorage;
    private StorageAccountId? _storageAccountId;
    private RemotePath _remotePath = RemotePath.Root;
    private RemotePageCursor? _previousCursor;
    private RemotePageCursor? _nextCursor;
    private RemotePageCursor? _requestedCursor;
    private RemotePageCursor? _currentCursor;
    private readonly Stack<RemotePageCursor?> _previousPageCursors = new();
    private RemotePathContextResult _pathContext = RemotePathContextResult.From(RemotePath.Root);
    private ListRemoteItemsRequest? _lastFailedListRequest;
    private ErrorDialogRequest? _lastErrorDetails;
    private bool _isSyncingBucketSelection;
    private bool _isUpdatingFileSelection;
    private string _searchText = string.Empty;
    private string _appliedSearchText = string.Empty;
    private bool _restoreListAfterSearchLoad;
    private bool _isUploadPreparing;

    public RemoteBrowserViewModel(
        AccountAppService accounts,
        RemoteBrowserAppService browser,
        TransferAppService transfers,
        IMessageService messages,
        IDialogService dialogs,
        INavigationService navigation,
        IFilePickerService filePicker,
        IDesktopPreferencesService preferences,
        IAccountDialogWorkflow accountDialogWorkflow,
        StatusBarViewModel statusBar,
        ILocalizationService localization)
        : base(localization)
    {
        _accounts = accounts;
        _browser = browser;
        _transfers = transfers;
        _messages = messages;
        _dialogs = dialogs;
        _navigation = navigation;
        _filePicker = filePicker;
        _preferences = preferences;
        _accountDialogWorkflow = accountDialogWorkflow;
        _statusBar = statusBar;
        _title = Localization.GetString("RemoteBrowser", "Title");
        _statusMessage = Localization.GetString("RemoteBrowser", "SelectTypeOrConnection");
        _accountsChangedHandler = (_, _) => RefreshAccountsOnUiThread();
        _accountDialogWorkflow.AccountsChanged += _accountsChangedHandler;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        RetryLoadCommand = new AsyncRelayCommand(_ => RetryLoadAsync(), _ => _lastFailedListRequest is not null);
        PreviousPageCommand = new AsyncRelayCommand(_ => LoadPreviousPageAsync(), _ => CanGoPreviousPage);
        NextPageCommand = new AsyncRelayCommand(_ => LoadNextPageAsync(), _ => CanGoNextPage);
        AddAccountCommand = new AsyncRelayCommand(_ => AddAccountAsync());
        OpenAccountCommand = new AsyncRelayCommand(OpenAccountAsync, parameter => parameter is RemoteAccountChoiceViewModel);
        OpenRowCommand = new AsyncRelayCommand(OpenRowAsync);
        UploadCommand = new AsyncRelayCommand(_ => UploadAsync(), _ => CanUpload);
        CreateBucketCommand = new AsyncRelayCommand(_ => CreateBucketAsync(), _ => CanCreateBucket);
        CreateFolderCommand = new AsyncRelayCommand(_ => CreateFolderAsync(), _ => CanCreateFolder);
        DownloadSelectedCommand = new AsyncRelayCommand(
            DownloadSelectedAsync,
            parameter => TryGetFileRow(parameter, out _, out _));
        PreviewSelectedCommand = new AsyncRelayCommand(
            PreviewSelectedAsync,
            parameter => TryGetPreviewableRow(parameter, out _, out _, out _, out _));
        RenameSelectedCommand = new AsyncRelayCommand(
            RenameSelectedAsync,
            parameter => TryGetRenameableRow(parameter, out _, out _, out _));
        DeleteSelectedCommand = new AsyncRelayCommand(
            DeleteSelectedAsync,
            parameter => _storageAccountId is not null &&
                !IsLoading &&
                TryGetDeletableRow(parameter, out _, out _, out _));
        DownloadSelectedFilesCommand = new AsyncRelayCommand(
            _ => DownloadSelectedFilesAsync(),
            _ => _storageAccountId is not null && !IsLoading && HasSelectedFiles);
        DeleteSelectedFilesCommand = new AsyncRelayCommand(
            _ => DeleteSelectedFilesAsync(),
            _ => _storageAccountId is not null && !IsLoading && CanDeleteSelectedFiles);
        ClearSelectedFilesCommand = new RelayCommand(
            _ => ClearSelectedFiles(),
            _ => HasSelectedFiles);
        ShowErrorDetailsCommand = new AsyncRelayCommand(_ => ShowErrorDetailsAsync(), _ => HasErrorDetails);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentPath
    {
        get => _currentPath;
        private set => SetProperty(ref _currentPath, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmptyStateVisible));
                OnPropertyChanged(nameof(EmptyStateText));
                OnPropertyChanged(nameof(IsAddAccountVisible));
                OnPropertyChanged(nameof(IsUploadVisible));
                OnPropertyChanged(nameof(IsFileListActionsVisible));
            OnPropertyChanged(nameof(IsUploadActionVisible));
            OnPropertyChanged(nameof(IsCreateFolderActionVisible));
            OnPropertyChanged(nameof(IsBucketListActionsVisible));
                OnPropertyChanged(nameof(IsRemoteEntryEmptyVisible));
                OnPropertyChanged(nameof(IsRemoteAccountOverviewVisible));
                OnPropertyChanged(nameof(IsRemoteFileBrowserVisible));
                OnPropertyChanged(nameof(IsRemoteEntryAddAccountVisible));
                OnPropertyChanged(nameof(IsRemoteEntryActionsVisible));
                OnPropertyChanged(nameof(CanCreateBucket));
                OnPropertyChanged(nameof(CanCreateFolder));
                RaiseUploadStateChanged();
                RaiseFileSelectionStateChanged();
            }
        }
    }

    public bool IsAccountSelection
    {
        get => _isAccountSelection;
        private set => SetProperty(ref _isAccountSelection, value);
    }

    public bool IsItemList => !IsAccountSelection;

    public bool HasItems => Rows.Count > 0;

    public bool HasAccounts => Accounts.Count > 0;

    public bool HasObjectStorageAccounts => ObjectStorageAccounts.Count > 0;

    public bool HasFileTransferAccounts => FileTransferAccounts.Count > 0;

    public bool HasWebDavAccounts => WebDavAccounts.Count > 0;

    public int TotalAccountCount => Accounts.Count;

    public int ObjectStorageAccountCount => ObjectStorageAccounts.Count;

    public int FileTransferAccountCount => FileTransferAccounts.Count;

    public int WebDavAccountCount => WebDavAccounts.Count;

    public string SelectedFileCountText => Localization.Format(
        "RemoteBrowser",
        "SelectedFiles",
        SelectedFileCount);

    protected override void OnLanguageChanged()
    {
        UpdateTitleFromCurrentAccount();
        OnPropertyChanged(nameof(SelectedFileCountText));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(NameColumnHeader));
    }

    public override void Dispose()
    {
        _accountDialogWorkflow.AccountsChanged -= _accountsChangedHandler;
        base.Dispose();
    }

    public bool HasBuckets => Buckets.Count > 0;

    public bool IsEmptyStateVisible => !IsLoading &&
        ((IsAccountSelection && !HasAccounts) ||
        (!IsAccountSelection && IsBucketList && !HasBuckets) ||
        (!IsAccountSelection && !IsBucketList && !HasItems));

    public string EmptyStateText
    {
        get
        {
            if (IsAccountSelection && !HasAccounts)
            {
                return Localization.GetString("RemoteBrowser", "NoRemoteConnectionsPeriod");
            }

            if (!IsAccountSelection && !HasItems)
            {
                if (_storageAccountId is null)
                {
                    return Localization.GetString("RemoteBrowser", "SelectConnection");
                }

                return Localization.GetString("RemoteBrowser", IsBucketList ? "NoBuckets" : "EmptyDirectory");
            }

            return string.Empty;
        }
    }

    public bool CanUpload =>
        _storageAccountId is not null &&
        !IsLoading &&
        !IsUploadPreparing &&
        !IsAccountSelection &&
        !IsJianguoyunVirtualRoot() &&
        (_pathContext.CanUpload || CanWriteAtCurrentRoot() || CanWriteAtCurrentFileTransferLocation());

    public bool IsAddAccountVisible => false;

    public bool IsRemoteEntryEmptyVisible => !IsLoading && IsAccountSelection && !HasAccounts;

    public bool IsRemoteAccountOverviewVisible => !IsLoading && IsAccountSelection && HasAccounts;

    public bool IsRemoteFileBrowserVisible => !IsAccountSelection;

    public bool IsRemoteEntryAddAccountVisible => IsRemoteEntryEmptyVisible;

    public bool IsRemoteEntryActionsVisible => !IsLoading && IsAccountSelection && HasAccounts;

    public bool IsUploadVisible => !IsAccountSelection;

    public bool IsFileListActionsVisible => !IsLoading && !IsAccountSelection && !IsBucketList;

    public bool IsUploadActionVisible => IsFileListActionsVisible && !IsJianguoyunVirtualRoot();

    public bool IsCreateFolderActionVisible => IsFileListActionsVisible && !IsJianguoyunVirtualRoot();

    public bool IsBucketListActionsVisible => !IsLoading && !IsAccountSelection && IsBucketList;

    public bool CanCreateBucket =>
        _storageAccountId is not null &&
        !IsLoading &&
        IsBucketList;

    public bool CanCreateFolder =>
        _storageAccountId is not null &&
        !IsLoading &&
        !IsAccountSelection &&
        !IsBucketList &&
        !IsJianguoyunVirtualRoot() &&
        (!_remotePath.IsRoot || CanWriteAtCurrentRoot() || CanWriteAtCurrentFileTransferLocation());

    public bool CanGoPreviousPage => !IsLoading && _previousPageCursors.Count > 0;

    public bool CanGoNextPage => !IsLoading && _nextCursor is not null;

    public bool IsPaginationVisible =>
        _currentAccount is not null &&
        (_currentAccount.ProviderCategory == StorageProviderCategory.ObjectStorage || IsWebDav(_currentAccount)) &&
        !IsAccountSelection;

    public bool HasErrorDetails => _lastErrorDetails is not null;

    public bool IsStatusBarMessageVisible => !HasErrorDetails;

    public bool IsBucketSelectorVisible =>
        _currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage &&
        _storageAccountId is not null &&
        !IsAccountSelection;

    public bool IsBreadcrumbVisible =>
        _storageAccountId is not null &&
        !IsAccountSelection &&
        !IsBucketList &&
        (!_remotePath.IsRoot || IsWebDavBreadcrumbRootVisible());

    public bool IsSearchVisible =>
        _currentAccount is not null &&
        (_currentAccount.ProviderCategory == StorageProviderCategory.ObjectStorage || IsWebDav(_currentAccount)) &&
        _storageAccountId is not null &&
        !IsAccountSelection &&
        !IsBucketList;

    public bool IsObjectStorageNavigationVisible => IsBucketSelectorVisible || IsSearchVisible;

    public bool IsObjectStorageBreadcrumbInSearchNavigationVisible =>
        IsBreadcrumbVisible &&
        _currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage;

    public bool IsWebDavBreadcrumbInSearchNavigationVisible =>
        IsBreadcrumbVisible &&
        _currentAccount is not null &&
        IsWebDav(_currentAccount);

    public bool IsPlainBreadcrumbNavigationVisible =>
        IsBreadcrumbVisible &&
        !IsObjectStorageNavigationVisible &&
        _currentAccount?.ProviderCategory != StorageProviderCategory.ObjectStorage;

    public bool IsRemoteNavigationVisible => IsObjectStorageNavigationVisible || IsPlainBreadcrumbNavigationVisible;

    public string NameColumnHeader => IsBucketList
        ? Localization.GetString("RemoteBrowser", "BucketName")
        : Localization.GetString("Common", "FileName");

    public bool IsBucketList =>
        _currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage &&
        _pathContext.IsRoot;

    public bool IsDataRowsVisible => !IsAccountSelection;

    public bool IsUploadPreparing
    {
        get => _isUploadPreparing;
        private set
        {
            if (SetProperty(ref _isUploadPreparing, value))
            {
                RaiseUploadStateChanged();
            }
        }
    }

    public ObservableCollection<RemoteItemRowViewModel> Items { get; } = [];

    public ObservableCollection<RemoteListRowViewModel> Rows { get; } = [];

    public ObservableCollection<RemoteAccountChoiceViewModel> Accounts { get; } = [];

    public ObservableCollection<RemoteAccountChoiceViewModel> ObjectStorageAccounts { get; } = [];

    public ObservableCollection<RemoteAccountChoiceViewModel> FileTransferAccounts { get; } = [];

    public ObservableCollection<RemoteAccountChoiceViewModel> WebDavAccounts { get; } = [];

    public ObservableCollection<BucketChoiceViewModel> Buckets { get; } = [];

    public ObservableCollection<BreadcrumbItemData> BreadcrumbItems { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value ?? string.Empty);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RetryLoadCommand { get; }

    public AsyncRelayCommand PreviousPageCommand { get; }

    public AsyncRelayCommand NextPageCommand { get; }

    public AsyncRelayCommand AddAccountCommand { get; }

    public AsyncRelayCommand OpenAccountCommand { get; }

    public AsyncRelayCommand OpenRowCommand { get; }

    public AsyncRelayCommand UploadCommand { get; }

    public AsyncRelayCommand CreateBucketCommand { get; }

    public AsyncRelayCommand CreateFolderCommand { get; }

    public AsyncRelayCommand DownloadSelectedCommand { get; }

    public AsyncRelayCommand PreviewSelectedCommand { get; }

    public AsyncRelayCommand RenameSelectedCommand { get; }

    public AsyncRelayCommand DeleteSelectedCommand { get; }

    public AsyncRelayCommand DownloadSelectedFilesCommand { get; }

    public AsyncRelayCommand DeleteSelectedFilesCommand { get; }

    public RelayCommand ClearSelectedFilesCommand { get; }

    public AsyncRelayCommand ShowErrorDetailsCommand { get; }

    public int SelectedFileCount => Rows.Count(row => row.IsFile && row.IsSelected);

    public bool HasSelectedFiles => SelectedFileCount > 0;

    public bool IsBatchSelectionEnabled => RemoteBrowserFeatureFlags.BatchFileSelectionEnabled;

    public GridLength BatchSelectionColumnWidth =>
        IsBatchSelectionEnabled ? new GridLength(44) : new GridLength(0);

    public bool IsBatchSelectionBarVisible => IsBatchSelectionEnabled && HasSelectedFiles;

    public bool HasSelectableFiles => Rows.Any(row => row.IsFile);

    public bool CanDeleteSelectedFiles =>
        HasSelectedFiles &&
        Rows.Where(row => row.IsFile && row.IsSelected).All(row => row.CanDelete);

    public bool? AllFilesSelectionState
    {
        get
        {
            var files = Rows.Where(row => row.IsFile).ToArray();
            if (files.Length == 0 || files.All(row => !row.IsSelected))
            {
                return false;
            }

            return files.All(row => row.IsSelected) ? true : null;
        }
        set
        {
            if (_isUpdatingFileSelection)
            {
                return;
            }

            SetAllFilesSelected(value == true);
        }
    }

    public RemoteItemRowViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value) &&
                value is { Kind: RemoteItemKind.Bucket } &&
                IsBucketList)
            {
                _remotePath = value.Path;
                ResetPaging();
                SyncSelectedBucketForPath(_remotePath);
                _ = LoadItemsAsync();
            }
        }
    }

    public RemoteListRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => SetProperty(ref _selectedRow, value);
    }

    private Task OpenRowAsync(object? parameter)
    {
        if (parameter is not RemoteListRowViewModel row)
        {
            return Task.CompletedTask;
        }

        if ((row.Kind == RemoteItemKind.Bucket && IsBucketList) ||
            row.Kind == RemoteItemKind.Folder)
        {
            _remotePath = row.Path;
            ResetPaging();
            ClearSearch();
            SyncSelectedBucketForPath(_remotePath);

            return LoadItemsAsync();
        }

        if (row is { Kind: RemoteItemKind.File, CanPreview: true })
        {
            return PreviewSelectedAsync(row);
        }

        SelectedItem = Items.FirstOrDefault(item => item.Path == row.Path);
        return Task.CompletedTask;
    }

    public RemoteAccountChoiceViewModel? SelectedAccount
    {
        get => _selectedAccount;
        set => SetProperty(ref _selectedAccount, value);
    }

    public BucketChoiceViewModel? SelectedBucket
    {
        get => _selectedBucket;
        set
        {
            if (!SetProperty(ref _selectedBucket, value) || _isSyncingBucketSelection)
            {
                return;
            }

            _remotePath = value?.Path ?? RemotePath.Root;
            ResetPaging();
            ClearSearch();
            _ = LoadItemsAsync();
        }
    }

    public void ApplyNavigationParameter(RemoteBrowserNavigationParameter parameter)
    {
        _resourceGroup = parameter.ResourceGroup;
        _storageAccountId = parameter.StorageAccountId;
        _remotePath = parameter.RemotePath ?? RemotePath.Root;
        _currentAccount = null;
        Buckets.Clear();
        SyncSelectedBucket(null);
        ResetPaging();
        ClearSearch();
        RaiseUploadStateChanged();

        Title = parameter.ResourceGroup switch
        {
            NavigationResourceGroup.All => Localization.GetString("Shell", "RemoteStorage"),
            NavigationResourceGroup.ObjectStorage => "OSS",
            NavigationResourceGroup.FileTransfer => "FTP / SFTP",
            NavigationResourceGroup.WebDav => "WebDAV",
            _ => Localization.GetString("RemoteBrowser", "Title")
        };

        _ = RefreshAsync();
    }

    private Task RefreshAsync()
    {
        ResetPaging();
        return _storageAccountId is null
            ? LoadEntryAsync()
            : LoadItemsAsync();
    }

    private Task RetryLoadAsync()
    {
        if (_lastFailedListRequest is null)
        {
            return Task.CompletedTask;
        }

        _remotePath = _lastFailedListRequest.Path;
        _requestedCursor = _lastFailedListRequest.PageRequest?.Cursor;
        return LoadItemsAsync();
    }

    private Task LoadNextPageAsync()
    {
        if (_nextCursor is null)
        {
            return Task.CompletedTask;
        }

        _previousPageCursors.Push(_currentCursor);
        _requestedCursor = _nextCursor;
        return LoadItemsAsync();
    }

    private Task LoadPreviousPageAsync()
    {
        if (_previousPageCursors.Count == 0)
        {
            return Task.CompletedTask;
        }

        _requestedCursor = _previousPageCursors.Pop();
        return LoadItemsAsync();
    }

    private Task LoadEntryAsync()
    {
        return RunWithLoadingAsync(LoadEntryCoreAsync);
    }

    private async Task LoadEntryCoreAsync()
    {
        SetAccountSelection(false);
        Items.Clear();
        Rows.Clear();
        RaiseFileSelectionStateChanged();
        Accounts.Clear();
        ObjectStorageAccounts.Clear();
        FileTransferAccounts.Clear();
        WebDavAccounts.Clear();
        Buckets.Clear();
        _currentAccount = null;
        SyncSelectedBucket(null);
        SetPagination(null, null);
        ResetPaging();
        _pathContext = RemotePathContextResult.From(RemotePath.Root);
        StatusMessage = Localization.GetString("RemoteBrowser", "LoadingAccounts");
        CurrentPath = "/";
        ClearErrorDetails();

        var category = ToProviderCategory(_resourceGroup);
        var accountsResult = await _accounts.ListAsync(new ListStorageAccountsRequest(category)).ConfigureAwait(true);
        if (accountsResult.IsFailure)
        {
            StatusMessage = Localization.GetString("RemoteBrowser", "AccountEntryLoadFailed");
            SetErrorDetails(StatusMessage, StatusMessage, accountsResult.Error);
            RaiseCollectionStateChanged();
            return;
        }

        var accounts = accountsResult.GetValueOrThrow()
            .Where(IsAccountVisibleInResourceGroup)
            .ToArray();
        if (accounts.Length == 0)
        {
            SetAccountSelection(true);
            StatusMessage = _resourceGroup == NavigationResourceGroup.All
                ? string.Empty
                : Localization.GetString("RemoteBrowser", "CategoryNoAccounts");
            RaiseCollectionStateChanged();
            return;
        }

        SetAccountSelection(true);
        foreach (var account in accounts)
        {
            var choice = RemoteAccountChoiceViewModel.From(account);
            Accounts.Add(choice);
            if (account.ProviderCategory == StorageProviderCategory.ObjectStorage)
            {
                ObjectStorageAccounts.Add(choice);
            }
            else if (IsFtpOrSftp(account))
            {
                FileTransferAccounts.Add(choice);
            }
            else if (IsWebDav(account))
            {
                WebDavAccounts.Add(choice);
            }
        }

        StatusMessage = Localization.GetString("RemoteBrowser", "SelectConnectionToBrowse");
        RaiseCollectionStateChanged();
    }

    private bool IsAccountVisibleInResourceGroup(StorageAccountSummary account)
    {
        return _resourceGroup switch
        {
            NavigationResourceGroup.All => true,
            NavigationResourceGroup.ObjectStorage => account.ProviderCategory == StorageProviderCategory.ObjectStorage,
            NavigationResourceGroup.FileTransfer => IsFtpOrSftp(account),
            NavigationResourceGroup.WebDav => IsWebDav(account),
            _ => true
        };
    }

    private Task OpenAccountAsync(object? parameter)
    {
        if (parameter is not RemoteAccountChoiceViewModel account)
        {
            return Task.CompletedTask;
        }

        SelectedAccount = account;
        _storageAccountId = account.Id;
        _remotePath = account.InitialPath;
        ResetPaging();
        ClearSearch();
        RaiseUploadStateChanged();
        return LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        if (_storageAccountId is null)
        {
            await LoadEntryAsync().ConfigureAwait(true);
            return;
        }

        await RunWithLoadingAsync(LoadItemsCoreAsync).ConfigureAwait(true);

        if (_restoreListAfterSearchLoad)
        {
            _restoreListAfterSearchLoad = false;
            await RestoreListWhenSearchClearedAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadItemsCoreAsync()
    {
        if (_storageAccountId is not { } storageAccountId)
        {
            return;
        }

        SetAccountSelection(false);
        Items.Clear();
        Rows.Clear();
        RaiseFileSelectionStateChanged();
        Accounts.Clear();
        ObjectStorageAccounts.Clear();
        FileTransferAccounts.Clear();
        WebDavAccounts.Clear();
        await LoadCurrentAccountAsync().ConfigureAwait(true);
        CurrentPath = FormatRemotePath(_remotePath);
        StatusMessage = Localization.GetString("RemoteBrowser", "LoadingItems");
        ClearErrorDetails();
        _lastFailedListRequest = null;
        RetryLoadCommand.RaiseCanExecuteChanged();

        var appliedSearchText = GetAppliedSearchText();
        var result = await _browser.ListRemoteItemsAsync(
            new ListRemoteItemsRequest(
                storageAccountId,
                _remotePath,
                new RemotePageRequest(100, _requestedCursor),
                appliedSearchText)).ConfigureAwait(true);

        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("RemoteBrowser", "ItemListLoadFailed");
            _lastFailedListRequest = new ListRemoteItemsRequest(
                storageAccountId,
                _remotePath,
                new RemotePageRequest(100, _requestedCursor),
                appliedSearchText);
            RetryLoadCommand.RaiseCanExecuteChanged();
            SetErrorDetails(
                StatusMessage,
                StatusMessage,
                result.Error,
                new Dictionary<string, string>
                {
                    [Localization.GetString("Common", "Account")] = storageAccountId.ToString(),
                    [Localization.GetString("Common", "Path")] = CurrentPath
                });
            RaiseCollectionStateChanged();
            return;
        }

        var list = result.GetValueOrThrow();
        _currentCursor = _requestedCursor;
        _remotePath = list.Path;
        _pathContext = list.Context;
        SetPagination(list.PreviousCursor, list.NextCursor);
        CurrentPath = FormatRemotePath(_remotePath);
        UpdateTitleFromCurrentAccount();
        RebuildBreadcrumbItems();
        OnPropertyChanged(nameof(IsBucketList));
        OnPropertyChanged(nameof(IsDataRowsVisible));
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
        OnPropertyChanged(nameof(IsSearchVisible));
        OnPropertyChanged(nameof(IsObjectStorageNavigationVisible));
        OnPropertyChanged(nameof(IsObjectStorageBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsWebDavBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsPlainBreadcrumbNavigationVisible));
        OnPropertyChanged(nameof(IsRemoteNavigationVisible));
        OnPropertyChanged(nameof(NameColumnHeader));

        foreach (var item in list.Items)
        {
            Items.Add(RemoteItemRowViewModel.From(
                item,
                OpenRowCommand,
                PreviewSelectedCommand,
                DownloadSelectedCommand,
                RenameSelectedCommand,
                DeleteSelectedCommand,
                CanPreviewItem(item),
                CanRenameItem(item),
                CanDeleteItem(item)));
            Rows.Add(RemoteListRowViewModel.From(
                item,
                OpenRowCommand,
                PreviewSelectedCommand,
                DownloadSelectedCommand,
                RenameSelectedCommand,
                DeleteSelectedCommand,
                CanPreviewItem(item),
                CanRenameItem(item),
                CanDeleteItem(item),
                RaiseFileSelectionStateChanged));
        }

        if (_currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage && _pathContext.IsRoot)
        {
            ReplaceBuckets(list.Items.Where(item => item.Kind == RemoteItemKind.Bucket));
            SyncSelectedBucket(null);
        }
        else if (_currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage)
        {
            SyncSelectedBucketForPath(_remotePath);
        }

        RefreshCurrentListStatus();
    }

    private async Task RunWithLoadingAsync(Func<Task> operation)
    {
        var wasIdle = _loadingDepth == 0;
        _loadingDepth++;

        if (wasIdle)
        {
            IsLoading = true;
        }

        try
        {
            await operation().ConfigureAwait(true);
        }
        finally
        {
            _loadingDepth--;
            if (_loadingDepth == 0)
            {
                IsLoading = false;
            }
        }
    }

    private async Task<T> RunWithLoadingAsync<T>(Func<Task<T>> operation)
    {
        var wasIdle = _loadingDepth == 0;
        _loadingDepth++;

        if (wasIdle)
        {
            IsLoading = true;
        }

        try
        {
            return await operation().ConfigureAwait(true);
        }
        finally
        {
            _loadingDepth--;
            if (_loadingDepth == 0)
            {
                IsLoading = false;
            }
        }
    }

    private void SetAccountSelection(bool value)
    {
        if (SetProperty(ref _isAccountSelection, value, nameof(IsAccountSelection)))
        {
            OnPropertyChanged(nameof(IsItemList));
            OnPropertyChanged(nameof(IsFileListActionsVisible));
            OnPropertyChanged(nameof(IsUploadActionVisible));
            OnPropertyChanged(nameof(IsCreateFolderActionVisible));
            OnPropertyChanged(nameof(IsBucketListActionsVisible));
            OnPropertyChanged(nameof(IsRemoteEntryEmptyVisible));
            OnPropertyChanged(nameof(IsRemoteAccountOverviewVisible));
            OnPropertyChanged(nameof(IsRemoteFileBrowserVisible));
            OnPropertyChanged(nameof(IsRemoteEntryAddAccountVisible));
            OnPropertyChanged(nameof(IsRemoteEntryActionsVisible));
            OnPropertyChanged(nameof(CanCreateBucket));
            OnPropertyChanged(nameof(CanCreateFolder));
            RaiseUploadStateChanged();
        }
    }

    private void RaiseCollectionStateChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasAccounts));
        OnPropertyChanged(nameof(HasObjectStorageAccounts));
        OnPropertyChanged(nameof(HasFileTransferAccounts));
        OnPropertyChanged(nameof(HasWebDavAccounts));
        OnPropertyChanged(nameof(TotalAccountCount));
        OnPropertyChanged(nameof(ObjectStorageAccountCount));
        OnPropertyChanged(nameof(FileTransferAccountCount));
        OnPropertyChanged(nameof(WebDavAccountCount));
        OnPropertyChanged(nameof(HasBuckets));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(EmptyStateText));
        OnPropertyChanged(nameof(IsAddAccountVisible));
        OnPropertyChanged(nameof(IsUploadVisible));
        OnPropertyChanged(nameof(IsFileListActionsVisible));
            OnPropertyChanged(nameof(IsUploadActionVisible));
            OnPropertyChanged(nameof(IsCreateFolderActionVisible));
            OnPropertyChanged(nameof(IsBucketListActionsVisible));
        OnPropertyChanged(nameof(IsRemoteEntryEmptyVisible));
        OnPropertyChanged(nameof(IsRemoteAccountOverviewVisible));
        OnPropertyChanged(nameof(IsRemoteFileBrowserVisible));
        OnPropertyChanged(nameof(IsRemoteEntryAddAccountVisible));
        OnPropertyChanged(nameof(IsRemoteEntryActionsVisible));
        OnPropertyChanged(nameof(CanCreateBucket));
        OnPropertyChanged(nameof(CanCreateFolder));
        OnPropertyChanged(nameof(IsBucketSelectorVisible));
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
        OnPropertyChanged(nameof(IsSearchVisible));
        OnPropertyChanged(nameof(IsObjectStorageNavigationVisible));
        OnPropertyChanged(nameof(IsObjectStorageBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsWebDavBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsPlainBreadcrumbNavigationVisible));
        OnPropertyChanged(nameof(IsRemoteNavigationVisible));
        OnPropertyChanged(nameof(NameColumnHeader));
        OnPropertyChanged(nameof(IsBucketList));
        OnPropertyChanged(nameof(IsDataRowsVisible));
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
        OnPropertyChanged(nameof(IsSearchVisible));
        OnPropertyChanged(nameof(IsObjectStorageNavigationVisible));
        OnPropertyChanged(nameof(IsObjectStorageBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsWebDavBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsPlainBreadcrumbNavigationVisible));
        OnPropertyChanged(nameof(IsRemoteNavigationVisible));
        RaiseUploadStateChanged();
        RaiseFileSelectionStateChanged();
    }

    private void RaiseUploadStateChanged()
    {
        OnPropertyChanged(nameof(CanUpload));
        OnPropertyChanged(nameof(IsUploadActionVisible));
        OnPropertyChanged(nameof(IsCreateFolderActionVisible));
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(IsPaginationVisible));
        RetryLoadCommand.RaiseCanExecuteChanged();
        UploadCommand.RaiseCanExecuteChanged();
        CreateBucketCommand.RaiseCanExecuteChanged();
        CreateFolderCommand.RaiseCanExecuteChanged();
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }

    private void RefreshAccountsOnUiThread()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        });
    }

    private bool CanWriteAtCurrentRoot()
    {
        return _remotePath.IsRoot &&
            _currentAccount is not null &&
            IsWebDav(_currentAccount);
    }

    private bool CanWriteAtCurrentFileTransferLocation()
    {
        return _currentAccount is not null &&
            _currentAccount.ProviderCategory == StorageProviderCategory.FileTransfer &&
            !IsBucketList;
    }


    private bool IsJianguoyunVirtualRoot()
    {
        if (_currentAccount is null ||
            !_remotePath.IsRoot ||
            !IsWebDav(_currentAccount) ||
            !_currentAccount.ProviderConfig.TryGetValue("webDavProfile", out var profile) ||
            !profile.Equals("jianguoyun", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !_currentAccount.ProviderConfig.TryGetValue("rootPath", out var rootPath) ||
            string.IsNullOrWhiteSpace(rootPath) ||
            rootPath.Trim().Trim('/') == string.Empty;
    }

    private void SetPagination(RemotePageCursor? previousCursor, RemotePageCursor? nextCursor)
    {
        _previousCursor = previousCursor;
        _nextCursor = nextCursor;
        _pathContext = _pathContext with
        {
            HasPreviousPage = _previousPageCursors.Count > 0,
            HasNextPage = nextCursor is not null
        };
        OnPropertyChanged(nameof(CanGoPreviousPage));
        OnPropertyChanged(nameof(CanGoNextPage));
        OnPropertyChanged(nameof(IsPaginationVisible));
        OnPropertyChanged(nameof(NameColumnHeader));
        OnPropertyChanged(nameof(IsBucketList));
        OnPropertyChanged(nameof(IsDataRowsVisible));
        PreviousPageCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
    }

    private void ResetPaging()
    {
        _requestedCursor = null;
        _currentCursor = null;
        _previousPageCursors.Clear();
    }

    private void ClearSearch()
    {
        _appliedSearchText = string.Empty;
        _restoreListAfterSearchLoad = false;

        if (string.IsNullOrEmpty(_searchText))
        {
            return;
        }

        _searchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
    }

    private async Task LoadCurrentAccountAsync()
    {
        if (_storageAccountId is null)
        {
            _currentAccount = null;
            return;
        }

        if (_currentAccount?.Id == _storageAccountId.Value)
        {
            return;
        }

        var result = await _accounts.ListAsync(new ListStorageAccountsRequest()).ConfigureAwait(true);
        if (result.IsFailure)
        {
            _currentAccount = null;
            return;
        }

        _currentAccount = result.GetValueOrThrow().FirstOrDefault(account => account.Id == _storageAccountId.Value);
        UpdateTitleFromCurrentAccount();
        OnPropertyChanged(nameof(IsBucketSelectorVisible));
        OnPropertyChanged(nameof(IsBreadcrumbVisible));
        OnPropertyChanged(nameof(IsSearchVisible));
        OnPropertyChanged(nameof(IsObjectStorageNavigationVisible));
        OnPropertyChanged(nameof(IsObjectStorageBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsWebDavBreadcrumbInSearchNavigationVisible));
        OnPropertyChanged(nameof(IsPlainBreadcrumbNavigationVisible));
        OnPropertyChanged(nameof(IsRemoteNavigationVisible));
        OnPropertyChanged(nameof(IsPaginationVisible));
    }

    private void UpdateTitleFromCurrentAccount()
    {
        if (_currentAccount is not null)
        {
            Title = _currentAccount.DisplayName;
            return;
        }

        Title = _resourceGroup switch
        {
            NavigationResourceGroup.All => Localization.GetString("Shell", "RemoteStorage"),
            NavigationResourceGroup.ObjectStorage => "OSS",
            NavigationResourceGroup.FileTransfer => "FTP / SFTP",
            NavigationResourceGroup.WebDav => "WebDAV",
            _ => Localization.GetString("RemoteBrowser", "Title")
        };
    }

    private void ReplaceBuckets(IEnumerable<RemoteItem> buckets)
    {
        Buckets.Clear();
        foreach (var bucket in buckets.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            Buckets.Add(BucketChoiceViewModel.From(bucket));
        }
    }

    private void SyncSelectedBucket(BucketChoiceViewModel? bucket)
    {
        _isSyncingBucketSelection = true;
        try
        {
            SelectedBucket = bucket;
        }
        finally
        {
            _isSyncingBucketSelection = false;
        }
    }

    private void SyncSelectedBucketForPath(RemotePath path)
    {
        if (_currentAccount?.ProviderCategory != StorageProviderCategory.ObjectStorage ||
            !TryGetBucketRootPath(path, out var bucketPath))
        {
            SyncSelectedBucket(null);
            return;
        }

        var bucket = Buckets.FirstOrDefault(item => item.Path == bucketPath);
        if (bucket is null)
        {
            bucket = new BucketChoiceViewModel(bucketPath.Name, bucketPath, "-", string.Empty);
            Buckets.Add(bucket);
        }

        SyncSelectedBucket(bucket);
    }

    private static bool TryGetBucketRootPath(RemotePath path, out RemotePath bucketPath)
    {
        bucketPath = RemotePath.Root;
        if (path.IsRoot)
        {
            return false;
        }

        var parts = path.Value.Split(
            path.Separator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        bucketPath = new RemotePath(parts[0], RemotePathKind.BucketRoot, path.Separator);
        return true;
    }

    public Task NavigateBreadcrumbAsync(object? navigateContext)
    {
        if (navigateContext is not RemotePath path)
        {
            return Task.CompletedTask;
        }

        _remotePath = path;
        _requestedCursor = null;
        ClearSearch();
        SyncSelectedBucketForPath(_remotePath);

        return LoadItemsAsync();
    }

    public Task ApplySearchAsync()
    {
        if (!IsSearchVisible || IsLoading)
        {
            return Task.CompletedTask;
        }

        _appliedSearchText = SearchText.Trim();
        ResetPaging();
        return LoadItemsAsync();
    }

    public Task RestoreListWhenSearchClearedAsync()
    {
        if (!IsSearchVisible)
        {
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            _restoreListAfterSearchLoad = false;
            return Task.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_appliedSearchText))
        {
            return Task.CompletedTask;
        }

        if (IsLoading)
        {
            _restoreListAfterSearchLoad = true;
            return Task.CompletedTask;
        }

        _appliedSearchText = string.Empty;
        ResetPaging();
        return LoadItemsAsync();
    }

    private void SetAllFilesSelected(bool isSelected)
    {
        _isUpdatingFileSelection = true;
        try
        {
            foreach (var row in Rows.Where(row => row.IsFile))
            {
                row.IsSelected = isSelected;
            }
        }
        finally
        {
            _isUpdatingFileSelection = false;
        }

        RaiseFileSelectionStateChanged();
    }

    private void ClearSelectedFiles()
    {
        SetAllFilesSelected(false);
    }

    private void RaiseFileSelectionStateChanged()
    {
        if (_isUpdatingFileSelection)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedFileCount));
        OnPropertyChanged(nameof(SelectedFileCountText));
        OnPropertyChanged(nameof(HasSelectedFiles));
        OnPropertyChanged(nameof(IsBatchSelectionBarVisible));
        OnPropertyChanged(nameof(HasSelectableFiles));
        OnPropertyChanged(nameof(CanDeleteSelectedFiles));
        OnPropertyChanged(nameof(AllFilesSelectionState));
        DownloadSelectedFilesCommand.RaiseCanExecuteChanged();
        DeleteSelectedFilesCommand.RaiseCanExecuteChanged();
        ClearSelectedFilesCommand.RaiseCanExecuteChanged();
    }

    private void RebuildBreadcrumbItems()
    {
        BreadcrumbItems.Clear();
        if (IsWebDavBreadcrumbRootVisible())
        {
            BreadcrumbItems.Add(new BreadcrumbItemData
            {
                Content = Localization.GetString("RemoteBrowser", "WebDavRoot"),
                NavigateContext = RemotePath.Root
            });
        }

        if (_remotePath.IsRoot)
        {
            return;
        }

        var parts = _remotePath.Value
            .Split(_remotePath.Separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        foreach (var i in GetVisibleBreadcrumbIndexes(parts.Length))
        {
            if (i < 0)
            {
                BreadcrumbItems.Add(new BreadcrumbItemData
                {
                    Content = "...",
                    NavigateContext = null
                });
                continue;
            }

            var value = string.Join(_remotePath.Separator, parts.Take(i + 1));
            var kind = _currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage && i == 0
                ? RemotePathKind.BucketRoot
                : RemotePathKind.Folder;
            BreadcrumbItems.Add(new BreadcrumbItemData
            {
                Content = parts[i],
                NavigateContext = new RemotePath(value, kind, _remotePath.Separator)
            });
        }
    }

    private bool IsWebDavBreadcrumbRootVisible()
    {
        return _currentAccount is not null && IsWebDav(_currentAccount);
    }
    private static IReadOnlyList<int> GetVisibleBreadcrumbIndexes(int partCount)
    {
        if (partCount <= 4)
        {
            return Enumerable.Range(0, partCount).ToArray();
        }

        return [0, -1, partCount - 3, partCount - 2, partCount - 1];
    }

    private async Task DownloadSelectedAsync(object? parameter)
    {
        if (_storageAccountId is null || !TryGetFileRow(parameter, out var path, out var name))
        {
            return;
        }

        var storageAccountId = _storageAccountId.Value;
        var preferences = await _preferences.GetAsync().ConfigureAwait(true);
        var localPath = new LocalPath(Path.Combine(preferences.DefaultDownloadDirectory, name));
        _messages.Info(Localization.Format("RemoteBrowser", "CreatingDownload", localPath.Value));

        _ = Task.Run(async () =>
        {
            var result = await _transfers.CreateDownloadTasksAsync(
                new CreateDownloadTasksRequest(
                    storageAccountId,
                    [path],
                    localPath,
                    TransferOverwritePolicy.Rename)).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                if (result.IsFailure)
                {
                    var message = Localization.GetString("RemoteBrowser", "DownloadCreateFailed");
                    StatusMessage = message;
                    SetErrorDetails(message, message, result.Error);
                    _messages.Error(message);
                    return;
                }

                _statusBar.RequestQueueRefresh();
                StatusMessage = Localization.Format("RemoteBrowser", "DownloadCreatedAt", localPath.Value);
                _messages.Success(Localization.GetString("RemoteBrowser", "DownloadCreated"));
            });
        });
    }

    private async Task DownloadSelectedFilesAsync()
    {
        if (_storageAccountId is not { } storageAccountId)
        {
            return;
        }

        var selectedFiles = Rows
            .Where(row => row.IsFile && row.IsSelected)
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            return;
        }

        var preferences = await _preferences.GetAsync().ConfigureAwait(true);
        var targets = selectedFiles
            .Select(row => new DownloadTaskTarget(
                row.Path,
                new LocalPath(Path.Combine(preferences.DefaultDownloadDirectory, row.Name))))
            .ToArray();

        StatusMessage = Localization.Format("RemoteBrowser", "CreatingDownloads", targets.Length);
        var result = await _transfers.CreateDownloadTasksAsync(
            new CreateDownloadTasksRequest(
                storageAccountId,
                targets,
                TransferOverwritePolicy.Rename)).ConfigureAwait(true);

        if (result.IsFailure)
        {
            var message = Localization.GetString("RemoteBrowser", "BatchDownloadCreateFailed");
            StatusMessage = message;
            SetErrorDetails(
                message,
                message,
                result.Error,
                new Dictionary<string, string>
                {
                    [Localization.GetString("Common", "FileCount")] = targets.Length.ToString(),
                    [Localization.GetString("Common", "SaveDirectory")] = preferences.DefaultDownloadDirectory
                });
            _messages.Error(message);
            return;
        }

        var createdCount = result.GetValueOrThrow().Tasks.Count;
        _statusBar.RequestQueueRefresh();
        ClearSelectedFiles();
        StatusMessage = Localization.Format("RemoteBrowser", "DownloadsCreatedAt", createdCount, preferences.DefaultDownloadDirectory);
        _messages.Success(Localization.Format("RemoteBrowser", "DownloadsCreated", createdCount));
    }

    private async Task PreviewSelectedAsync(object? parameter)
    {
        if (_storageAccountId is null ||
            !TryGetPreviewableRow(parameter, out var path, out var name, out var sizeBytes, out var kind))
        {
            return;
        }

        StatusMessage = Localization.Format("RemoteBrowser", "Previewing", name);
        var previewRequest = new PreviewRemoteFileRequest(
            _storageAccountId.Value,
            path,
            name,
            sizeBytes,
            kind);
        var result = await RunWithLoadingAsync(() => _browser.PreviewRemoteFileAsync(previewRequest)).ConfigureAwait(true);

        if (result.IsFailure)
        {
            var message = Localization.GetString("RemoteBrowser", "PreviewFailed");
            StatusMessage = message;
            SetErrorDetails(
                message,
                message,
                result.Error,
                new Dictionary<string, string>
                {
                    [Localization.GetString("Common", "File")] = name,
                    [Localization.GetString("Common", "Path")] = path.ToString()
                });
            _messages.Error(message);
            return;
        }

        var preview = result.GetValueOrThrow();
        await _dialogs.ShowPreviewAsync(new PreviewDialogRequest(
            preview.Kind,
            preview.FileName,
            preview.ContentType,
            preview.Size,
            preview.Kind == RemotePreviewKind.Image
                ? async cancellationToken =>
                {
                    var streamResult = await _browser.OpenRemoteImagePreviewStreamAsync(
                        previewRequest,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    if (streamResult.IsFailure)
                    {
                        throw new InvalidOperationException(
                            streamResult.Error?.Message ?? "Remote image preview could not be loaded.");
                    }

                    return streamResult.GetValueOrThrow().Content;
                }
                : null,
            preview.Text,
            preview.EncodingName)).ConfigureAwait(true);
        StatusMessage = Localization.Format("RemoteBrowser", "PreviewOpened", name);
    }

    private async Task RenameSelectedAsync(object? parameter)
    {
        if (_storageAccountId is null ||
            !TryGetRenameableRow(parameter, out var path, out var name, out var kind))
        {
            return;
        }

        if (IsObjectStorageFolderRenameDisabled(kind))
        {
            _messages.Info(Localization.GetString("RemoteBrowser", "FolderRenameUnavailable"));
            return;
        }

        var newName = await _dialogs.ShowTextInputAsync(new TextInputDialogRequest(
            Localization.GetString("RemoteBrowser", "RenameTitle"),
            Localization.Format("RemoteBrowser", "RenamePrompt", name),
            name,
            Localization.GetString("RemoteBrowser", "NewName"),
            Localization.GetString("Common", "Save"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(newName.Trim(), name, StringComparison.Ordinal))
        {
            return;
        }

        StatusMessage = Localization.Format("RemoteBrowser", "Renaming", name);
        var result = await RunWithLoadingAsync(() => _browser.RenameRemoteItemAsync(new RenameRemoteItemRequest(
            _storageAccountId.Value,
            path,
            kind,
            newName.Trim()))).ConfigureAwait(true);

        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("RemoteBrowser", "RenameFailed");
            SetErrorDetails(
                StatusMessage,
                StatusMessage,
                result.Error,
                new Dictionary<string, string>
                {
                    [Localization.GetString("Common", "Object")] = name,
                    [Localization.GetString("Common", "NewName")] = newName.Trim(),
                    [Localization.GetString("Common", "Path")] = path.ToString()
                });
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            return;
        }

        _messages.Info(Localization.Format("RemoteBrowser", "Renamed", newName.Trim()));
        StatusMessage = Localization.Format("RemoteBrowser", "Renamed", newName.Trim());
        ApplyRenamedRow(path, kind, newName.Trim());
    }

    private void ApplyRenamedRow(RemotePath oldPath, RemoteItemKind kind, string newName)
    {
        var newPath = CreateRenamedPath(oldPath, kind, newName);
        for (var index = 0; index < Rows.Count; index++)
        {
            var row = Rows[index];
            if (row.Path != oldPath)
            {
                continue;
            }

            Rows[index] = row.WithPathAndName(newPath, newName);
            break;
        }

        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            if (item.Path != oldPath)
            {
                continue;
            }

            Items[index] = item with
            {
                Path = newPath,
                Name = newName
            };
            break;
        }
    }

    private void AppendCreatedFolderRow(string folderName, RemotePath folderPath)
    {
        if (Rows.Any(row => row.Path == folderPath))
        {
            RefreshCurrentListStatus();
            return;
        }

        var item = new RemoteItem(
            folderName,
            folderPath,
            RemoteItemKind.Folder,
            size: null,
            updatedAt: DateTimeOffset.Now);
        var itemRow = RemoteItemRowViewModel.From(
            item,
            OpenRowCommand,
            PreviewSelectedCommand,
            DownloadSelectedCommand,
            RenameSelectedCommand,
            DeleteSelectedCommand,
            CanPreviewItem(item),
            CanRenameItem(item),
            CanDeleteItem(item));
        var listRow = RemoteListRowViewModel.From(
            item,
            OpenRowCommand,
            PreviewSelectedCommand,
            DownloadSelectedCommand,
            RenameSelectedCommand,
            DeleteSelectedCommand,
            CanPreviewItem(item),
            CanRenameItem(item),
            CanDeleteItem(item),
            RaiseFileSelectionStateChanged);

        var insertIndex = Rows
            .Select((row, index) => new { row, index })
            .FirstOrDefault(entry => string.Compare(
                entry.row.Name,
                folderName,
                StringComparison.CurrentCultureIgnoreCase) > 0)
            ?.index ?? Rows.Count;

        Rows.Insert(insertIndex, listRow);
        Items.Insert(insertIndex, itemRow);
        RefreshCurrentListStatus();
    }

    private void RemoveRow(RemotePath path)
    {
        RemoveFirst(Rows, row => row.Path == path);
        RemoveFirst(Items, item => item.Path == path);
        RefreshCurrentListStatus();
    }

    private void RefreshCurrentListStatus()
    {
        var query = GetAppliedSearchText();
        if (!string.IsNullOrWhiteSpace(query))
        {
            StatusMessage = Rows.Count == 0
                ? Localization.Format("RemoteBrowser", "SearchNoMatch", query)
                : Localization.Format("RemoteBrowser", "SearchMatchCount", Rows.Count);
        }
        else
        {
            StatusMessage = Rows.Count == 0
                ? Localization.GetString("RemoteBrowser", "EmptyDirectory")
                : Localization.Format("RemoteBrowser", "DirectoryItemCount", Rows.Count);
        }

        RaiseCollectionStateChanged();
    }

    private string? GetAppliedSearchText()
    {
        return IsSearchVisible && !string.IsNullOrWhiteSpace(_appliedSearchText)
            ? _appliedSearchText
            : null;
    }

    private static void RemoveFirst<T>(ObservableCollection<T> collection, Predicate<T> predicate)
    {
        for (var index = 0; index < collection.Count; index++)
        {
            if (!predicate(collection[index]))
            {
                continue;
            }

            collection.RemoveAt(index);
            return;
        }
    }

    private static RemotePath CreateRenamedPath(RemotePath oldPath, RemoteItemKind kind, string newName)
    {
        var pathKind = kind == RemoteItemKind.Folder
            ? RemotePathKind.Folder
            : RemotePathKind.ObjectPath;
        var parent = oldPath.GetParent();

        return !parent.HasValue
            ? new RemotePath(newName, pathKind, oldPath.Separator)
            : parent.Value.Combine(newName, pathKind);
    }

    private async Task UploadAsync()
    {
        if (_storageAccountId is null || !CanUpload)
        {
            if (IsUploadPreparing)
            {
                StatusMessage = Localization.GetString("RemoteBrowser", "UploadPreparing");
            }

            return;
        }

        var selectedFiles = await _filePicker.PickFilesForUploadAsync().ConfigureAwait(true);
        if (selectedFiles.Count == 0)
        {
            StatusMessage = Localization.GetString("RemoteBrowser", "NoUploadFiles");
            return;
        }

        var uploadTargets = selectedFiles
            .Select(path => new LocalPath(path))
            .Select(localPath => new UploadTaskTarget(localPath, GetUploadTargetPath(localPath)))
            .ToArray();
        var storageAccountId = _storageAccountId.Value;

        IsUploadPreparing = true;
        ClearErrorDetails();
        OperationResult<PrepareBatchUploadTasksResult> prepareResult;
        try
        {
            var progress = new Progress<UploadPreparationProgress>(item =>
            {
                StatusMessage = item.TotalCount <= 1
                    ? Localization.Format("RemoteBrowser", "FingerprintingSingle", item.FileName)
                    : Localization.Format("RemoteBrowser", "FingerprintingMultiple", item.CurrentIndex, item.TotalCount, item.FileName);
            });
            prepareResult = await _transfers
                .PrepareBatchUploadTasksAsync(
                    new PrepareBatchUploadTasksRequest(storageAccountId, uploadTargets),
                    progress)
                .ConfigureAwait(true);
        }
        finally
        {
            IsUploadPreparing = false;
        }

        if (prepareResult.IsFailure)
        {
            var message = Localization.GetString("RemoteBrowser", "PreUploadCheckFailed");
            StatusMessage = message;
            SetErrorDetails(message, message, prepareResult.Error);
            _messages.Error(message);
            return;
        }

        var prepared = prepareResult.GetValueOrThrow();
        var duplicateTargets = prepared.Targets
            .Where(target => target.HistoricalRecords.Count > 0)
            .ToArray();
        if (duplicateTargets.Length > 0)
        {
            var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
                Localization.GetString("RemoteBrowser", "DuplicateUploadTitle"),
                BuildDuplicateUploadMessage(duplicateTargets),
                Localization.GetString("RemoteBrowser", "UploadAgain"),
                Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
            if (!confirmed)
            {
                StatusMessage = Localization.GetString("RemoteBrowser", "UploadCanceled");
                return;
            }
        }

        var targetsToCreate = prepared.Targets
            .Select(target => new UploadTaskTarget(target.LocalPath, target.RemotePath, target.Fingerprint))
            .ToArray();

        _messages.Info(targetsToCreate.Length == 1
            ? Localization.GetString("RemoteBrowser", "CreatingUploadSingle")
            : Localization.Format("RemoteBrowser", "CreatingUploadMultiple", targetsToCreate.Length));

        _ = Task.Run(async () =>
        {
            var result = await _transfers.CreateBatchUploadTasksAsync(
                new CreateBatchUploadTasksRequest(
                    storageAccountId,
                    targetsToCreate,
                    TransferOverwritePolicy.Ask)).ConfigureAwait(false);

            Dispatcher.UIThread.Post(() =>
            {
                if (result.IsFailure)
                {
                    var message = Localization.GetString("RemoteBrowser", "UploadCreateFailed");
                    StatusMessage = message;
                    SetErrorDetails(message, message, result.Error);
                    _messages.Error(message);
                    return;
                }

                var createdCount = result.GetValueOrThrow().Tasks.Count;
                _statusBar.RequestQueueRefresh();
                StatusMessage = createdCount <= 1
                    ? Localization.GetString("RemoteBrowser", "UploadCreatedWaitSingle")
                    : Localization.Format("RemoteBrowser", "UploadCreatedWaitMultiple", createdCount);
                _messages.Success(createdCount <= 1
                    ? Localization.GetString("RemoteBrowser", "UploadCreatedSingle")
                    : Localization.Format("RemoteBrowser", "UploadCreatedMultiple", createdCount));
            });
        });
    }

    private async Task CreateBucketAsync()
    {
        if (_storageAccountId is null || !CanCreateBucket)
        {
            return;
        }

        var bucketName = await _dialogs.ShowTextInputAsync(new TextInputDialogRequest(
            Localization.GetString("RemoteBrowser", "CreateBucketTitle"),
            Localization.GetString("RemoteBrowser", "CreateBucketPrompt"),
            string.Empty,
            Localization.GetString("RemoteBrowser", "BucketNameField"),
            Localization.GetString("RemoteBrowser", "Create"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        bucketName = bucketName?.Trim();
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            return;
        }

        if (bucketName.Contains('/', StringComparison.Ordinal) ||
            bucketName.Contains('\\', StringComparison.Ordinal))
        {
            _messages.Error(Localization.GetString("RemoteBrowser", "InvalidBucketName"));
            return;
        }

        StatusMessage = Localization.Format("RemoteBrowser", "CreatingBucket", bucketName);
        var path = new RemotePath(bucketName, RemotePathKind.BucketRoot);
        var result = await _browser.CreateRemoteFolderAsync(new CreateRemoteFolderRequest(
            _storageAccountId.Value,
            path)).ConfigureAwait(true);

        if (result.IsFailure)
        {
            var message = Localization.GetString("RemoteBrowser", "CreateBucketFailed");
            StatusMessage = message;
            SetErrorDetails(
                message,
                message,
                result.Error,
                new Dictionary<string, string>
                {
                    ["Bucket"] = bucketName,
                    [Localization.GetString("Common", "Account")] = _storageAccountId.Value.ToString()
                });
            _messages.Error(message);
            return;
        }

        _messages.Info(Localization.Format("RemoteBrowser", "BucketCreated", bucketName));
        StatusMessage = Localization.Format("RemoteBrowser", "BucketCreated", bucketName);
        await LoadItemsAsync().ConfigureAwait(true);
    }

    private async Task CreateFolderAsync()
    {
        if (_storageAccountId is null || !CanCreateFolder)
        {
            return;
        }

        var folderName = await _dialogs.ShowTextInputAsync(new TextInputDialogRequest(
            Localization.GetString("RemoteBrowser", "CreateFolderTitle"),
            Localization.GetString("RemoteBrowser", "CreateFolderPrompt"),
            string.Empty,
            Localization.GetString("RemoteBrowser", "FolderNameField"),
            Localization.GetString("RemoteBrowser", "Create"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        folderName = folderName?.Trim();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return;
        }

        if (folderName.Contains('/', StringComparison.Ordinal) ||
            folderName.Contains('\\', StringComparison.Ordinal))
        {
            _messages.Error(Localization.GetString("RemoteBrowser", "InvalidFolderName"));
            return;
        }

        var folderPath = _remotePath.Combine(folderName, RemotePathKind.Folder);
        StatusMessage = Localization.Format("RemoteBrowser", "CreatingFolder", folderName);
        var result = await _browser.CreateRemoteFolderAsync(new CreateRemoteFolderRequest(
            _storageAccountId.Value,
            folderPath)).ConfigureAwait(true);

        if (result.IsFailure)
        {
            var message = Localization.GetString("RemoteBrowser", "CreateFolderFailed");
            StatusMessage = message;
            SetErrorDetails(
                message,
                message,
                result.Error,
                new Dictionary<string, string>
                {
                    [Localization.GetString("Common", "Folder")] = folderName,
                    [Localization.GetString("Common", "CurrentLocation")] = CurrentPath,
                    [Localization.GetString("Common", "Account")] = _storageAccountId.Value.ToString()
                });
            _messages.Error(message);
            return;
        }

        _messages.Info(Localization.Format("RemoteBrowser", "FolderCreated", folderName));
        StatusMessage = Localization.Format("RemoteBrowser", "FolderCreated", folderName);
        AppendCreatedFolderRow(folderName, folderPath);
    }

    private async Task DeleteSelectedAsync(object? parameter)
    {
        if (_storageAccountId is null || !TryGetDeletableRow(parameter, out var path, out var name, out var kind))
        {
            return;
        }

        var objectType = Localization.GetString("RemoteBrowser", kind == RemoteItemKind.Folder ? "Folder" : "File");
        var extraWarning = kind == RemoteItemKind.Folder
            ? Localization.GetString("RemoteBrowser", "DeleteFolderWarning")
            : string.Empty;
        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            Localization.GetString("RemoteBrowser", "DeleteRemoteTitle"),
            Localization.Format("RemoteBrowser", "DeleteRemoteMessage", objectType, name, CurrentPath, extraWarning),
            Localization.GetString("Common", "Delete"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        StatusMessage = Localization.Format("RemoteBrowser", "Deleting", name);
        var result = await RunWithLoadingAsync(() => _browser.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
            _storageAccountId.Value,
            path,
            kind))).ConfigureAwait(true);

        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("RemoteBrowser", "DeleteFailed");
            var failureSummary = kind == RemoteItemKind.Folder
                ? Localization.GetString("RemoteBrowser", "DeleteFolderPartialFailed")
                : Localization.GetString("RemoteBrowser", "DeleteFailed");
            SetErrorDetails(
                Localization.GetString("RemoteBrowser", "DeleteFailed"),
                failureSummary,
                result.Error,
                new Dictionary<string, string>
                {
                    [Localization.GetString("Common", "Object")] = name,
                    [Localization.GetString("Common", "Path")] = path.ToString()
                });
            await _dialogs.ShowErrorDetailsAsync(_lastErrorDetails!).ConfigureAwait(true);
            return;
        }

        _messages.Info(Localization.Format("RemoteBrowser", "Deleted", name));
        StatusMessage = Localization.Format("RemoteBrowser", "Deleted", name);
        RemoveRow(path);
    }

    private async Task DeleteSelectedFilesAsync()
    {
        if (_storageAccountId is not { } storageAccountId)
        {
            return;
        }

        var selectedFiles = Rows
            .Where(row => row.IsFile && row.IsSelected && row.CanDelete)
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(new ConfirmDialogRequest(
            Localization.GetString("RemoteBrowser", "BatchDeleteTitle"),
            Localization.Format("RemoteBrowser", "BatchDeleteMessage", selectedFiles.Length, CurrentPath),
            Localization.GetString("Common", "Delete"),
            Localization.GetString("Common", "Cancel"))).ConfigureAwait(true);
        if (!confirmed)
        {
            return;
        }

        var failures = new List<(RemoteListRowViewModel Row, StorageError Error)>();
        var deletedCount = 0;
        await RunWithLoadingAsync(async () =>
        {
            for (var index = 0; index < selectedFiles.Length; index++)
            {
                var row = selectedFiles[index];
                StatusMessage = Localization.Format("RemoteBrowser", "DeletingProgress", index + 1, selectedFiles.Length, row.Name);
                var result = await _browser.DeleteRemoteItemAsync(new DeleteRemoteItemRequest(
                    storageAccountId,
                    row.Path,
                    RemoteItemKind.File)).ConfigureAwait(true);
                if (result.IsFailure)
                {
                    failures.Add((row, result.Error!));
                }
                else
                {
                    deletedCount++;
                }
            }

            await LoadItemsCoreAsync().ConfigureAwait(true);
        }).ConfigureAwait(true);

        if (failures.Count == 0)
        {
            StatusMessage = Localization.Format("RemoteBrowser", "FilesDeleted", deletedCount);
            _messages.Info(StatusMessage);
            return;
        }

        var summary = Localization.Format("RemoteBrowser", "BatchDeleteSummary", deletedCount, failures.Count);
        StatusMessage = summary;
        SetErrorDetails(
            Localization.GetString("RemoteBrowser", "BatchDeletePartialFailed"),
            summary,
            failures[0].Error,
            new Dictionary<string, string>
            {
                [Localization.GetString("Common", "CurrentLocation")] = CurrentPath,
                [Localization.GetString("Common", "FailedFiles")] = string.Join(
                    Environment.NewLine,
                    failures.Select(item => $"{item.Row.Name}：{item.Error.Message}"))
            });
        _messages.Error(summary);
    }

    private bool CanDeleteItem(RemoteItem item)
    {
        return (_pathContext.CanDeleteSelectedFile || CanWriteAtCurrentFileTransferLocation()) &&
               item.Kind is RemoteItemKind.File or RemoteItemKind.Folder;
    }

    private bool CanRenameItem(RemoteItem item)
    {
        return item.Kind is RemoteItemKind.File or RemoteItemKind.Folder &&
               !IsObjectStorageFolderRenameDisabled(item.Kind);
    }

    private static bool CanPreviewItem(RemoteItem item)
    {
        if (item.Kind != RemoteItemKind.File)
        {
            return false;
        }

        var maxBytes = Path.GetExtension(item.Name).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" => RemotePreviewOptions.DefaultMaxImageBytes,
            ".txt" or ".log" or ".md" or ".markdown" or ".json" or ".xml" or ".yaml" or ".yml" or ".csv" or ".ini" or ".conf" or ".config" or ".html" or ".css" or ".js" or ".ts" or ".cs" or ".py" or ".java" or ".go" or ".rs" or ".cpp" or ".c" or ".h" or ".sql" or ".sh" or ".ps1" => RemotePreviewOptions.DefaultMaxTextBytes,
            _ => 0
        };

        return maxBytes > 0 && (item.Size is null || item.Size <= maxBytes);
    }

    private bool IsObjectStorageFolderRenameDisabled(RemoteItemKind kind)
    {
        return kind == RemoteItemKind.Folder &&
               _currentAccount?.ProviderCategory == StorageProviderCategory.ObjectStorage;
    }

    private static bool TryGetFileRow(object? parameter, out RemotePath path, out string name)
    {
        switch (parameter)
        {
            case RemoteItemRowViewModel { Kind: RemoteItemKind.File } row:
                path = row.Path;
                name = row.Name;
                return true;
            case RemoteListRowViewModel { Kind: RemoteItemKind.File } row:
                path = row.Path;
                name = row.Name;
                return true;
            default:
                path = RemotePath.Root;
                name = string.Empty;
                return false;
        }
    }

    private static bool TryGetPreviewableRow(
        object? parameter,
        out RemotePath path,
        out string name,
        out long? sizeBytes,
        out RemoteItemKind kind)
    {
        switch (parameter)
        {
            case RemoteItemRowViewModel { CanPreview: true } row:
                path = row.Path;
                name = row.Name;
                sizeBytes = row.SizeBytes;
                kind = row.Kind;
                return true;
            case RemoteListRowViewModel { CanPreview: true } row:
                path = row.Path;
                name = row.Name;
                sizeBytes = row.SizeBytes;
                kind = row.Kind;
                return true;
            default:
                path = RemotePath.Root;
                name = string.Empty;
                sizeBytes = null;
                kind = default;
                return false;
        }
    }

    private static bool TryGetDeletableRow(object? parameter, out RemotePath path, out string name, out RemoteItemKind kind)
    {
        switch (parameter)
        {
            case RemoteItemRowViewModel { Kind: RemoteItemKind.File or RemoteItemKind.Folder } row:
                path = row.Path;
                name = row.Name;
                kind = row.Kind;
                return true;
            case RemoteListRowViewModel { Kind: RemoteItemKind.File or RemoteItemKind.Folder } row:
                path = row.Path;
                name = row.Name;
                kind = row.Kind;
                return true;
            default:
                path = RemotePath.Root;
                name = string.Empty;
                kind = default;
                return false;
        }
    }

    private static bool TryGetRenameableRow(
        object? parameter,
        out RemotePath path,
        out string name,
        out RemoteItemKind kind)
    {
        switch (parameter)
        {
            case RemoteItemRowViewModel { Kind: RemoteItemKind.File or RemoteItemKind.Folder } row:
                path = row.Path;
                name = row.Name;
                kind = row.Kind;
                return true;
            case RemoteListRowViewModel { Kind: RemoteItemKind.File or RemoteItemKind.Folder } row:
                path = row.Path;
                name = row.Name;
                kind = row.Kind;
                return true;
            default:
                path = RemotePath.Root;
                name = string.Empty;
                kind = default;
                return false;
        }
    }

    private async Task AddAccountAsync()
    {
        var category = ToProviderCategory(_resourceGroup);
        var preferredProviderId = _resourceGroup == NavigationResourceGroup.WebDav
            ? new StorageProviderId("webdav")
            : (StorageProviderId?)null;
        var result = await _accountDialogWorkflow.AddAccountAsync(category, preferredProviderId).ConfigureAwait(true);
        if (result.IsFailure)
        {
            StatusMessage = Localization.GetString("RemoteBrowser", "AccountSaveFailed");
            SetErrorDetails(StatusMessage, StatusMessage, result.Error);
            return;
        }

        var account = result.GetValueOrThrow();
        if (account is null)
        {
            return;
        }

        _storageAccountId = account.Id;
        _remotePath = GetInitialPath(account);
        ResetPaging();
        await LoadItemsAsync().ConfigureAwait(true);
    }

    private RemotePath GetUploadTargetPath(LocalPath firstLocalPath)
    {
        var fileName = firstLocalPath.GetFileName();
        return _remotePath.IsRoot
            ? new RemotePath(fileName)
            : _remotePath.Combine(fileName);
    }

    private async Task NavigateToTransferStatusAsync(CreateTransferTasksResult createdTasks)
    {
        var createdTaskIds = createdTasks.Tasks.Select(task => task.Id).ToHashSet();
        var queueResult = await _transfers.GetQueueAsync(new GetTransferQueueRequest()).ConfigureAwait(true);
        if (queueResult.IsSuccess &&
            queueResult.GetValueOrThrow().Tasks.Any(snapshot => createdTaskIds.Contains(snapshot.Task.Id)))
        {
            await _navigation.NavigateAsync(NavigationTarget.TransferQueue).ConfigureAwait(true);
            return;
        }

        await _navigation.NavigateAsync(NavigationTarget.TransferHistory, new TransferHistoryNavigationParameter(1)).ConfigureAwait(true);
    }

    private string BuildDuplicateUploadMessage(IReadOnlyList<PreparedUploadTaskTarget> targets)
    {
        var lines = new List<string>
        {
            targets.Count == 1
                ? Localization.GetString("RemoteBrowser", "DuplicateSingleMessage")
                : Localization.Format("RemoteBrowser", "DuplicateMultipleMessage", targets.Count),
            string.Empty
        };

        foreach (var target in targets.Take(5))
        {
            var record = target.HistoricalRecords
                .OrderByDescending(item => item.LastSeenAt ?? item.UploadedAt)
                .ThenByDescending(item => item.UploadedAt)
                .First();
            lines.Add($"{target.LocalPath.GetFileName()} -> {FormatRemotePath(record.RemotePath)}");
        }

        if (targets.Count > 5)
        {
            lines.Add(Localization.Format("RemoteBrowser", "MoreDuplicateFiles", targets.Count - 5));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatRemotePath(RemotePath path)
    {
        if (path.IsRoot)
        {
            return path.Kind == RemotePathKind.Root
                ? Localization.GetString("RemoteBrowser", "BucketListRoot")
                : "/";
        }

        return path.Kind switch
        {
            RemotePathKind.BucketRoot => $"oss://{path.Value}",
            RemotePathKind.Folder => $"/{path.Value.TrimEnd('/')}/",
            _ => $"/{path.Value}"
        };
    }

    private static StorageProviderCategory? ToProviderCategory(NavigationResourceGroup resourceGroup)
    {
        return resourceGroup switch
        {
            NavigationResourceGroup.ObjectStorage => StorageProviderCategory.ObjectStorage,
            NavigationResourceGroup.FileTransfer => StorageProviderCategory.FileTransfer,
            NavigationResourceGroup.WebDav => StorageProviderCategory.FileTransfer,
            _ => null
        };
    }

    private static bool IsFtpOrSftp(StorageAccountSummary account)
    {
        return account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            (account.ProviderId.Value.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
             account.ProviderId.Value.Equals("sftp", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWebDav(StorageAccountSummary account)
    {
        return account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase);
    }

    private static RemotePath GetInitialPath(StorageAccountSummary account)
    {
        if (account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("sftp", StringComparison.OrdinalIgnoreCase) &&
            account.ProviderConfig.TryGetValue("homePath", out var homePath) &&
            !string.IsNullOrWhiteSpace(homePath) &&
            homePath.Trim() != "/")
        {
            return new RemotePath(homePath, RemotePathKind.Folder);
        }

        return RemotePath.Root;
    }

    private Task ShowErrorDetailsAsync()
    {
        return _lastErrorDetails is null
            ? Task.CompletedTask
            : _dialogs.ShowErrorDetailsAsync(_lastErrorDetails);
    }

    private void SetErrorDetails(
        string title,
        string fallbackSummary,
        StorageError? error,
        IReadOnlyDictionary<string, string>? details = null)
    {
        _lastErrorDetails = ErrorDialogRequest.FromError(title, fallbackSummary, error, details);
        OnPropertyChanged(nameof(HasErrorDetails));
        OnPropertyChanged(nameof(IsStatusBarMessageVisible));
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
        OnPropertyChanged(nameof(IsStatusBarMessageVisible));
        ShowErrorDetailsCommand.RaiseCanExecuteChanged();
    }
}

public sealed record RemoteItemRowViewModel(
    RemotePath Path,
    Icon Icon,
    string Name,
    long? SizeBytes,
    string Size,
    string UpdatedAt,
    RemoteItemKind Kind,
    ICommand OpenCommand,
    ICommand PreviewCommand,
    ICommand DownloadCommand,
    ICommand RenameCommand,
    ICommand DeleteCommand,
    bool CanPreview,
    bool CanRename,
    bool CanDelete)
{
    public bool IsFile => Kind == RemoteItemKind.File;

    public bool IsFolderLike => Kind is RemoteItemKind.Folder or RemoteItemKind.Bucket;

    public static RemoteItemRowViewModel From(
        RemoteItem item,
        ICommand openCommand,
        ICommand previewCommand,
        ICommand downloadCommand,
        ICommand renameCommand,
        ICommand deleteCommand,
        bool canPreview,
        bool canRename,
        bool canDelete)
    {
        return new RemoteItemRowViewModel(
            item.Path,
            RemoteItemIconFactory.Create(item),
            item.Name,
            item.Size,
            FormatSize(item.Size, item.Kind),
            item.UpdatedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            item.Kind,
            openCommand,
            previewCommand,
            downloadCommand,
            renameCommand,
            deleteCommand,
            canPreview,
            canRename,
            canDelete);
    }

    private static string FormatSize(long? size, RemoteItemKind kind)
    {
        if (kind is RemoteItemKind.Folder or RemoteItemKind.Bucket || size is null)
        {
            return "-";
        }

        var value = (double)size.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}

public sealed record RemoteListRowViewModel(
    RemotePath Path,
    Icon Icon,
    string Name,
    long? SizeBytes,
    string Size,
    string UpdatedAt,
    RemoteItemKind Kind,
    ICommand OpenCommand,
    ICommand PreviewCommand,
    ICommand DownloadCommand,
    ICommand RenameCommand,
    ICommand DeleteCommand,
    bool CanPreview,
    bool CanRename,
    bool CanDelete,
    Action SelectionChanged) : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsFile => Kind == RemoteItemKind.File;

    public bool IsFolderLike => Kind is RemoteItemKind.Folder or RemoteItemKind.Bucket;

    public bool CanSelect => IsFile;

    public bool IsBatchSelectionVisible =>
        RemoteBrowserFeatureFlags.BatchFileSelectionEnabled && CanSelect;

    public GridLength BatchSelectionColumnWidth =>
        RemoteBrowserFeatureFlags.BatchFileSelectionEnabled ? new GridLength(44) : new GridLength(0);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var nextValue = IsFile && value;
            if (_isSelected == nextValue)
            {
                return;
            }

            _isSelected = nextValue;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            SelectionChanged();
        }
    }

    public RemoteListRowViewModel WithPathAndName(RemotePath path, string name)
    {
        var renamedRow = new RemoteListRowViewModel(
            path,
            Icon,
            name,
            SizeBytes,
            Size,
            UpdatedAt,
            Kind,
            OpenCommand,
            PreviewCommand,
            DownloadCommand,
            RenameCommand,
            DeleteCommand,
            CanPreview,
            CanRename,
            CanDelete,
            SelectionChanged);
        renamedRow._isSelected = _isSelected;
        return renamedRow;
    }

    public static RemoteListRowViewModel From(
        RemoteItem item,
        ICommand openCommand,
        ICommand previewCommand,
        ICommand downloadCommand,
        ICommand renameCommand,
        ICommand deleteCommand,
        bool canPreview,
        bool canRename,
        bool canDelete,
        Action selectionChanged)
    {
        return new RemoteListRowViewModel(
            item.Path,
            RemoteItemIconFactory.Create(item),
            item.Name,
            item.Size,
            FormatSize(item.Size, item.Kind),
            item.UpdatedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            item.Kind,
            openCommand,
            previewCommand,
            downloadCommand,
            renameCommand,
            deleteCommand,
            canPreview,
            canRename,
            canDelete,
            selectionChanged);
    }

    private static string FormatSize(long? size, RemoteItemKind kind)
    {
        if (kind is RemoteItemKind.Folder or RemoteItemKind.Bucket || size is null)
        {
            return "-";
        }

        var value = (double)size.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.#} {units[unitIndex]}";
    }
}

internal static class RemoteBrowserFeatureFlags
{
    public const bool BatchFileSelectionEnabled = false;
}

internal static class RemoteItemIconFactory
{
    public static Icon Create(RemoteItem item)
    {
        var icon = item.Kind switch
        {
            RemoteItemKind.Bucket => new FolderOutlined(),
            RemoteItemKind.Folder => new FolderOutlined(),
            RemoteItemKind.File => CreateFileIcon(item.Name),
            _ => new FileUnknownOutlined()
        };

        icon.Width = 16;
        icon.Height = 16;
        return icon;
    }

    private static Icon CreateFileIcon(string name)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => new FileJpgOutlined(),
            ".gif" => new FileGifOutlined(),
            ".png" or ".bmp" or ".webp" or ".svg" => new FileImageOutlined(),
            ".pdf" => new FilePdfOutlined(),
            ".doc" or ".docx" => new FileWordOutlined(),
            ".xls" or ".xlsx" or ".csv" => new FileExcelOutlined(),
            ".ppt" or ".pptx" => new FilePptOutlined(),
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => new FileZipOutlined(),
            ".txt" or ".log" => new FileTextOutlined(),
            ".md" or ".markdown" => new FileMarkdownOutlined(),
            ".cs" or ".js" or ".ts" or ".html" or ".css" or ".xml" or ".json" or ".yaml" or ".yml" or ".py" or ".java" or ".go" or ".rs" or ".cpp" or ".c" or ".h" => new CodeOutlined(),
            ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" => new AudioOutlined(),
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".wmv" => new VideoCameraOutlined(),
            ".exe" or ".msi" => new WindowsOutlined(),
            _ => new FileUnknownOutlined()
        };
    }
}

public sealed record RemoteAccountChoiceViewModel(
    StorageAccountId Id,
    string DisplayName,
    string ProviderId,
    StorageProviderCategory ProviderCategory,
    string ProviderTag,
    string ProviderTagColor,
    string Endpoint,
    RemotePath InitialPath)
{
    public static RemoteAccountChoiceViewModel From(StorageAccountSummary account)
    {
        return new RemoteAccountChoiceViewModel(
            account.Id,
            account.DisplayName,
            account.ProviderId.ToString(),
            account.ProviderCategory,
            FormatProviderTag(account),
            FormatProviderTagColor(account.ProviderCategory),
            string.IsNullOrWhiteSpace(account.Endpoint) ? "-" : account.Endpoint,
            GetInitialPath(account));
    }

    private static string FormatProviderTag(StorageAccountSummary account)
    {
        return account.ProviderCategory switch
        {
            StorageProviderCategory.ObjectStorage => account.ProviderId.Value.ToUpperInvariant(),
            StorageProviderCategory.FileTransfer => account.ProviderId.Value.ToUpperInvariant(),
            _ => account.ProviderId.ToString()
        };
    }

    private static string FormatProviderTagColor(StorageProviderCategory category)
    {
        return category switch
        {
            StorageProviderCategory.ObjectStorage => "blue",
            StorageProviderCategory.FileTransfer => "green",
            _ => "default"
        };
    }

    private static RemotePath GetInitialPath(StorageAccountSummary account)
    {
        if (account.ProviderCategory == StorageProviderCategory.FileTransfer &&
            account.ProviderId.Value.Equals("sftp", StringComparison.OrdinalIgnoreCase) &&
            account.ProviderConfig.TryGetValue("homePath", out var homePath) &&
            !string.IsNullOrWhiteSpace(homePath) &&
            homePath.Trim() != "/")
        {
            return new RemotePath(homePath, RemotePathKind.Folder);
        }

        return RemotePath.Root;
    }
}

public sealed record BucketChoiceViewModel(string Name, RemotePath Path, string Size, string UpdatedAt)
{
    public static BucketChoiceViewModel From(RemoteItem item)
    {
        return new BucketChoiceViewModel(
            item.Name,
            item.Path,
            "-",
            item.UpdatedAt?.LocalDateTime.ToString("yyyy-MM-dd HH:mm") ?? string.Empty);
    }
}

