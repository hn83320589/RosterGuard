using RosterGuard.Core.Rules;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Rules;

public class RestGapRuleTests
{
    private const int Closing = 1, Morning = 2, Late = 3;

    private static RosterBuilder Scenario(string morningStart = "07:00") => new RosterBuilder()
        .With(Employee(1, "小明"))
        .With(Employee(2, "小華"))
        .With(Shift(Closing, "打烊班", "18:00", "02:00"))
        .With(Shift(Morning, "早班", morningStart, "15:00"))
        .With(Shift(Late, "晚班", "13:00", "21:00"));

    [Fact]
    public void Exactly_11_hours_after_overnight_shift_is_not_a_violation()
    {
        // 打烊班 10/1 18:00 → 10/2 02:00，下一班 10/2 13:00：間隔剛好 11 小時
        var violations = Scenario()
            .Assign(1, D(10, 1), Closing)
            .Assign(1, D(10, 2), Late)
            .Evaluate(RuleKind.InsufficientRest);

        Assert.Empty(violations);
    }

    [Fact]
    public void One_minute_short_of_11_hours_is_a_violation()
    {
        var violations = Scenario(morningStart: "12:59")
            .Assign(1, D(10, 1), Closing)
            .Assign(1, D(10, 2), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        var v = Assert.Single(violations);
        Assert.Equal(1, v.Key.EmployeeId);
        Assert.Equal(D(10, 2), v.Key.Date);
    }

    [Fact]
    public void Closing_shift_followed_by_next_morning_shift_is_a_violation()
    {
        // 02:00 下班、07:00 上班：間隔 5 小時
        var violations = Scenario()
            .Assign(1, D(10, 1), Closing)
            .Assign(1, D(10, 2), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        var v = Assert.Single(violations);
        Assert.Contains("5", v.Message);
        Assert.Contains(new CellRef(1, D(10, 2)), v.Cells);
    }

    [Fact]
    public void Minimum_rest_is_configurable()
    {
        var violations = Scenario()
            .MinRestHours(5)
            .Assign(1, D(10, 1), Closing)
            .Assign(1, D(10, 2), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        Assert.Empty(violations);
    }

    [Fact]
    public void Different_employees_are_not_compared()
    {
        var violations = Scenario()
            .Assign(1, D(10, 1), Closing)
            .Assign(2, D(10, 2), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        Assert.Empty(violations);
    }

    [Fact]
    public void Overlapping_shifts_are_reported_as_overlap_not_as_rest_gap()
    {
        var violations = Scenario()
            .Assign(1, D(10, 2), Morning)
            .Assign(1, D(10, 2), Late)
            .EvaluateIgnoringStaffing();

        Assert.DoesNotContain(violations, v => v.Rule == RuleKind.InsufficientRest);
        Assert.Contains(violations, v => v.Rule == RuleKind.Overlap);
    }

    [Fact]
    public void Gap_is_measured_from_the_latest_end_when_shifts_overlap()
    {
        // 早班 07-15 與晚班 13-21 重疊，隔天 07:00 早班距離晚班結束只有 10 小時
        var violations = Scenario()
            .Assign(1, D(10, 2), Morning)
            .Assign(1, D(10, 2), Late)
            .Assign(1, D(10, 3), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 3), v.Key.Date);
    }

    [Fact]
    public void Previous_month_closing_shift_is_considered_at_month_start()
    {
        var violations = Scenario()
            .Assign(1, D(9, 30), Closing)
            .Assign(1, D(10, 1), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 1), v.Key.Date);
    }

    [Fact]
    public void Violation_anchored_outside_range_is_not_reported()
    {
        var violations = Scenario()
            .Assign(1, D(10, 31), Closing)
            .Assign(1, D(11, 1), Morning)
            .Evaluate(RuleKind.InsufficientRest);

        Assert.Empty(violations);
    }
}
