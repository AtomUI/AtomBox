using AtomBox.Desktop.Shell;
using AtomUI.City.Core.Hosting;
using AtomUI.City.Presentation;
using AtomBox.Desktop.Navigation;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop.Composition;

internal static class DesktopBootstrap
{
    private static readonly object SyncRoot = new();
    private static IApplicationHost? s_host;
    private static Exception? s_startupFailure;

    internal static IApplicationHost Host
    {
        get
        {
            lock (SyncRoot)
            {
                return s_host ?? throw new InvalidOperationException(
                    "The AtomBox application Host is not attached.");
            }
        }
    }

    internal static void Attach(IApplicationHost host, Exception? startupFailure = null)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (SyncRoot)
        {
            if (s_host is not null)
            {
                throw new InvalidOperationException("An AtomBox application Host is already attached.");
            }

            s_startupFailure = startupFailure;
            s_host = host;
        }
    }

    internal static void Initialize(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);

        var (host, startupFailure) = GetState();
        if (startupFailure is not null)
        {
            ShowStartupError(lifetime, startupFailure);
            return;
        }

        if (lifetime.MainWindow is not null)
        {
            throw new InvalidOperationException("The desktop lifetime already has a main window.");
        }

        var runtime = host.Services.GetRequiredService<IPresentationRuntime>();
        runtime.Attach(lifetime, host.HostScope);

        var mainWindow = host.Services.GetRequiredService<MainWindow>();
        var session = runtime.RegisterWindow(mainWindow, "main");
        session.RegisterOutlet(
            AtomBoxRouteCatalog.PrimaryOutlet,
            new AvaloniaRouteOutletTarget(mainWindow.GetMainContentControl()));
        var navigation = host.Services.GetRequiredService<INavigationService>() as NavigationService
            ?? throw new InvalidOperationException("The AtomBox navigation service does not support City outlets.");
        navigation.AttachOutlet(session.GetOutlet(AtomBoxRouteCatalog.PrimaryOutlet));
        mainWindow.AttachInitialization(() => ComposeShellAsync(session, host.Services));
        mainWindow.Closed += (_, _) =>
            _ = ShutdownWhenSessionCompletesAsync(session, lifetime);
        lifetime.MainWindow = mainWindow;
    }

    internal static void ShowStartupError(
        IClassicDesktopStyleApplicationLifetime lifetime,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(exception);

        var errorWindow = new StartupErrorWindow(exception);
        errorWindow.Closed += (_, _) => lifetime.Shutdown(1);
        lifetime.MainWindow = errorWindow;
    }

    internal static void Detach(IApplicationHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        lock (SyncRoot)
        {
            if (!ReferenceEquals(s_host, host))
            {
                throw new InvalidOperationException("The supplied AtomBox Host is not attached.");
            }

            s_host = null;
            s_startupFailure = null;
        }
    }

    private static (IApplicationHost Host, Exception? StartupFailure) GetState()
    {
        lock (SyncRoot)
        {
            return (
                s_host ?? throw new InvalidOperationException(
                    "The AtomBox application Host is not attached."),
                s_startupFailure);
        }
    }

    private static async Task ShutdownWhenSessionCompletesAsync(
        WindowSession session,
        IClassicDesktopStyleApplicationLifetime lifetime)
    {
        try
        {
            while (session.State is not (WindowSessionState.Closed or WindowSessionState.Faulted))
            {
                await Task.Delay(10).ConfigureAwait(false);
            }

            var exitCode = session.State == WindowSessionState.Closed ? 0 : 1;
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(exitCode));
        }
        catch
        {
            Dispatcher.UIThread.Post(() => lifetime.Shutdown(1));
        }
    }

    private static async Task ComposeShellAsync(
        WindowSession session,
        IServiceProvider services)
    {
        var mainViewModel = services.GetRequiredService<MainWindowViewModel>();
        var statusBar = services.GetRequiredService<StatusBarViewModel>();
        var cancellationToken = session.Scope.CancellationToken;

        var root = await session.ViewModels.AttachRootAsync(
            "shell",
            ViewModelLease.Borrowed(mainViewModel),
            cancellationToken).ConfigureAwait(true);
        await root.AttachChildAsync(
            "shell.status-bar",
            ViewModelLease.Borrowed(statusBar),
            cancellationToken).ConfigureAwait(true);
    }
}
