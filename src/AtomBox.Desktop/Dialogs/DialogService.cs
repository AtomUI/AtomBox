using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AtomBox.Application.Accounts;
using AtomBox.Core.Accounts;
using AtomBox.Core.Capabilities;
using AtomBox.Core.Previews;
using AtomBox.Core.Providers;
using AtomBox.Core.Results;
using AtomBox.Core.ValueObjects;
using AtomBox.Desktop.Services;
using AtomUI;
using AtomUI.Controls;
using AtomButton = AtomUI.Desktop.Controls.Button;
using AtomButtonType = AtomUI.Desktop.Controls.ButtonType;
using AtomComboBox = AtomUI.Desktop.Controls.ComboBox;
using AtomCheckBox = AtomUI.Desktop.Controls.CheckBox;
using AtomDialog = AtomUI.Desktop.Controls.Dialog;
using AtomDialogCode = AtomUI.Desktop.Controls.DialogCode;
using AtomDialogHorizontalAnchor = AtomUI.Desktop.Controls.DialogHorizontalAnchor;
using AtomDialogHostType = AtomUI.Desktop.Controls.DialogHostType;
using AtomDialogOptions = AtomUI.Desktop.Controls.DialogOptions;
using AtomDialogStandardButton = AtomUI.Desktop.Controls.DialogStandardButton;
using AtomDialogStandardButtons = AtomUI.Desktop.Controls.DialogStandardButtons;
using AtomDialogVerticalAnchor = AtomUI.Desktop.Controls.DialogVerticalAnchor;
using AtomDialogButton = AtomUI.Desktop.Controls.DialogButton;
using AtomDialogButtonRole = AtomUI.Desktop.Controls.DialogButtonRole;
using AtomLineEdit = AtomUI.Desktop.Controls.LineEdit;
using AtomTextArea = AtomUI.Desktop.Controls.TextArea;

namespace AtomBox.Desktop.Dialogs;

public sealed class DialogService : IDialogService
{
    public async Task<AccountDialogResult?> ShowAccountDialogAsync(AccountDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (GetMainWindow() is not { } mainWindow)
        {
            return null;
        }

        try
        {
            var content = new AccountDialogWindow(request);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var result = await AtomDialog.ShowDialogModalAsync(
                content,
                null,
                new AtomDialogOptions
                {
                    Title = request.Title,
                    DialogHostType = AtomDialogHostType.Overlay,
                    IsResizable = false,
                    IsClosable = true,
                    IsDragMovable = true,
                    IsMaximizable = false,
                    IsMinimizable = false,
                    StandardButtons = AtomDialogStandardButtons.Parse("Cancel, Save"),
                    DefaultStandardButton = AtomDialogStandardButton.Save,
                    HorizontalStartupLocation = AtomDialogHorizontalAnchor.Center,
                    VerticalStartupLocation = AtomDialogVerticalAnchor.Center,
                    HostWidth = 680,
                    HostHeight = 720,
                    HostMinWidth = 680,
                    HostMinHeight = 640,
                    HostMaxWidth = 760,
                    HostMaxHeight = 820
                },
                mainWindow).ConfigureAwait(true);

            return result is AtomDialogCode.Accepted ? content.BuildResult() : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            await ShowErrorDetailsOverlayAsync(new ErrorDialogRequest(
                "账号弹窗打开失败",
                $"账号弹窗打开失败：{ex.Message}",
                Details: new Dictionary<string, string>
                {
                    ["异常类型"] = ex.GetType().FullName ?? ex.GetType().Name,
                    ["阶段"] = request.ExistingAccount is null ? "新增账号" : "编辑账号",
                    ["Provider"] = request.ExistingAccount?.ProviderId.ToString() ?? "-"
                }), mainWindow).ConfigureAwait(true);
            return null;
        }
    }

    public async Task<bool> ConfirmAsync(ConfirmDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (GetMainWindow() is not { } mainWindow)
        {
            return false;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var result = await AtomDialog.ShowDialogModalAsync(
                new ConfirmDialogContent(request),
                null,
                new AtomDialogOptions
                {
                    Title = request.Title,
                    DialogHostType = AtomDialogHostType.Overlay,
                    IsResizable = false,
                    IsClosable = true,
                    IsDragMovable = true,
                    IsMaximizable = false,
                    IsMinimizable = false,
                    IsFooterVisible = false,
                    HorizontalStartupLocation = AtomDialogHorizontalAnchor.Center,
                    VerticalStartupLocation = AtomDialogVerticalAnchor.Center,
                    HostWidth = 420,
                    HostMinWidth = 380,
                    HostMinHeight = 220,
                    HostMaxWidth = 520,
                    HostMaxHeight = 420
                },
                mainWindow).ConfigureAwait(true);

            return result is true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return false;
        }
    }

    public async Task<string?> ShowTextInputAsync(TextInputDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (GetMainWindow() is not { } mainWindow)
        {
            return null;
        }

        try
        {
            var content = new TextInputDialogContent(request);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var result = await AtomDialog.ShowDialogModalAsync(
                content,
                null,
                new AtomDialogOptions
                {
                    Title = request.Title,
                    DialogHostType = AtomDialogHostType.Overlay,
                    IsResizable = false,
                    IsClosable = true,
                    IsDragMovable = true,
                    IsMaximizable = false,
                    IsMinimizable = false,
                    IsFooterVisible = false,
                    HorizontalStartupLocation = AtomDialogHorizontalAnchor.Center,
                    VerticalStartupLocation = AtomDialogVerticalAnchor.Center,
                    HostWidth = 460,
                    HostMinWidth = 420,
                    HostMinHeight = 240,
                    HostMaxWidth = 560,
                    HostMaxHeight = 340
                },
                mainWindow).ConfigureAwait(true);

            return result as string;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return null;
        }
    }

    public Task ShowErrorDetailsAsync(ErrorDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (GetMainWindow() is not { } mainWindow)
        {
            return Task.CompletedTask;
        }

        return ShowErrorDetailsOverlayAsync(request, mainWindow);
    }

