using RosterGuard.Core.Rules;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Rules;

public class RestDayRuleTests
{
    private const int Day = 1, Closing = 2, Evening = 3;

    private static RosterBuilder Scenario() => new RosterBuilder()
        .With(Employee(1, "小明", weeklyLimitHours: 100))
        .With(Shift(Day, "早班", "08:00", "12:00"))
        .With(Shift(Closing, "打烊班", "18:00", "02:00"))
        .With(Shift(Evening, "晚班", "16:00", "00:00"));

    [Fact]
    public void Six_work_days_then_a_rest_day_is_not_a_violation()
    {
        var violations = Scenario()
            .AssignDays(1, D(10, 5), 6, Day)   // 10/5–10/10 上班
            .Assign(1, D(10, 12), Day)          // 10/11 休息
            .Evaluate(RuleKind.MissingRestDay);

        Assert.Empty(violations);
    }

    [Fact]
    public void Seven_consecutive_work_days_is_a_violation_on_the_seventh_day()
    {
        var violations = Scenario()
            .AssignDays(1, D(10, 5), 7, Day)
            .Evaluate(RuleKind.MissingRestDay);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 11), v.Key.Date);
        Assert.Equal(7, v.Cells.Count);
    }

    [Fact]
    public void Seventh_day_touched_only_by_overnight_tail_is_not_a_rest_day()
    {
        // 打烊班 10/5–10/10 共 6 班，最後一班結束於 10/11 02:00，因此 10/11 不是休息日
        var violations = Scenario()
            .AssignDays(1, D(10, 5), 6, Closing)
            .Evaluate(RuleKind.MissingRestDay);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 11), v.Key.Date);
    }

    [Fact]
    public void Shift_ending_exactly_at_midnight_leaves_next_day_as_rest_day()
    {
        // 晚班 16:00–00:00 半開區間，不會碰到隔天
        var violations = Scenario()
            .AssignDays(1, D(10, 5), 6, Evening)
            .Evaluate(RuleKind.MissingRestDay);

        Assert.Empty(violations);
    }

    [Fact]
    public void A_long_run_of_work_days_is_reported_once()
    {
        var violations = Scenario()
            .AssignDays(1, D(10, 1), 12, Day)
            .Evaluate(RuleKind.MissingRestDay);

        var v = Assert.Single(violations);
        Assert.Equal(12, v.Cells.Count);
        Assert.Contains("12", v.Message);
    }

    [Fact]
    public void Separate_runs_are_reported_independently()
    {
        var violations = Scenario()
            .AssignDays(1, D(10, 1), 7, Day)
            .AssignDays(1, D(10, 9), 8, Day) // 10/8 休息
            .Evaluate(RuleKind.MissingRestDay);

        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void Run_starting_in_previous_month_is_anchored_at_month_start()
    {
        var violations = Scenario()
            .AssignDays(1, D(9, 26), 8, Day) // 9/26–10/3
            .Evaluate(RuleKind.MissingRestDay);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 2), v.Key.Date); // 第 7 天 = 10/2
        Assert.All(v.Cells, c => Assert.Equal(10, c.Date.Month));
    }
}
