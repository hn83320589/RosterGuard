using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RosterGuard.App.ViewModels;
using RosterGuard.App.Views;

namespace RosterGuard.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var path = AppBootstrapper.ResolveDatabasePath();
            try
            {
                desktop.MainWindow = new MainWindow { DataContext = AppBootstrapper.Create(path) };
            }
            catch (Exception ex) when (AppBootstrapper.IsDatabaseOpenFailure(ex))
            {
                desktop.MainWindow = StartupError(desktop, path, ex);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static StartupErrorWindow StartupError(IClassicDesktopStyleApplicationLifetime desktop, string path, Exception error)
    {
        var vm = new StartupErrorViewModel(path, error, p => AppBootstrapper.Create(p));
        var window = new StartupErrorWindow { DataContext = vm };
        vm.Opened += (_, main) =>
        {
            var mainWindow = new MainWindow { DataContext = main };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            window.Close();
        };
        vm.ExitRequested += (_, _) => desktop.Shutdown(1);
        return window;
    }
}
