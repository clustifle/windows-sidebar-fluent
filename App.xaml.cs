using Microsoft.UI.Xaml;

namespace Sidebar;
public partial class App : Application
{
    private MainWindow? window;
    private System.Threading.Mutex? instanceMutex;
    public App()
    {
        UnhandledException += (_, args) => LogFailure(args.Exception);
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            instanceMutex = new System.Threading.Mutex(true, "Local\\ClassicSidebar.DesktopHost", out bool firstInstance);
            if (!firstInstance) { Exit(); return; }
            window = new MainWindow();
            window.Activate();
            window.AttachToDesktop();
#if THEME_REGRESSION
            window.DispatcherQueue.TryEnqueue(async () => { await Task.Delay(2500); if (Environment.GetCommandLineArgs().Contains("--desktop-gadget-regression")) window.RunDesktopGadgetRegression(); else if (Environment.GetCommandLineArgs().Contains("--launcher-flyout-regression")) window.RunLauncherFlyoutRegression(); else if (Environment.GetCommandLineArgs().Contains("--v2-regression")) window.RunV2Regression(); else if (Environment.GetCommandLineArgs().Contains("--puzzle-regression")) window.RunPuzzleRegression(); else if (Environment.GetCommandLineArgs().Contains("--search-regression")) window.RunSearchRegression(); else window.RunThemeRegression(); });
#endif
        }
        catch (Exception ex) { LogFailure(ex); throw; }
    }
    private static void LogFailure(Exception exception)
    {
        try
        {
            var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClassicSidebar");
            System.IO.Directory.CreateDirectory(directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "startup-error.txt"), exception.ToString());
        }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
