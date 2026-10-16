using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RosterGuard.App.ViewModels;
using RosterGuard.App.Views;

namespace RosterGuard.App.Tests;

/// <summary>以暫存 SQLite（首次建立時自動載入範例資料）開啟主視窗，月份固定為 2026 年 10 月。</summary>
internal sealed class MainWindowFixture : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RosterGuardUiTests", Guid.NewGuid().ToString("N"));

    public MainWindowFixture(int month = 10)
    {
        ViewModel = AppBootstrapper.Create(Path.Combine(_dir, "roster.db"), new DateOnly(2026, month, 1));
        Window = new MainWindow { DataContext = ViewModel };
        Window.Show();
        Pump();
    }

    public MainWindow Window { get; }
    public MainWindowViewModel ViewModel { get; }
    public RosterViewModel Roster => ViewModel.Roster;

    public T Find<T>(string name) where T : Control =>
        Window.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name)
        ?? throw new InvalidOperationException($"找不到控制項 {name}");

    public Button CellButton(object cellViewModel) =>
        Window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.DataContext, cellViewModel));

    public void Click(Control control)
    {
        control.BringIntoView();
        Pump();
        var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("控制項不在視窗中");
        Window.MouseDown(center, MouseButton.Left);
        Window.MouseUp(center, MouseButton.Left);
        Pump();
    }

    public void Press(Key key, RawInputModifiers modifiers)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Pump();
    }

    public static void Pump() => Dispatcher.UIThread.RunJobs();

    public void Dispose()
    {
        Window.Close();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