    public async Task ShowPreviewAsync(PreviewDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (GetMainWindow() is not { } mainWindow)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await AtomDialog.ShowDialogModalAsync(
                new PreviewDialogContent(request),
                null,
                new AtomDialogOptions
                {
                    Title = $"预览 - {request.FileName}",
                    DialogHostType = AtomDialogHostType.Overlay,
                    IsResizable = true,
                    IsClosable = true,
                    IsDragMovable = true,
                    IsMaximizable = false,
                    IsMinimizable = false,
                    StandardButtons = AtomDialogStandardButtons.Parse("Close"),
                    DefaultStandardButton = AtomDialogStandardButton.Close,
                    HorizontalStartupLocation = AtomDialogHorizontalAnchor.Center,
                    VerticalStartupLocation = AtomDialogVerticalAnchor.Center,
                    HostWidth = request.Kind == RemotePreviewKind.Image ? 760 : 820,
                    HostHeight = 620,
                    HostMinWidth = 520,
                    HostMinHeight = 360,
                    HostMaxWidth = 1100,
                    HostMaxHeight = 860
                },
                mainWindow).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            await ShowErrorDetailsOverlayAsync(new ErrorDialogRequest(
                "预览打开失败",
                $"预览打开失败：{ex.Message}",
                Details: new Dictionary<string, string>
                {
                    ["文件"] = request.FileName,
                    ["类型"] = request.ContentType,
                    ["异常类型"] = ex.GetType().FullName ?? ex.GetType().Name
                }), mainWindow).ConfigureAwait(true);
        }
    }

    private static Window? GetMainWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }

    private sealed class TextInputDialogContent : UserControl
    {
        private readonly AtomLineEdit _input = new()
        {
            StyleVariant = InputControlStyleVariant.Outlined,
            SizeType = SizeType.Middle,
            MinHeight = 34
        };

        public TextInputDialogContent(TextInputDialogRequest request)
        {
            _input.Text = request.InitialValue;
            _input.PlaceholderText = request.Placeholder;

            var cancelButton = new AtomButton
            {
                Content = request.CancelText,
                ButtonType = AtomButtonType.Default,
                MinWidth = 88
            };
            cancelButton.Click += (_, _) => Close(null);

            var confirmButton = new AtomButton
            {
                Content = request.ConfirmText,
                ButtonType = AtomButtonType.Primary,
                MinWidth = 88
            };
            confirmButton.Click += (_, _) => Close(_input.Text?.Trim());

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancelButton, confirmButton }
            };

            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = request.Message,
                        TextWrapping = TextWrapping.Wrap
                    },
                    _input,
                    actions
                }
            };
        }

        private void Close(string? result)
        {
            if (TemplatedParent is AtomDialog dialog)
            {
                dialog.Done(result);
            }
        }
    }

    private sealed class ConfirmDialogContent : UserControl
    {
        public ConfirmDialogContent(ConfirmDialogRequest request)
        {
            var cancelButton = new AtomButton
            {
                Content = request.CancelText,
                ButtonType = AtomButtonType.Default,
                MinWidth = 88
            };
            cancelButton.Click += (_, _) => Close(false);

            var confirmButton = new AtomButton
            {
                Content = request.ConfirmText,
                ButtonType = AtomButtonType.Primary,
                MinWidth = 88
            };
            confirmButton.Click += (_, _) => Close(true);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Children = { cancelButton, confirmButton }
            };

            Content = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("*,Auto"),
                RowSpacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = request.Message,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalAlignment = VerticalAlignment.Top
                    },
                    actions
                }
            };
            Grid.SetRow(actions, 1);
        }

        private void Close(bool result)
        {
            if (TemplatedParent is AtomDialog dialog)
            {
                dialog.Done(result);
            }
        }
    }

    private static async Task ShowErrorDetailsOverlayAsync(ErrorDialogRequest request, TopLevel mainWindow)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await AtomDialog.ShowDialogModalAsync(
                new ErrorDetailsDialogContent(request),
                null,
                new AtomDialogOptions
                {
                    Title = request.Title,
                    DialogHostType = AtomDialogHostType.Overlay,
                    IsResizable = true,
                    IsClosable = true,
                    IsDragMovable = true,
                    IsMaximizable = false,
                    IsMinimizable = false,
                    StandardButtons = AtomDialogStandardButtons.Parse("Close"),
                    DefaultStandardButton = AtomDialogStandardButton.Close,
                    HorizontalStartupLocation = AtomDialogHorizontalAnchor.Center,
                    VerticalStartupLocation = AtomDialogVerticalAnchor.Center,
                    HostWidth = 560,
                    HostHeight = 420,
                    HostMinWidth = 480,
                    HostMinHeight = 320,
                    HostMaxWidth = 720,
                    HostMaxHeight = 640
                },
                mainWindow).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private sealed class ErrorDetailsDialogContent : UserControl
    {
        public ErrorDetailsDialogContent(ErrorDialogRequest request)
        {
            Content = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = request.Summary,
                        FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    BuildDetails(request)
                }
            };
        }

        private static Control BuildDetails(ErrorDialogRequest request)
        {
            var details = new StackPanel { Spacing = 8 };
            if (request.Error is { } error)
            {
                details.Children.Add(DetailRow("类别", error.Category.ToString()));
                details.Children.Add(DetailRow("错误码", error.Code.ToString()));
                details.Children.Add(DetailRow("可重试", error.IsRetryable ? "是" : "否"));
                if (!string.IsNullOrWhiteSpace(error.ProviderErrorCode))
                {
                    details.Children.Add(DetailRow("Provider 错误码", error.ProviderErrorCode));
                }
            }

            if (request.Details is not null)
            {
                foreach (var item in request.Details)
                {
                    details.Children.Add(DetailRow(item.Key, string.IsNullOrWhiteSpace(item.Value) ? "-" : item.Value));
                }
            }

            if (details.Children.Count == 0)
            {
                details.Children.Add(new TextBlock
                {
                    Text = "没有更多详情。",
                    Foreground = Brushes.DimGray
                });
            }

            return new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Child = new ScrollViewer { Content = details }
            };
        }

        private static Grid DetailRow(string label, string value)
        {
            var valueText = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap
            };

            return new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(132, GridUnitType.Pixel),
                    new ColumnDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        Foreground = Brushes.DimGray,
                        VerticalAlignment = VerticalAlignment.Top
                    },
                    valueText
                }
            }.WithInputColumn(valueText);
        }
    }

    private sealed class PreviewDialogContent : UserControl
    {
        public PreviewDialogContent(PreviewDialogRequest request)
        {
            var header = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(1, GridUnitType.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = request.FileName,
                        FontWeight = FontWeight.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = FormatPreviewMeta(request),
                        Foreground = Brushes.DimGray,
                        VerticalAlignment = VerticalAlignment.Center
                    }.WithPreviewMetaColumn()
                }
            };

            Content = new Grid
            {
                Margin = new Thickness(20),
                RowDefinitions = new RowDefinitions("Auto,*"),
                RowSpacing = 12,
                Children =
                {
                    header,
                    BuildPreviewContent(request).WithPreviewContentRow()
                }
            };
        }

        private static Control BuildPreviewContent(PreviewDialogRequest request)
        {
            if (request.Kind == RemotePreviewKind.Image)
            {
                using var stream = new MemoryStream(request.Content);
                return new Border
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Padding = new Thickness(12),
                    Child = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = new Image
                        {
                            Source = new Bitmap(stream),
                            Stretch = Stretch.Uniform,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                };
            }

            return new AtomTextArea
            {
                Text = request.Text ?? string.Empty,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = FontFamily.Parse("Consolas, Cascadia Mono, Menlo, monospace"),
                MinHeight = 420,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Lines = 18,
                IsAutoSize = false,
                IsResizable = false,
                StyleVariant = InputControlStyleVariant.Outlined,
                SizeType = SizeType.Middle
            };
        }

        private static string FormatPreviewMeta(PreviewDialogRequest request)
        {
            var parts = new List<string> { FormatSize(request.Size), request.ContentType };
            if (!string.IsNullOrWhiteSpace(request.EncodingName))
            {
                parts.Add(request.EncodingName);
            }

            return string.Join(" | ", parts);
        }

        private static string FormatSize(long size)
        {
            var value = (double)size;
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

    private sealed class AccountDialogWindow : UserControl
    {
        private readonly AccountDialogRequest _request;
        private readonly AtomComboBox _categoryInput = new();
        private readonly AtomComboBox _providerInput = new();
        private readonly AtomComboBox _sftpAuthModeInput = new();
        private readonly AtomCheckBox _ftpAnonymousInput = new();
        private readonly AtomCheckBox _webDavAnonymousInput = new();
        private readonly AtomLineEdit _displayNameInput = CreateLineEdit();
        private readonly StackPanel _configFields = new() { Spacing = 8 };
        private readonly StackPanel _credentialFields = new() { Spacing = 8 };
        private readonly AtomUI.Desktop.Controls.MessageCard _connectionMessage = new();
        private readonly AtomDialogButton _testButton = new();
        private readonly AtomDialogButton _testDetailsButton = new();
        private readonly Dictionary<string, AtomLineEdit> _configInputs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AtomLineEdit> _credentialInputs = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Control> _credentialRows = new(StringComparer.Ordinal);
        private readonly IReadOnlyList<ProviderCategoryOption> _categoryOptions =
        [
            new ProviderCategoryOption(StorageProviderCategory.ObjectStorage, "OSS"),
            new ProviderCategoryOption(StorageProviderCategory.FileTransfer, "FTP / SFTP", ExcludedProviderId: "webdav"),
            new ProviderCategoryOption(StorageProviderCategory.FileTransfer, "WebDAV", RequiredProviderId: "webdav")
        ];
        private ErrorDialogRequest? _lastTestDetails;
        private bool _isClosed;
        private bool _footerButtonsAttached;
        private IDisposable? _connectionMessageDismissTimer;

        public AccountDialogWindow(AccountDialogRequest request)
        {
            _request = request;
            Content = BuildContent();
            SelectInitialCategory();
            RebuildProviderChoices();
            RebuildConfigFields();
            DetachedFromVisualTree += (_, _) =>
            {
                _isClosed = true;
                _connectionMessageDismissTimer?.Dispose();
                _connectionMessageDismissTimer = null;
            };
            AttachedToVisualTree += (_, _) =>
            {
                Dispatcher.UIThread.Post(AttachFooterButtons, DispatcherPriority.Background);
            };
        }

        private Control BuildContent()
        {
            _categoryInput.ItemsSource = _categoryOptions;
            _categoryInput.ItemTemplate = new FuncDataTemplate<ProviderCategoryOption>((category, _) =>
                new TextBlock { Text = category?.DisplayName ?? string.Empty });
            _categoryInput.IsEnabled = _request.ExistingAccount is null;
            _categoryInput.MinHeight = 34;
            _categoryInput.SelectionChanged += (_, _) =>
            {
                if (_isClosed)
                {
                    return;
                }

                RebuildProviderChoices();
                RebuildConfigFields();
                ResetConnectionTestState();
            };

            _providerInput.ItemTemplate = new FuncDataTemplate<ProviderDescriptor>((provider, _) =>
                new TextBlock { Text = provider?.DisplayName ?? string.Empty });
            _providerInput.IsEnabled = _request.ExistingAccount is null;
            _providerInput.MinHeight = 34;
            _providerInput.SelectionChanged += (_, _) =>
            {
                if (_isClosed)
                {
                    return;
                }

                RebuildConfigFields();
                ResetConnectionTestState();
            };

            _displayNameInput.PlaceholderText = "Aliyun OSS - production";
            _displayNameInput.Text = _request.ExistingAccount?.DisplayName;
            _displayNameInput.TextChanged += (_, _) => ResetConnectionTestState();
            _ftpAnonymousInput.Content = "匿名访问";
            _ftpAnonymousInput.IsCheckedChanged += (_, _) =>
            {
                UpdateCredentialInputState();
                ResetConnectionTestState();
            };
            _webDavAnonymousInput.Content = "匿名访问";
            _webDavAnonymousInput.IsCheckedChanged += (_, _) =>
            {
                UpdateCredentialInputState();
                ResetConnectionTestState();
            };
            _sftpAuthModeInput.ItemsSource = new[]
            {
                new AuthModeOption("password", "密码"),
                new AuthModeOption("privateKey", "私钥")
            };
            _sftpAuthModeInput.ItemTemplate = new FuncDataTemplate<AuthModeOption>((option, _) =>
                new TextBlock { Text = option?.DisplayName ?? string.Empty });
            _sftpAuthModeInput.MinHeight = 34;
            _sftpAuthModeInput.SelectionChanged += (_, _) =>
            {
                UpdateCredentialInputState();
                ResetConnectionTestState();
            };
            _connectionMessage.IsVisible = false;
            _connectionMessage.HorizontalAlignment = HorizontalAlignment.Center;
            _connectionMessage.MaxWidth = 580;
            _testButton.Content = "测试连接";
            _testButton.ButtonType = AtomButtonType.Default;
            _testButton.MinWidth = 88;
            _testButton.IsEnabled = _request.TestConnectionAsync is not null;
            _testButton.Role = AtomDialogButtonRole.ActionRole;
            _testButton.Click += async (_, _) =>
            {
                try
                {
                    await TestConnectionAsync().ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    if (!_isClosed)
                    {
                        ShowConnectionMessage($"连接测试异常：{ex.Message}", AtomUI.Desktop.Controls.MessageType.Error);
                        _testButton.IsEnabled = true;
                    }
                }
            };
            _testDetailsButton.Content = "详情";
            _testDetailsButton.ButtonType = AtomButtonType.Default;
            _testDetailsButton.MinWidth = 72;
            _testDetailsButton.IsEnabled = false;
            _testDetailsButton.Role = AtomDialogButtonRole.ActionRole;
            _testDetailsButton.Click += async (_, _) =>
            {
                try
                {
                    await ShowLastTestDetailsAsync().ConfigureAwait(true);
                }
                catch
                {
                    // Error details are diagnostic-only; never let this path terminate the app.
                }
            };

            var body = new StackPanel
            {
                Margin = new Thickness(20),
                Spacing = 14,
                Children =
                {
                    Section("连接类型", new StackPanel
                    {
                        Spacing = 8,
                        Children =
                        {
                            Field("类型", _categoryInput),
                            Field("Provider / 协议", _providerInput)
                        }
                    }),
                    Section("基本信息", Field("显示名称", _displayNameInput)),
                    Section("连接配置", _configFields),
                    Section("凭据", _credentialFields)
                }
            };

            return new Grid
            {
                Children =
                {
                    new ScrollViewer { Content = body },
                    _connectionMessage.WithOverlayMessagePlacement()
                }
            };
        }

        private void AttachFooterButtons()
        {
            if (_footerButtonsAttached || _isClosed || TemplatedParent is not AtomDialog dialog)
            {
                return;
            }

            _footerButtonsAttached = true;
            dialog.CustomButtons.Add(_testButton);
            dialog.CustomButtons.Add(_testDetailsButton);
        }

        private void SelectInitialCategory()
        {
            var category = _request.ExistingAccount?.ProviderCategory ??
                _request.PreferredCategory ??
                _request.Providers.FirstOrDefault()?.Category ??
                StorageProviderCategory.ObjectStorage;

            _categoryInput.SelectedItem = _categoryOptions.FirstOrDefault(item => item.Matches(category, _request.ExistingAccount?.ProviderId ?? _request.PreferredProviderId)) ??
                _categoryOptions.FirstOrDefault(item => item.Category == category) ??
                _categoryOptions.First();
        }

        private void RebuildProviderChoices()
        {
            var category = GetSelectedCategory();
            var categoryOption = GetSelectedCategoryOption();
            var providers = _request.Providers
                .Where(item => item.Category == category)
                .Where(item => categoryOption?.Allows(item) != false)
                .Where(IsVisibleProvider)
                .ToList();

            if (categoryOption?.RequiredProviderId == "webdav")
            {
                providers = providers.SelectMany(CreateWebDavProviderChoices).ToList();
            }

            _providerInput.ItemsSource = providers;
            var selectedProvider = SelectInitialProvider(providers);
            _providerInput.SelectedItem = selectedProvider;
        }

        private ProviderDescriptor? SelectInitialProvider(IReadOnlyList<ProviderDescriptor> providers)
        {
            if (_request.ExistingAccount is not null)
            {
                if (_request.ExistingAccount.ProviderId.Value.Equals("webdav", StringComparison.OrdinalIgnoreCase))
                {
                    var profile = GetExistingWebDavProfile();
                    return providers.FirstOrDefault(item => item.Id == _request.ExistingAccount.ProviderId && GetWebDavProfileValue(item) == profile) ??
                        providers.FirstOrDefault(item => item.Id == _request.ExistingAccount.ProviderId);
                }

                return providers.FirstOrDefault(item => item.Id == _request.ExistingAccount.ProviderId);
            }

            return providers.FirstOrDefault(item => _request.PreferredProviderId is not null && item.Id == _request.PreferredProviderId) ??
                providers.FirstOrDefault();
        }

        private static IEnumerable<ProviderDescriptor> CreateWebDavProviderChoices(ProviderDescriptor provider)
        {
            if (!IsWebDav(provider))
            {
                yield return provider;
                yield break;
            }

            yield return CreateWebDavProviderChoice(provider, "通用 WebDAV");
            yield return CreateWebDavProviderChoice(provider, "坚果云");
        }

        private static ProviderDescriptor CreateWebDavProviderChoice(ProviderDescriptor source, string displayName)
        {
            return new ProviderDescriptor(
                source.Id,
                source.Category,
                displayName,
                source.Description,
                source.Capabilities,
                source.ConfigFields);
        }
        private void RebuildConfigFields()
        {
            _configInputs.Clear();
            _configFields.Children.Clear();
            _credentialInputs.Clear();
            _credentialRows.Clear();
            _credentialFields.Children.Clear();

            if (_providerInput.SelectedItem is not ProviderDescriptor provider)
            {
                return;
            }

            foreach (var field in provider.ConfigFields)
            {
                if (!ShouldShowConfigField(provider, field))
                {
                    continue;
                }

                var input = CreateLineEdit();
                input.PlaceholderText = GetConfigPlaceholder(field);
                input.Text = GetExistingConfigValue(field.Key);
                if (field.Key == "port" && string.IsNullOrWhiteSpace(input.Text))
                {
                    input.Text = IsSftp(provider) ? "22" : IsFtp(provider) ? "21" : null;
                }
                input.TextChanged += (_, _) => ResetConnectionTestState();
                _configInputs[field.Key] = input;
                _configFields.Children.Add(Field(field.DisplayName, input));
            }

            if (string.IsNullOrWhiteSpace(_displayNameInput.Text))
            {
                _displayNameInput.Text = provider.DisplayName;
            }

            if (IsFtp(provider))
            {
                _ftpAnonymousInput.IsChecked = IsExistingAnonymousAuthMode();
                _credentialFields.Children.Add(Field("匿名访问", _ftpAnonymousInput));
            }
            else if (IsWebDav(provider))
            {
                ApplySelectedWebDavProfileDefaults();
                _webDavAnonymousInput.IsChecked = IsExistingAnonymousAuthMode();
                _credentialFields.Children.Add(Field("匿名访问", _webDavAnonymousInput));
            }
            else if (IsSftp(provider))
            {
                SelectInitialSftpAuthMode();
                _credentialFields.Children.Add(Field("认证方式", _sftpAuthModeInput));
            }

            foreach (var field in GetCredentialFields(provider))
            {
                var input = CreateLineEdit();
                input.PlaceholderText = GetCredentialPlaceholder(field);
                input.PasswordChar = field.IsSecret && field.Key != "privateKey" ? '*' : default(char);
                input.IsEnableRevealButton = field.IsSecret && field.Key != "privateKey";
                if (field.Key == "privateKey")
                {
                    input.IsReadOnly = true;
                }

                input.TextChanged += (_, _) => ResetConnectionTestState();
                _credentialInputs[field.Key] = input;
                var row = Field(field.DisplayName, field.Key == "privateKey" ? CreatePrivateKeyPicker(input) : input);
                _credentialRows[field.Key] = row;
                _credentialFields.Children.Add(row);
            }

            UpdateCredentialInputState();
        }

        private async Task TestConnectionAsync()
        {
            if (_request.TestConnectionAsync is null)
            {
                return;
            }

            var result = BuildResult();
            if (result is null)
            {
                return;
            }

            _testButton.IsEnabled = false;
            _testDetailsButton.IsEnabled = false;
            _lastTestDetails = null;
            ShowConnectionMessage("正在测试连接...", AtomUI.Desktop.Controls.MessageType.Loading, autoDismiss: false);
            try
            {
                var testResult = await _request.TestConnectionAsync(result, CancellationToken.None).ConfigureAwait(true);
                if (_isClosed)
                {
                    return;
                }

                _lastTestDetails = BuildConnectionTestDetails(result, testResult);
                _testDetailsButton.IsEnabled = true;
            if (testResult.IsSuccess && testResult.Value is not null)
            {
                ApplyConnectionTestResult(result, testResult.Value);
                ShowConnectionMessage(
                    $"连接可用：{testResult.Value.TargetSummary}",
                    AtomUI.Desktop.Controls.MessageType.Success);
                }
                else
                {
                    ShowConnectionMessage(
                        testResult.Error?.Message ?? "连接失败。",
                        AtomUI.Desktop.Controls.MessageType.Error);
                }
            }
            finally
            {
                if (!_isClosed)
                {
                    _testButton.IsEnabled = true;
                }
            }
        }

        private void ApplyConnectionTestResult(
            AccountDialogResult dialogResult,
            TestConnectionResult result)
        {
            if (!string.Equals(dialogResult.ProviderId.Value, "sftp", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(result.HomePath) ||
                !_configInputs.TryGetValue("homePath", out var homeInput))
            {
                return;
            }

            homeInput.Text = result.HomePath;
        }

        private Task ShowLastTestDetailsAsync()
        {
            if (_lastTestDetails is null)
            {
                return Task.CompletedTask;
            }

            if (GetMainWindow() is not { } mainWindow)
            {
                return Task.CompletedTask;
            }

            return ShowErrorDetailsOverlayAsync(_lastTestDetails, mainWindow);
        }

        private static ErrorDialogRequest BuildConnectionTestDetails(
            AccountDialogResult dialogResult,
            OperationResult<TestConnectionResult> testResult)
        {
            if (testResult.IsFailure)
            {
                return ErrorDialogRequest.FromError(
                    "连接测试失败",
                    "连接测试失败。",
                    testResult.Error,
                    new Dictionary<string, string>
                    {
                        ["Provider"] = dialogResult.ProviderId.ToString(),
                        ["Endpoint"] = dialogResult.Endpoint ?? "-",
                        ["Region"] = dialogResult.Region ?? "-",
                        ["Bucket / Root"] = dialogResult.ProviderConfig.TryGetValue("bucket", out var bucket) ? bucket : "/"
                    });
            }

            var value = testResult.GetValueOrThrow();
            return new ErrorDialogRequest(
                "连接测试成功",
                "连接测试成功。",
                Details: new Dictionary<string, string>
                {
                    ["Provider"] = value.ProviderId.ToString(),
                    ["Endpoint"] = value.Endpoint ?? dialogResult.Endpoint ?? "-",
                    ["Region"] = value.Region ?? dialogResult.Region ?? "-",
                    ["Bucket / Root"] = value.BucketName ?? (dialogResult.ProviderConfig.TryGetValue("bucket", out var configuredBucket) ? configuredBucket : "/"),
                    ["Home"] = value.HomePath ?? (dialogResult.ProviderConfig.TryGetValue("homePath", out var configuredHome) ? configuredHome : "-"),
                    ["Target"] = string.IsNullOrWhiteSpace(value.TargetSummary) ? "/" : value.TargetSummary,
                    ["Capabilities"] = FormatCapabilities(value.Capabilities)
                });
        }

        private static string FormatCapabilities(StorageCapabilitySet capabilities)
        {
            if (capabilities.Value == StorageCapability.None)
            {
                return "None";
            }

            return string.Join(", ", Enum.GetValues<StorageCapability>()
                .Where(capability => capability != StorageCapability.None && capabilities.Supports(capability)));
        }

        public AccountDialogResult? BuildResult()
        {
            if (_providerInput.SelectedItem is not ProviderDescriptor provider)
            {
                SetValidation("请选择 Provider。");
                return null;
            }

            var displayName = _displayNameInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                SetValidation("显示名称不能为空。");
                return null;
            }

            var credentialValues = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, input) in _credentialInputs.Where(item => IsCredentialFieldActive(item.Key)))
            {
                var value = input.Text?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (string.Equals(key, "privateKey", StringComparison.Ordinal))
                {
                    if (!File.Exists(value))
                    {
                        SetValidation("请选择有效的私钥文件。");
                        return null;
                    }

                    try
                    {
                        value = File.ReadAllText(value);
                    }
                    catch (Exception ex)
                    {
                        SetValidation($"读取私钥文件失败：{ex.Message}");
                        return null;
                    }
                }

                if (!string.IsNullOrWhiteSpace(value))
                {
                    credentialValues[key] = value;
                }
            }
            if (_request.ExistingAccount is null || credentialValues.Count > 0 || IsAnonymousFtp(provider) || IsAnonymousWebDav(provider))
            {
                ApplyExplicitAuthMode(provider, credentialValues);
            }
            var credentialFields = GetCredentialFields(provider);
            if (_request.ExistingAccount is null && !IsAnonymousFtp(provider) && !IsAnonymousWebDav(provider))
            {
                foreach (var field in credentialFields.Where(field => field.IsRequired && IsCredentialFieldActive(field.Key)))
                {
                    if (!credentialValues.ContainsKey(field.Key))
                    {
                        SetValidation($"{field.DisplayName} 不能为空。");
                        return null;
                    }
                }
            }
            else if (_request.ExistingAccount is not null && credentialValues.Count > 0 && !IsAnonymousFtp(provider) && !IsAnonymousWebDav(provider))
            {
                foreach (var field in credentialFields.Where(field => field.IsRequired && IsCredentialFieldActive(field.Key)))
                {
                    if (!credentialValues.ContainsKey(field.Key))
                    {
                        SetValidation("修改凭据时必须填写当前类型的全部必填凭据字段。");
                        return null;
                    }
                }
            }

            foreach (var field in provider.ConfigFields.Where(field => field.IsRequired))
            {
                if (!_configInputs.TryGetValue(field.Key, out var input) || string.IsNullOrWhiteSpace(input.Text))
                {
                    SetValidation($"{field.DisplayName} 不能为空。");
                    return null;
                }
            }

            var config = _configInputs
                .Select(item => new KeyValuePair<string, string>(item.Key, item.Value.Text?.Trim() ?? string.Empty))
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            ApplyExplicitAuthMode(provider, config);
            ApplyExplicitWebDavProfile(provider, config);

            if (config.TryGetValue("port", out var portText) &&
                (!int.TryParse(portText, out var port) || port is < 1 or > 65535))
            {
                SetValidation("Port 必须是 1 到 65535 之间的数字。");
                return null;
            }

            config.TryGetValue("endpoint", out var endpoint);
            config.TryGetValue("region", out var region);
            config.Remove("endpoint");
            config.Remove("region");
            SetValidation(string.Empty);

            return new AccountDialogResult(
                provider.Category,
                provider.Id,
                displayName,
                endpoint,
                region,
                config,
                credentialValues);
        }

        private ProviderCategoryOption? GetSelectedCategoryOption()
        {
            return _categoryInput.SelectedItem as ProviderCategoryOption;
        }

        private StorageProviderCategory GetSelectedCategory()
        {
            return GetSelectedCategoryOption()?.Category ?? _request.PreferredCategory ?? StorageProviderCategory.ObjectStorage;
        }

        private static IReadOnlyList<CredentialField> GetCredentialFields(ProviderDescriptor provider)
        {
            if (provider.Category == StorageProviderCategory.ObjectStorage)
            {
                return
                [
                    new CredentialField("accessKeyId", "AccessKeyId", true, false),
                    new CredentialField("accessKeySecret", "AccessKeySecret", true, true)
                ];
            }

            if (IsFtp(provider) || IsWebDav(provider))
            {
                return
                [
                    new CredentialField("username", "用户名", true, false),
                    new CredentialField("password", "密码", true, true)
                ];
            }

            if (IsSftp(provider))
            {
                return
                [
                    new CredentialField("username", "用户名", true, false),
                    new CredentialField("password", "密码", true, true),
                    new CredentialField("privateKey", "私钥", true, true)
                ];
            }

            return [];
        }

        private static string GetConfigPlaceholder(ProviderConfigFieldDescriptor field)
        {
            if (field.Key == "bucket")
            {
                return "可选；留空进入 bucket 列表";
            }

            if (field.Key == "rootPath")
            {
                return "可选；留空使用远端默认根路径";
            }

            if (field.Key == "homePath")
            {
                return "可选；连接测试成功后自动填入";
            }

            if (field.Key == "port")
            {
                return "可选；例如 22 或 21";
            }

            return field.IsRequired ? $"{field.DisplayName} 必填" : field.DisplayName;
        }

        private static string GetCredentialPlaceholder(CredentialField field)
        {
            if (field.Key == "privateKey")
            {
                return @"请选择本机 .ssh 目录下的私钥文件";
            }

            return field.IsSecret ? "已配置；留空不修改" : field.DisplayName;
        }

        private string? GetExistingConfigValue(string key)
        {
            var account = _request.ExistingAccount;
            if (account is null)
            {
                return null;
            }

            return key switch
            {
                "endpoint" => account.Endpoint,
                "region" => account.Region,
                var configKey when account.ProviderConfig.TryGetValue(configKey, out var value) => value,
                _ => null
            };
        }

        private void SetValidation(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                HideConnectionMessage();
                return;
            }

            ShowConnectionMessage(message, AtomUI.Desktop.Controls.MessageType.Error, autoDismiss: false);
        }

        private void ResetConnectionTestState()
        {
            if (_isClosed)
            {
                return;
            }

            _lastTestDetails = null;
            _testDetailsButton.IsEnabled = false;
            HideConnectionMessage();
        }

        private void ShowConnectionMessage(
            string message,
            AtomUI.Desktop.Controls.MessageType type,
            bool autoDismiss = true)
        {
            _connectionMessageDismissTimer?.Dispose();
            _connectionMessageDismissTimer = null;
            _connectionMessage.ClearValue(AtomUI.Desktop.Controls.MessageCard.IconProperty);
            _connectionMessage.Message = message;
            _connectionMessage.MessageType = type;
            _connectionMessage.IsClosed = false;
            _connectionMessage.IsVisible = true;

            if (autoDismiss)
            {
                _connectionMessageDismissTimer = DispatcherTimer.RunOnce(
                    HideConnectionMessage,
                    TimeSpan.FromSeconds(2));
            }
        }

        private void HideConnectionMessage()
        {
            _connectionMessageDismissTimer?.Dispose();
            _connectionMessageDismissTimer = null;
            _connectionMessage.IsVisible = false;
        }

        private static StackPanel Section(string title, Control content)
        {
            return new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                    content
                }
            };
        }

        private static AtomLineEdit CreateLineEdit()
        {
            return new AtomLineEdit
            {
                StyleVariant = InputControlStyleVariant.Outlined,
                SizeType = SizeType.Middle,
                MinHeight = 34
            };
        }

        private static Grid Field(string label, Control input)
        {
            DetachFromCurrentParent(input);

            return new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(132, GridUnitType.Pixel),
                    new ColumnDefinition(1, GridUnitType.Star)
                },
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Brushes.DimGray
                    },
                    input
                }
            }.WithInputColumn(input);
        }

        private Grid CreatePrivateKeyPicker(AtomLineEdit input)
        {
            async Task SelectPrivateKeyAsync()
            {
                var selectedPath = await PickSftpPrivateKeyFileAsync().ConfigureAwait(true);
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    return;
                }

                input.Text = selectedPath;
                ResetConnectionTestState();
            }

            var pickerButton = new AtomButton
            {
                Content = "选择",
                ButtonType = AtomButtonType.Default,
                SizeType = SizeType.Middle,
                MinHeight = 34,
                MinWidth = 72
            };
            pickerButton.Click += async (_, _) =>
            {
                await SelectPrivateKeyAsync().ConfigureAwait(true);
            };
            input.PointerPressed += async (_, _) =>
            {
                await SelectPrivateKeyAsync().ConfigureAwait(true);
            };

            return new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(1, GridUnitType.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8,
                Children =
                {
                    input,
                    pickerButton
                }
            }.WithColumn(pickerButton, 1);
        }

        private static async Task<string?> PickSftpPrivateKeyFileAsync()
        {
            if (GetMainWindow() is not { } mainWindow)
            {
                return null;
            }

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var sshDirectory = string.IsNullOrWhiteSpace(userProfile)
                ? null
                : Path.Combine(userProfile, ".ssh");
            var suggestedPath = !string.IsNullOrWhiteSpace(sshDirectory) && Directory.Exists(sshDirectory)
                ? sshDirectory
                : userProfile;

            IStorageFolder? suggestedFolder = null;
            if (!string.IsNullOrWhiteSpace(suggestedPath) && Directory.Exists(suggestedPath))
            {
                suggestedFolder = await mainWindow.StorageProvider
                    .TryGetFolderFromPathAsync(suggestedPath)
                    .ConfigureAwait(true);
            }

            var files = await mainWindow.StorageProvider.OpenFilePickerAsync(
                new FilePickerOpenOptions
                {
                    Title = "选择 SFTP 私钥文件",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedFolder
                }).ConfigureAwait(true);

            return files
                .Select(file => file.TryGetLocalPath())
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        }

        private static void DetachFromCurrentParent(Control input)
        {
            switch (input.Parent)
            {
                case Panel panel:
                    panel.Children.Remove(input);
                    break;
                case ContentControl contentControl when ReferenceEquals(contentControl.Content, input):
                    contentControl.Content = null;
                    break;
            }
        }

        private static bool IsVisibleProvider(ProviderDescriptor provider)
        {
            // Keep JdCloud OSS and QingStor hidden from the account dialog until they complete release validation.
            if (provider.Id.Value is "jdcloud-oss" or "qingstor")
            {
                return false;
            }

            return provider.Category == StorageProviderCategory.ObjectStorage ||
                IsFtp(provider) ||
                IsSftp(provider) ||
                IsWebDav(provider);
        }

        private static bool ShouldShowConfigField(ProviderDescriptor provider, ProviderConfigFieldDescriptor field)
        {
            if (provider.Category == StorageProviderCategory.ObjectStorage)
            {
                return field.Key is "endpoint" or "region";
            }

            if (IsFtp(provider) || IsSftp(provider))
            {
                return field.Key is "endpoint" or "port" or "homePath";
            }

            if (IsWebDav(provider))
            {
                return field.Key is "endpoint" or "rootPath";
            }

            return false;
        }

        private static bool IsFtp(ProviderDescriptor provider)
        {
            return string.Equals(provider.Id.Value, "ftp", StringComparison.Ordinal);
        }

        private static bool IsSftp(ProviderDescriptor provider)
        {
            return string.Equals(provider.Id.Value, "sftp", StringComparison.Ordinal);
        }

        private static bool IsWebDav(ProviderDescriptor provider)
        {
            return string.Equals(provider.Id.Value, "webdav", StringComparison.Ordinal);
        }

        private bool IsAnonymousFtp(ProviderDescriptor provider)
        {
            return IsFtp(provider) && _ftpAnonymousInput.IsChecked == true;
        }


        private bool IsAnonymousWebDav(ProviderDescriptor provider)
        {
            return IsWebDav(provider) && _webDavAnonymousInput.IsChecked == true;
        }

        private bool IsExistingAnonymousAuthMode()
        {
            return string.Equals(
                _request.ExistingAccount?.ProviderConfig.TryGetValue("authMode", out var existingAuthMode) == true
                    ? existingAuthMode
                    : null,
                "anonymous",
                StringComparison.OrdinalIgnoreCase);
        }
        private bool IsPrivateKeySftp(ProviderDescriptor provider)
        {
            return IsSftp(provider) &&
                _sftpAuthModeInput.SelectedItem is AuthModeOption { Value: "privateKey" };
        }

        private string GetExistingWebDavProfile()
        {
            return _request.ExistingAccount?.ProviderConfig.TryGetValue("webDavProfile", out var existingProfile) == true
                ? existingProfile
                : "generic";
        }

        private string GetSelectedWebDavProfile()
        {
            return _providerInput.SelectedItem is ProviderDescriptor provider
                ? GetWebDavProfileValue(provider)
                : "generic";
        }

        private static string GetWebDavProfileValue(ProviderDescriptor provider)
        {
            return IsWebDav(provider) && provider.DisplayName.Equals("坚果云", StringComparison.OrdinalIgnoreCase)
                ? "jianguoyun"
                : "generic";
        }

        private void ApplySelectedWebDavProfileDefaults()
        {
            if (_providerInput.SelectedItem is not ProviderDescriptor provider ||
                !IsWebDav(provider) ||
                GetSelectedWebDavProfile() != "jianguoyun")
            {
                return;
            }

            if (_configInputs.TryGetValue("endpoint", out var endpointInput) &&
                string.IsNullOrWhiteSpace(endpointInput.Text))
            {
                endpointInput.Text = "https://dav.jianguoyun.com/dav/";
            }
        }

        private void SelectInitialSftpAuthMode()
        {
            var authMode = _request.ExistingAccount?.ProviderConfig.TryGetValue("authMode", out var existingAuthMode) == true
                ? existingAuthMode
                : "password";
            _sftpAuthModeInput.SelectedItem = ((IEnumerable<AuthModeOption>)_sftpAuthModeInput.ItemsSource!)
                .FirstOrDefault(item => string.Equals(item.Value, authMode, StringComparison.OrdinalIgnoreCase)) ??
                ((IEnumerable<AuthModeOption>)_sftpAuthModeInput.ItemsSource!).First();
        }

        private void UpdateCredentialInputState()
        {
            if (_providerInput.SelectedItem is not ProviderDescriptor provider)
            {
                return;
            }

            foreach (var (key, input) in _credentialInputs)
            {
                var isActive = IsCredentialFieldActive(provider, key);
                input.IsEnabled = isActive;
                if (_credentialRows.TryGetValue(key, out var row))
                {
                    row.IsVisible = isActive;
                }
            }
        }

        private bool IsCredentialFieldActive(string key)
        {
            return _providerInput.SelectedItem is ProviderDescriptor provider && IsCredentialFieldActive(provider, key);
        }

        private bool IsCredentialFieldActive(ProviderDescriptor provider, string key)
        {
            if (IsAnonymousFtp(provider) || IsAnonymousWebDav(provider))
            {
                return false;
            }

            if (IsSftp(provider))
            {
                return key switch
                {
                    "password" => !IsPrivateKeySftp(provider),
                    "privateKey" => IsPrivateKeySftp(provider),
                    _ => true
                };
            }

            return true;
        }

        private void ApplyExplicitAuthMode(ProviderDescriptor provider, IDictionary<string, string> values)
        {
            if (IsAnonymousFtp(provider) || IsAnonymousWebDav(provider))
            {
                values["authMode"] = "anonymous";
                return;
            }

            if (IsSftp(provider) && _sftpAuthModeInput.SelectedItem is AuthModeOption option)
            {
                values["authMode"] = option.Value;
            }
        }

        private void ApplyExplicitWebDavProfile(ProviderDescriptor provider, IDictionary<string, string> values)
        {
            if (!IsWebDav(provider))
            {
                return;
            }

            values["webDavProfile"] = GetSelectedWebDavProfile();
        }

    }

    private sealed record CredentialField(string Key, string DisplayName, bool IsRequired, bool IsSecret);

    private sealed record ProviderCategoryOption(
        StorageProviderCategory Category,
        string DisplayName,
        string? RequiredProviderId = null,
        string? ExcludedProviderId = null)
    {
        public bool Matches(StorageProviderCategory category, StorageProviderId? providerId)
        {
            return Category == category &&
                (providerId is null || Allows(providerId.Value));
        }

        public bool Allows(ProviderDescriptor provider)
        {
            return Allows(provider.Id);
        }

        private bool Allows(StorageProviderId providerId)
        {
            return (RequiredProviderId is null || providerId.Value.Equals(RequiredProviderId, StringComparison.OrdinalIgnoreCase)) &&
                (ExcludedProviderId is null || !providerId.Value.Equals(ExcludedProviderId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private sealed record AuthModeOption(string Value, string DisplayName);

}

file static class AccountDialogGridExtensions
{
public static Grid WithInputColumn(this Grid grid, Control input)
{
    Grid.SetColumn(input, 1);
    return grid;
}

public static Grid WithColumn(this Grid grid, Control input, int column)
{
    Grid.SetColumn(input, column);
    return grid;
}

public static Control WithGridRow(this Control control, int row)
{
    Grid.SetRow(control, row);
    return control;
}

public static Control WithOverlayMessagePlacement(this Control control)
{
    control.VerticalAlignment = VerticalAlignment.Top;
    control.HorizontalAlignment = HorizontalAlignment.Center;
    control.Margin = new Thickness(0, 12, 0, 0);
    control.ZIndex = 10;
    return control;
}

public static Control WithPreviewContentRow(this Control control)
{
    Grid.SetRow(control, 1);
    return control;
}

public static Control WithPreviewMetaColumn(this Control control)
{
    Grid.SetColumn(control, 1);
    return control;
}
}





