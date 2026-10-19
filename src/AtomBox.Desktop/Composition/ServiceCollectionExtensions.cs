using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomBox.Desktop.ViewModels.Pages;
using AtomBox.Desktop.ViewModels;
using AtomUI.City.Mvvm;
using Microsoft.Extensions.DependencyInjection;

namespace AtomBox.Desktop.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAtomBoxDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddTransient<MainWindow>();

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAccountDialogWorkflow, AccountDialogWorkflow>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<ICommandExceptionHandler, DesktopCommandExceptionHandler>();
        services.AddSingleton<DesktopCommandFactory>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();

        services.AddTransient<RemoteBrowserViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<TransferQueueViewModel>();
        services.AddTransient<TransferHistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AccountManagementViewModel>();

        services.AddSingleton<INavigationService, NavigationService>();

        return services;
    }
}
