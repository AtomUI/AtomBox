using System.Collections.ObjectModel;
using System.ComponentModel;
using AtomUI;
using AtomUI.Controls;
using AtomUI.Icons.AntDesign;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.ViewFactory;
using AtomUI.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AtomToolTip = AtomUI.Desktop.Controls.ToolTip;

namespace AtomBox.Desktop.Shell;

public sealed partial class MainWindow : AtomUI.Desktop.Controls.Window
{
    private static readonly bool IsNavigationTooltipEnabled = false;
    private const int NavigationTooltipDisplayLengthThreshold = 12;
    private const double NavigationMinPaneWidth = 220;
    private const double NavigationMaxPaneWidth = 420;
    private const double NavigationBaseChromeWidth = 112;
    private const double NavigationDisplayUnitWidth = 7.5;
    private static readonly Uri TrayIconUri = new("avares://AtomBox.Desktop/Assets/logo.ico");
    private readonly MainWindowViewModel? _viewModel;
    private readonly IDesktopPreferencesService? _preferences;
    private TrayIcon? _trayIcon;
    private TrayIcons? _trayIcons;
    private bool _isExitRequested;
    private NavMenu? _navMenu;
    private NavMenuNode? _remoteRootNode;
    private NavMenuNode? _ossRootNode;
    private NavMenuNode? _fileTransferRootNode;
    private NavMenuNode? _webDavRootNode;
    private NavMenuNode? _transferRootNode;
    private Border? _navigationPane;
    private readonly Dictionary<string, NavMenuNode> _navigationNodesByKey = new(StringComparer.Ordinal);
    private string? _pendingNavigationMenuKey;

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(
        MainWindowViewModel viewModel,
        IViewFactory viewFactory,
        IDesktopPreferencesService preferences)
    {
        _viewModel = viewModel;
        _preferences = preferences;
        InitializeComponent();
        DataContext = viewModel;

        var content = this.FindControl<ContentControl>("MainContent")
            ?? throw new InvalidOperationException("Main content control was not found.");
        content.ContentTemplate = new ViewFactoryDataTemplate(viewFactory);

        var navMenu = this.FindControl<NavMenu>("MainNavigationMenu")
            ?? throw new InvalidOperationException("Main navigation menu was not found.");
        _navMenu = navMenu;
        _navigationPane = this.FindControl<Border>("NavigationPane")
            ?? throw new InvalidOperationException("Navigation pane was not found.");
        BuildNavigationMenu(navMenu);
        viewModel.RemoteAccountMenuRefreshed += HandleRemoteAccountMenuRefreshed;
        viewModel.CurrentMenuKeyChanged += HandleCurrentMenuKeyChanged;
        viewModel.StatusBar.PropertyChanged += HandleStatusBarPropertyChanged;
        navMenu.NavMenuItemClick += HandleNavMenuItemClick;
        Closing += HandleClosing;
        EnsureTrayIcon();
        SyncNavigationSelection(viewModel.CurrentMenuKey);
        ScheduleNavigationTooltips();
    }

