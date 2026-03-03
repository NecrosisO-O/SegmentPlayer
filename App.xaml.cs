using PortablePlayer.UI.ViewModels;

namespace PortablePlayer;

public partial class App : global::System.Windows.Application
{
    protected override void OnStartup(global::System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow
        {
            DataContext = new MainWindowViewModel(),
        };
        window.Show();
    }
}
