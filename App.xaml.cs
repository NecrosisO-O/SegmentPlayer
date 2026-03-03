using PortablePlayer.UI.ViewModels;
using PortablePlayer.Infrastructure.Diagnostics;

namespace PortablePlayer;

public partial class App : global::System.Windows.Application
{
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(global::System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Initialize(AppContext.BaseDirectory);
        AppLog.Info("App", $"Startup. Args={string.Join(" ", e.Args)}");

        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
        };
        window.Show();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        AppLog.Error("AppDomain", "Unhandled exception.", e.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, global::System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Dispatcher", "Unhandled UI exception.", e.Exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Error("TaskScheduler", "Unobserved task exception.", e.Exception);
    }
}
