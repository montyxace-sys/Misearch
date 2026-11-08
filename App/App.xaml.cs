using System.Windows;

namespace Misearch.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Misearch",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
    }
}
