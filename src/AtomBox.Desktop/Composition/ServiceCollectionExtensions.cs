using AtomBox.Desktop.Dialogs;
using AtomBox.Desktop.Localization;
using AtomBox.Desktop.Navigation;
using AtomBox.Desktop.Services;
using AtomBox.Desktop.Shell;
using AtomBox.Desktop.ViewFactory;
using AtomBox.Desktop.ViewModels.Pages;
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

        services.AddSingleton<IViewFactory, ViewFactory.ViewFactory>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAccountDialogWorkflow, AccountDialogWorkflow>();
        services.AddSingleton<IMessageService, MessageService>();
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton<IFilePickerService, AvaloniaFilePickerService>();

        services.AddTransient<RemoteBrowserViewModel>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<TransferQueueViewModel>();
        services.AddTransient<TransferHistoryViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AccountManagementViewModel>();

        services.AddSingleton<IPageViewModelFactory>(provider =>
            new PageViewModelFactory(
                provider.GetRequiredService<HomeViewModel>,
                provider.GetRequiredService<RemoteBrowserViewModel>,
                provider.GetRequiredService<TransferQueueViewModel>,
                provider.GetRequiredService<TransferHistoryViewModel>,
                provider.GetRequiredService<SettingsViewModel>,
                provider.GetRequiredService<AccountManagementViewModel>));
        services.AddSingleton<INavigationService, NavigationService>();

        return services;
    }
}
