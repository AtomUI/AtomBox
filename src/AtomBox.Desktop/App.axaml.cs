using System.Diagnostics;
using System.Text;
using AtomBox.Desktop.Composition;
using AtomBox.Desktop.Localization;
using AtomUI;
using AtomUI.Desktop.Controls;
using AtomUI.Theme;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop;

public sealed partial class App : Avalonia.Application
{
    private static bool s_exceptionHandlersRegistered;

    public override void Initialize()
    {
        RegisterExceptionHandlers();

        var localization = DesktopBootstrap.Host.Services
            .GetRequiredService<IAtomBoxLocalizedText>();
        LocalizationHost.Initialize(localization);

        this.UseAtomUI(builder =>
        {
            builder.WithDefaultCultureInfo(localization.CurrentCulture);
            builder.WithDefaultTheme(IThemeManager.DEFAULT_THEME_ID);
            builder.UseAlibabaSansFont();
            builder.UseDesktopControls();
        });

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException(
                "AtomBox requires the Avalonia classic desktop lifetime.");
        }

        try
        {
            DesktopBootstrap.Initialize(desktop);
        }
        catch (Exception exception)
        {
            DesktopBootstrap.ShowStartupError(desktop, exception);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterExceptionHandlers()
    {
        if (s_exceptionHandlersRegistered)
        {
            return;
        }

        s_exceptionHandlersRegistered = true;
        Dispatcher.UIThread.UnhandledException += (_, args) =>
        {
            LogUnhandledException("Dispatcher.UIThread", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogUnhandledException("TaskScheduler", args.Exception);
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                LogUnhandledException("AppDomain", exception);
            }
            else
            {
                LogUnhandledException("AppDomain", new InvalidOperationException(args.ExceptionObject?.ToString()));
            }
        };
    }

    private static void LogUnhandledException(string source, Exception exception)
    {
        Debug.WriteLine(exception);

        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);
            var builder = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}] {source}")
                .AppendLine(exception.ToString())
                .AppendLine();
            File.AppendAllText(Path.Combine(logDirectory, "desktop-errors.log"), builder.ToString());
        }
        catch
        {
            // Last-chance logging must never become a new crash source.
        }
    }
}