    private void HandleClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_preferences is null || _isExitRequested)
        {
            return;
        }

        var preferences = _preferences.GetAsync().GetAwaiter().GetResult();
        if (preferences.CloseWindowBehavior != CloseWindowBehavior.MinimizeApplication)
        {
            return;
        }

        e.Cancel = true;
        EnsureTrayIcon();
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = true;
        }

        Hide();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        var showItem = new NativeMenuItem
        {
            Header = "显示 AtomBox",
            IsEnabled = true,
            IsVisible = true
        };
        showItem.Click += (_, _) => ShowFromTray();

        var exitItem = new NativeMenuItem
        {
            Header = "退出 AtomBox",
            IsEnabled = true,
            IsVisible = true
        };
        exitItem.Click += (_, _) => ExitFromTray();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(TrayIconUri)),
            ToolTipText = "AtomBox",
            Menu = menu,
            IsVisible = false
        };
        _trayIcon.Clicked += (_, _) => ShowFromTray();

        if (Avalonia.Application.Current is not null)
        {
            _trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(Avalonia.Application.Current, _trayIcons);
        }
    }

    private void ShowFromTray()
    {
        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
        }

        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        _trayIcon?.Dispose();
        if (Avalonia.Application.Current is not null)
        {
            TrayIcon.SetIcons(Avalonia.Application.Current, null);
        }

        _trayIcons = null;
        _trayIcon = null;
        Close();
    }

    private async void HandleNavMenuItemClick(object? sender, NavMenuItemClickEventArgs args)
    {
        if (_viewModel is null)
        {
            return;
        }

        var itemKey = args.NavMenuItem.ItemKey?.Value;
        try
        {
            if (!ShouldSkipNavigationForMenuClick(args.NavMenuItem, itemKey, _viewModel.CurrentMenuKey))
            {
                await _viewModel.NavigateByMenuKeyAsync(itemKey).ConfigureAwait(true);
            }
            ScheduleNavigationTooltips();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }



    private static bool ShouldSkipNavigationForMenuClick(INavMenuItem item, string? itemKey, string? currentMenuKey)
    {
        if (string.Equals(itemKey, currentMenuKey, StringComparison.Ordinal))
        {
            return true;
        }

        return item.HasSubMenu && IsRemoteStorageGroupMenuKey(itemKey);
    }

    private static bool IsRemoteStorageGroupMenuKey(string? itemKey)
    {
        return string.Equals(itemKey, NavigationMenuKeys.RemoteStorage, StringComparison.Ordinal) ||
            string.Equals(itemKey, NavigationMenuKeys.RemoteObjectStorage, StringComparison.Ordinal) ||
            string.Equals(itemKey, NavigationMenuKeys.RemoteFileTransfer, StringComparison.Ordinal) ||
            string.Equals(itemKey, NavigationMenuKeys.RemoteWebDav, StringComparison.Ordinal);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void BuildNavigationMenu(NavMenu navMenu)
    {
        _navigationNodesByKey.Clear();
        _remoteRootNode = RegisterNavigationNode(new NavMenuNode
        {
            Header = "远程存储",
            Icon = new CloudOutlined(),
            ItemKey = new EntityKey(NavigationMenuKeys.RemoteStorage)
        });
        _ossRootNode = RegisterNavigationNode(new NavMenuNode
        {
            Header = "OSS",
            Icon = new CloudServerOutlined(),
            ItemKey = new EntityKey(NavigationMenuKeys.RemoteObjectStorage)
        });
        _fileTransferRootNode = RegisterNavigationNode(new NavMenuNode
        {
            Header = "FTP/SFTP",
            Icon = new FolderOpenOutlined(),
            ItemKey = new EntityKey(NavigationMenuKeys.RemoteFileTransfer)
        });
        _webDavRootNode = RegisterNavigationNode(new NavMenuNode
        {
            Header = "WebDAV",
            Icon = new ApiOutlined(),
            ItemKey = new EntityKey(NavigationMenuKeys.RemoteWebDav)
        });

        navMenu.Items.Clear();
        navMenu.Items.Add(RegisterNavigationNode(new NavMenuNode
        {
            Header = "首页",
            Icon = new HomeOutlined(),
            ItemKey = new EntityKey(NavigationMenuKeys.Home)
        }));
        navMenu.Items.Add(_remoteRootNode);
        AttachRemoteCategoryNodes();
        _transferRootNode = new NavMenuNode
        {
            Header = "传输",
            Icon = new CloudSyncOutlined(),
            Children =
            {
                RegisterNavigationNode(new NavMenuNode
                {
                    Header = "传输队列",
                    HeaderTemplate = CreateTransferQueueHeaderTemplate(_viewModel?.StatusBar ??
                        throw new InvalidOperationException("Main window view model was not initialized.")),
                    Icon = new SwapOutlined(),
                    ItemKey = new EntityKey(NavigationMenuKeys.TransferQueue)
                }),
                RegisterNavigationNode(new NavMenuNode
                {
                    Header = "历史记录",
                    Icon = new HistoryOutlined(),
                    ItemKey = new EntityKey(NavigationMenuKeys.TransferHistory)
                })
            }
        };
        navMenu.Items.Add(_transferRootNode);
        navMenu.Items.Add(new NavMenuNode
        {
            Header = "设置",
            Icon = new SettingOutlined(),
            Children =
            {
                RegisterNavigationNode(new NavMenuNode
                {
                    Header = "应用设置",
                    Icon = new ToolOutlined(),
                    ItemKey = new EntityKey(NavigationMenuKeys.Settings)
                }),
                RegisterNavigationNode(new NavMenuNode
                {
                    Header = "账号管理",
                    Icon = new UserSwitchOutlined(),
                    ItemKey = new EntityKey(NavigationMenuKeys.AccountManagement)
                })
            }
        });

        RebuildRemoteAccountNodes();
    }

    private static FuncDataTemplate<NavMenuNode> CreateTransferQueueHeaderTemplate(StatusBarViewModel statusBar)
    {
        return new FuncDataTemplate<NavMenuNode>((_, _) =>
        {
            var panel = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var title = new Avalonia.Controls.TextBlock
            {
                Text = "传输队列",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(title, 0);
            panel.Children.Add(title);

            var badge = new CountBadge
            {
                Margin = new Thickness(8, 0, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Size = AtomUI.Controls.Commons.CountBadgeSize.Small,
                OverflowCount = 99,
                IsZeroVisible = false
            };
            badge.Bind(CountBadge.CountProperty, new Binding(nameof(StatusBarViewModel.ActiveTransferCount))
            {
                Source = statusBar
            });
            Grid.SetColumn(badge, 1);
            panel.Children.Add(badge);

            return panel;
        });
    }

    private void HandleStatusBarPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(StatusBarViewModel.ActiveTransferCount), StringComparison.Ordinal) ||
            _viewModel?.StatusBar.ActiveTransferCount <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(ExpandTransferMenu, DispatcherPriority.Background);
    }

    private void ExpandTransferMenu()
    {
        if (_navMenu is null || _transferRootNode is null)
        {
            return;
        }

        foreach (var item in _navMenu.GetVisualDescendants().OfType<INavMenuItem>())
        {
            if (ReferenceEquals(item.Node, _transferRootNode))
            {
                item.Open();
                return;
            }
        }
    }

    private void AttachRemoteCategoryNodes()
    {
        if (_remoteRootNode is null ||
            _ossRootNode is null ||
            _fileTransferRootNode is null ||
            _webDavRootNode is null)
        {
            return;
        }

        if (!_remoteRootNode.Children.Contains(_ossRootNode))
        {
            _remoteRootNode.Children.Add(_ossRootNode);
        }

        if (!_remoteRootNode.Children.Contains(_fileTransferRootNode))
        {
            _remoteRootNode.Children.Add(_fileTransferRootNode);
        }

        if (!_remoteRootNode.Children.Contains(_webDavRootNode))
        {
            _remoteRootNode.Children.Add(_webDavRootNode);
        }
    }

    private void HandleRemoteAccountMenuRefreshed(object? sender, EventArgs e)
    {
        RebuildRemoteAccountNodes();
    }

    private void RebuildRemoteAccountNodes()
    {
        if (_viewModel is null ||
            _remoteRootNode is null ||
            _ossRootNode is null ||
            _fileTransferRootNode is null ||
            _webDavRootNode is null)
        {
            return;
        }

        UnregisterRemoteNavigationNodes();
        _ossRootNode.Children.Clear();
        _fileTransferRootNode.Children.Clear();
        _webDavRootNode.Children.Clear();

        PopulateAccountNodes(_ossRootNode, _viewModel.ObjectStorageAccounts, "暂无 OSS 账号", () => new CloudOutlined());
        PopulateAccountNodes(_fileTransferRootNode, _viewModel.FileTransferAccounts, "暂无 FTP/SFTP 账号", () => new FolderOutlined());
        PopulateAccountNodes(_webDavRootNode, _viewModel.WebDavAccounts, "暂无 WebDAV 账号", () => new ApiOutlined());
        AttachRemoteCategoryNodes();
        ApplyNavigationMinimumWidth();
        SyncNavigationSelection(_pendingNavigationMenuKey ?? _viewModel.CurrentMenuKey);
        ScheduleNavigationTooltips();
    }

    private void PopulateAccountNodes(
        NavMenuNode rootNode,
        ObservableCollection<RemoteAccountNavigationItem> accounts,
        string emptyText,
        Func<PathIcon> createAccountIcon)
    {
        if (accounts.Count == 0)
        {
            rootNode.Children.Add(new NavMenuNode
            {
                Header = emptyText,
                Icon = new InfoCircleOutlined(),
                IsEnabled = false
            });
            return;
        }

        foreach (var account in accounts)
        {
            rootNode.Children.Add(RegisterNavigationNode(new NavMenuNode
            {
                Header = account.Header,
                Icon = createAccountIcon(),
                ItemKey = new EntityKey(account.ItemKey)
            }));
        }
    }
    private void HandleCurrentMenuKeyChanged(object? sender, EventArgs e)
    {
        SyncNavigationSelection(_viewModel?.CurrentMenuKey);
    }

    private NavMenuNode RegisterNavigationNode(NavMenuNode node)
    {
        if (node.ItemKey?.Value is { } key)
        {
            _navigationNodesByKey[key] = node;
        }

        return node;
    }

    private void UnregisterRemoteNavigationNodes()
    {
        foreach (var key in _navigationNodesByKey.Keys
                     .Where(key => key == NavigationMenuKeys.AddRemote ||
                                   key.StartsWith(NavigationMenuKeys.RemoteAccountPrefix, StringComparison.Ordinal))
                     .ToArray())
        {
            _navigationNodesByKey.Remove(key);
        }
    }

    private void SyncNavigationSelection(string? itemKey)
    {
        if (_navMenu is null || string.IsNullOrWhiteSpace(itemKey))
        {
            _pendingNavigationMenuKey = null;
            return;
        }

        if (!_navigationNodesByKey.TryGetValue(itemKey, out var node) || !node.IsEnabled)
        {
            _pendingNavigationMenuKey = itemKey;
            return;
        }

        _pendingNavigationMenuKey = null;
        _navMenu.SelectedItem = node;
        ScheduleNavigationTooltips();
    }

    private void ApplyNavigationMinimumWidth()
    {
        if (_navigationPane is null)
        {
            return;
        }

        var maxDisplayLength = EnumerateNavigationHeaders()
            .Select(GetDisplayLength)
            .DefaultIfEmpty(0)
            .Max();
        var targetWidth = Math.Clamp(
            NavigationBaseChromeWidth + maxDisplayLength * NavigationDisplayUnitWidth,
            NavigationMinPaneWidth,
            NavigationMaxPaneWidth);

        var targetDimension = new Dimension(targetWidth);
        Splitter.SetMinSize(_navigationPane, targetDimension);
        var currentSize = Splitter.GetSize(_navigationPane);
        if (currentSize is null || currentSize.Value.Value < targetWidth)
        {
            Splitter.SetSize(_navigationPane, targetDimension);
        }
    }

    private IEnumerable<string> EnumerateNavigationHeaders()
    {
        foreach (var header in EnumerateNavigationHeaders(_remoteRootNode))
        {
            yield return header;
        }

        var staticHeaders = new[]
        {
            "首页",
            "传输",
            "传输队列",
            "历史记录",
            "设置",
            "应用设置",
            "账号管理"
        };
        foreach (var header in staticHeaders)
        {
            yield return header;
        }
    }

    private static IEnumerable<string> EnumerateNavigationHeaders(NavMenuNode? node)
    {
        if (node is null)
        {
            yield break;
        }

        if (node.Header is string header && !string.IsNullOrWhiteSpace(header))
        {
            yield return header;
        }

        foreach (var child in node.Children.OfType<NavMenuNode>())
        {
            foreach (var childHeader in EnumerateNavigationHeaders(child))
            {
                yield return childHeader;
            }
        }
    }

    private void ScheduleNavigationTooltips()
    {
        if (!IsNavigationTooltipEnabled)
        {
            Dispatcher.UIThread.Post(DisableNavigationTooltips, DispatcherPriority.Background);
            return;
        }

        Dispatcher.UIThread.Post(ApplyNavigationTooltips, DispatcherPriority.Background);
    }

    private void DisableNavigationTooltips()
    {
        if (_navMenu is null)
        {
            return;
        }

        foreach (var control in _navMenu.GetVisualDescendants().OfType<Control>())
        {
            if (IsNavMenuHeaderControl(control))
            {
                DisableNavigationTooltip(control);
            }
        }
    }

    private void ApplyNavigationTooltips()
    {
        if (_navMenu is null)
        {
            return;
        }

        foreach (var control in _navMenu.GetVisualDescendants().OfType<Control>())
        {
            if (!IsNavMenuHeaderControl(control))
            {
                continue;
            }

            if (TryGetNavMenuHeaderText(control, out var header))
            {
                ConfigureNavigationTooltip(control, header);
            }
            else
            {
                DisableNavigationTooltip(control);
            }
        }
    }

    private static bool IsNavMenuHeaderControl(Control control)
    {
        var typeName = control.GetType().FullName;
        return string.Equals(typeName, "AtomUI.Desktop.Controls.InlineNavMenuItemHeader", StringComparison.Ordinal) ||
               string.Equals(typeName, "AtomUI.Desktop.Controls.VerticalNavMenuItemHeader", StringComparison.Ordinal) ||
               string.Equals(typeName, "AtomUI.Desktop.Controls.HorizontalNavMenuItemHeader", StringComparison.Ordinal);
    }

    private static void ConfigureNavigationTooltip(Control control, string header)
    {
        if (!control.IsEnabled || GetDisplayLength(header) <= NavigationTooltipDisplayLengthThreshold)
        {
            DisableNavigationTooltip(control);
            return;
        }

        AtomToolTip.SetServiceEnabled(control, true);
        AtomToolTip.SetTip(control, new AtomUI.Desktop.Controls.TextBlock
        {
            Text = header,
            FontSize = 11,
            TextWrapping = Avalonia.Media.TextWrapping.NoWrap,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
            MaxWidth = 260
        });
        AtomToolTip.SetPlacement(control, PlacementMode.Top);
        AtomToolTip.SetMarginToAnchor(control, 2);
        AtomToolTip.SetShowDelay(control, 550);
        AtomToolTip.SetIsArrowVisible(control, true);
    }

    private static void DisableNavigationTooltip(Control control)
    {
        AtomToolTip.SetTip(control, null);
        AtomToolTip.SetServiceEnabled(control, false);
    }

    private static int GetDisplayLength(string value)
    {
        var length = 0;
        foreach (var character in value)
        {
            length += character <= 0x7F ? 1 : 2;
        }

        return length;
    }

    private static bool TryGetNavMenuHeaderText(Control control, out string header)
    {
        header = string.Empty;
        var headerValue = control.GetType().GetProperty("Header")?.GetValue(control);
        if (headerValue is NavMenuNode { Header: string nodeHeader } &&
            !string.IsNullOrWhiteSpace(nodeHeader))
        {
            header = nodeHeader;
            return true;
        }

        if (headerValue is string directHeader && !string.IsNullOrWhiteSpace(directHeader))
        {
            header = directHeader;
            return true;
        }

        return false;
    }
}
