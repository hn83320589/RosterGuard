using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using RosterGuard.App.ViewModels;
using RosterGuard.Core.Rules;

namespace RosterGuard.App.Tests;

/// <summary>
/// 產生 README 截圖。平時不執行；設定環境變數 ROSTERGUARD_SCREENSHOTS=輸出資料夾 後執行：
/// <c>dotnet test tests/RosterGuard.App.Tests --filter ScreenshotGenerator</c>
/// </summary>
public class ScreenshotGenerator
{
    private static string? OutputDir => Environment.GetEnvironmentVariable("ROSTERGUARD_SCREENSHOTS");

    [AvaloniaFact]
    public void Capture_readme_screenshots()
    {
        if (OutputDir is not { } dir)
        {
            return;
        }

        Directory.CreateDirectory(dir);

        using (var f = new MainWindowFixture(month: 10))
        {
            Select(f, RuleKind.MissingRestDay);
            Save(f, dir, "roster-october.png");
        }

        using (var f = new MainWindowFixture(month: 9))
        {
            Select(f, RuleKind.Understaffed);
            Save(f, dir, "candidates-september.png");
        }

        using (var f = new MainWindowFixture(month: 10))
        {
            f.Find<TabControl>("MainTabs").SelectedIndex = 1;
            MainWindowFixture.Pump();
            f.ViewModel.Employees.Selected = f.ViewModel.Employees.Items[3];
            Save(f, dir, "employees.png");
        }
    }

    private static void Select(MainWindowFixture f, RuleKind rule)
    {
        var item = f.Find<ListBox>("ViolationList").GetVisualDescendants().OfType<ListBoxItem>()
            .First(i => i.DataContext is ViolationItemViewModel v && v.Violation.Rule == rule);
        f.Click(item);
    }

    private static void Save(MainWindowFixture f, string dir, string name)
    {
        MainWindowFixture.Pump();
        using var frame = f.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("無法擷取畫面");
        frame.Save(Path.Combine(dir, name));
    }
}
