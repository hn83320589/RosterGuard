using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Rules;

public class UnavailableAndOverlapRuleTests
{
    private const int Morning = 1, Late = 2, Closing = 3, Dawn = 4, Afternoon = 5;

    // 2026-10-17 是星期六、10-18 是星期日
    private static RosterBuilder Scenario() => new RosterBuilder()
        .With(Employee(1, "小明", unavailable: Weekdays.Sunday))
        .With(Shift(Morning, "早班", "07:00", "15:00"))
        .With(Shift(Late, "晚班", "13:00", "21:00"))
        .With(Shift(Closing, "打烊班", "18:00", "02:00"))
        .With(Shift(Dawn, "清晨班", "01:00", "06:00"))
        .With(Shift(Afternoon, "午班", "15:00", "18:00"));

    [Fact]
    public void Assignment_on_unavailable_weekday_is_reported_as_unavailable_only()
    {
        var violations = Scenario().Assign(1, D(10, 18), Morning).EvaluateIgnoringStaffing();

        var v = Assert.Single(violations);
        Assert.Equal(RuleKind.UnavailableDay, v.Rule);
        Assert.Equal(new CellRef(1, D(10, 18)), Assert.Single(v.Cells));
    }

    [Fact]
    public void Overlap_on_available_day_is_reported_as_overlap_only()
    {
        var violations = Scenario()
            .Assign(1, D(10, 17), Morning)
            .Assign(1, D(10, 17), Late)
            .EvaluateIgnoringStaffing();

        var v = Assert.Single(violations);
        Assert.Equal(RuleKind.Overlap, v.Rule);
    }

    [Fact]
    public void Overlap_on_unavailable_day_reports_both_rules_independently()
    {
        var violations = Scenario()
            .Assign(1, D(10, 18), Morning)
            .Assign(1, D(10, 18), Late)
            .EvaluateIgnoringStaffing();

        Assert.Single(violations, v => v.Rule == RuleKind.Overlap);
        Assert.Equal(2, violations.Count(v => v.Rule == RuleKind.UnavailableDay));
        Assert.Equal(3, violations.Count);
    }

    [Fact]
    public void Overnight_shift_starting_before_unavailable_day_is_allowed()
    {
        // 週六打烊班結束於週日 02:00，以開始日判定
        var violations = Scenario().Assign(1, D(10, 17), Closing).Evaluate(RuleKind.UnavailableDay);

        Assert.Empty(violations);
    }

    [Fact]
    public void Overlap_across_midnight_is_detected()
    {
        var violations = Scenario()
            .Assign(1, D(10, 16), Closing)  // 10/16 18:00 → 10/17 02:00
            .Assign(1, D(10, 17), Dawn)     // 10/17 01:00 → 06:00
            .Evaluate(RuleKind.Overlap);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 17), v.Key.Date);
        Assert.Contains(new CellRef(1, D(10, 16)), v.Cells);
        Assert.Contains(new CellRef(1, D(10, 17)), v.Cells);
    }

    [Fact]
    public void Back_to_back_shifts_do_not_overlap()
    {
        var violations = Scenario()
            .Assign(1, D(10, 17), Morning)    // 07–15
            .Assign(1, D(10, 17), Afternoon)  // 15–18
            .Evaluate(RuleKind.Overlap);

        Assert.Empty(violations);
    }
}
