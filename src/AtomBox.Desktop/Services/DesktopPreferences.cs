namespace AtomBox.Desktop.Services;

public sealed record DesktopPreferences(
    StartupPageOption StartupPage,
    CloseWindowBehavior CloseWindowBehavior,
    string DefaultDownloadDirectory)
{
    public static DesktopPreferences CreateDefault()
    {
        return new DesktopPreferences(
            StartupPageOption.Home,
            CloseWindowBehavior.CloseApplication,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                "AtomBox"));
    }
}

public enum StartupPageOption
{
    RemoteStorage = 0,
    TransferQueue = 1,
    AccountManagement = 2,
    Home = 3
}

public enum CloseWindowBehavior
{
    CloseApplication = 0,
    MinimizeApplication = 1
}
