using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RosterGuard.App.ViewModels;
using RosterGuard.Core.Rules;
using RosterGuard.Core.Seed;

namespace RosterGuard.App.Tests;

public class MainWindowTests
{
    private static readonly DateOnly Oct1 = new(2026, 10, 1); // 星期四

    [AvaloniaFact]
    public void Startup_shows_month_grid_and_sample_violations()
    {
        using var f = new MainWindowFixture();

        Assert.Equal("2026 年 10 月", f.Find<TextBlock>("MonthTitleText").Text);
        Assert.Equal(9 * 31, f.Window.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("cell")));
        Assert.Equal("違規 3 筆（錯誤 2／警告 1）", f.Find<TextBlock>("StatusViolationText").Text);
        Assert.Equal(3, f.Find<ListBox>("ViolationList").ItemCount);
    }

    [AvaloniaFact]
    public void Assigning_through_cell_flyout_updates_grid_and_violations_and_ctrl_z_undoes_it()
    {
        using var f = new MainWindowFixture();
        var cell = f.Roster.Cell(SampleData.HuangMeiling, Oct1); // 黃美玲週四不可上班
        var button = f.CellButton(cell);

        f.Click(button);
        var flyout = Assert.IsType<Flyout>(button.Flyout);
        Assert.True(flyout.IsOpen);
        var morning = ((Control)flyout.Content!).GetVisualDescendants().OfType<CheckBox>()
            .Single(c => ((string)c.Content!).StartsWith("早班"));
        morning.IsChecked = true;
        MainWindowFixture.Pump();
        flyout.Hide();

        Assert.Equal("早班", button.GetVisualDescendants().OfType<TextBlock>().First().Text);
        Assert.Contains(cell.Badges, b => b.Text == ViolationStyle.Badge(RuleKind.UnavailableDay));
        // + 不可上班日、早班內場超額，以及兼職週工時 40h > 32h
        Assert.Equal("違規 6 筆（錯誤 3／警告 3）", f.Find<TextBlock>("StatusViolationText").Text);

        f.Press(Key.Z, RawInputModifiers.Control);

        Assert.Equal("", button.GetVisualDescendants().OfType<TextBlock>().First().Text);
        Assert.Empty(cell.Badges);
        Assert.Equal("違規 3 筆（錯誤 2／警告 1）", f.Find<TextBlock>("StatusViolationText").Text);

        f.Press(Key.Y, RawInputModifiers.Control);
        Assert.Equal("早班", button.GetVisualDescendants().OfType<TextBlock>().First().Text);
    }

    [AvaloniaFact]
    public void Clicking_a_violation_highlights_and_scrolls_to_its_cell()
    {
        using var f = new MainWindowFixture();
        var scroll = f.Find<ScrollViewer>("HorizontalScroll");
        Assert.Equal(0, scroll.Offset.X);

        var list = f.Find<ListBox>("ViolationList");
        var item = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(i => i.DataContext is ViolationItemViewModel { Violation.Rule: RuleKind.WeeklyHoursExceeded });
        f.Click(item);

        var cell = f.Roster.Cell(SampleData.LiYating, new DateOnly(2026, 10, 18));
        Assert.True(cell.IsHighlighted);
        Assert.Contains("highlight", f.CellButton(cell).Classes);
        Assert.True(scroll.Offset.X > 0, "應水平捲動到 10/18 所在的欄位");
        Assert.False(f.Roster.Cell(SampleData.LiYating, Oct1).IsHighlighted);
    }

    [AvaloniaFact]
    public void Switching_months_shows_each_months_own_roster()
    {
        using var f = new MainWindowFixture();

        f.Click(f.Find<Button>("PreviousMonthButton"));

        Assert.Equal("2026 年 9 月", f.Find<TextBlock>("MonthTitleText").Text);
        Assert.Equal("違規 4 筆（錯誤 2／警告 2）", f.Find<TextBlock>("StatusViolationText").Text);

        f.Click(f.Find<Button>("NextMonthButton"));
        Assert.Equal("違規 3 筆（錯誤 2／警告 1）", f.Find<TextBlock>("StatusViolationText").Text);
    }

    [AvaloniaFact]
    public void Understaffed_violation_shows_candidates_and_one_click_assign_resolves_it()
    {
        using var f = new MainWindowFixture(month: 9);
        var list = f.Find<ListBox>("ViolationList");
        var item = list.GetVisualDescendants().OfType<ListBoxItem>()
            .Single(i => i.DataContext is ViolationItemViewModel { Violation.Rule: RuleKind.Understaffed });

        f.Click(item);

        Assert.Contains("09/24", f.Find<TextBlock>("CandidateTitleText").Text);
        var candidates = f.Find<ItemsControl>("CandidateList");
        Assert.True(candidates.ItemCount > 0);
        var hours = f.Roster.Candidates.Select(c => c.Candidate.MonthMinutes).ToList();
        Assert.Equal(hours.Order(), hours);

        var assign = candidates.GetVisualDescendants().OfType<Button>().First(b => (string?)b.Content == "指派");
        f.Click(assign);

        Assert.DoesNotContain(f.Roster.Violations, v => v.Violation.Rule == RuleKind.Understaffed);
        Assert.Equal("違規 3 筆（錯誤 2／警告 1）", f.Find<TextBlock>("StatusViolationText").Text);
    }
}
