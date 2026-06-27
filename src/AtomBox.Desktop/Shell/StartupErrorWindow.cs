using AtomBox.Infrastructure.Configuration;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System.Diagnostics;

namespace AtomBox.Desktop.Shell;

public sealed class StartupErrorWindow : Window
{
    public StartupErrorWindow(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var paths = new AtomBoxStoragePaths();
        Title = "AtomBox 启动失败";
        Width = 640;
        Height = 420;
        MinWidth = 560;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var openConfigurationButton = new Button
        {
            Content = "打开配置目录",
            MinWidth = 112
        };
        openConfigurationButton.Click += (_, _) => OpenDirectory(paths.ConfigurationDirectory);

        var openLogButton = new Button
        {
            Content = "打开日志目录",
            MinWidth = 112
        };
        openLogButton.Click += (_, _) => OpenDirectory(paths.LogDirectory);

        var exitButton = new Button
        {
            Content = "退出应用",
            MinWidth = 96
        };
        exitButton.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "AtomBox 无法进入正常主界面。",
                    FontSize = 18,
                    FontWeight = FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = exception.Message,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.Firebrick
                },
                new TextBlock
                {
                    Text = $"异常类型：{exception.GetType().FullName}",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brushes.DimGray
                },
                BuildDetails(paths),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        openConfigurationButton,
                        openLogButton,
                        exitButton
                    }
                }
            }
        };
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    private static Control BuildDetails(AtomBoxStoragePaths paths)
    {
        var details = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "诊断路径",
                    FontWeight = FontWeight.SemiBold
                },
                DetailRow("配置目录", paths.ConfigurationDirectory),
                DetailRow("状态目录", paths.StateDirectory),
                DetailRow("凭据目录", paths.CredentialDirectory),
                DetailRow("日志目录", paths.LogDirectory)
            }
        };

        return new Border
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = details
        };
    }

    private static Grid DetailRow(string label, string value)
    {
        var valueText = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap
        };

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(100, GridUnitType.Pixel),
                new ColumnDefinition(1, GridUnitType.Star)
            },
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Foreground = Brushes.DimGray
                },
                valueText
            }
        };
        Grid.SetColumn(valueText, 1);
        return grid;
    }
}
