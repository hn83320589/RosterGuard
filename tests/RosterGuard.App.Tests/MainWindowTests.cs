using Avalonia.Headless.XUnit;
using RosterGuard.App.Views;

namespace RosterGuard.App.Tests;

public class MainWindowTests
{
    [AvaloniaFact]
    public void MainWindow_Starts_WithTitle()
    {
        var window = new MainWindow();
        window.Show();

        Assert.Contains("RosterGuard", window.Title);
    }
}
