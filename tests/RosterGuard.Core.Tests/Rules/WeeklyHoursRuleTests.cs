using RosterGuard.Core.Rules;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Rules;

public class WeeklyHoursRuleTests
{
    private const int Eight = 1, Closing = 2;

    private static RosterBuilder Scenario(double limitHours) => new RosterBuilder()
        .With(Employee(1, "小明", weeklyLimitHours: limitHours))
        .With(Shift(Eight, "早班", "07:00", "15:00"))
        .With(Shift(Closing, "打烊班", "18:00", "02:00"));

    [Fact]
    public void Exactly_at_the_limit_is_not_a_violation()
    {
        var violations = Scenario(40)
            .AssignDays(1, D(10, 12), 5, Eight) // 週一到週五 5×8 = 40
            .Evaluate(RuleKind.WeeklyHoursExceeded);

        Assert.Empty(violations);
    }

    [Fact]
    public void Over_the_limit_is_a_violation_anchored_at_the_exceeding_shift()
    {
        var violations = Scenario(32)
            .AssignDays(1, D(10, 12), 5, Eight)
            .Evaluate(RuleKind.WeeklyHoursExceeded);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 16), v.Key.Date);
        Assert.Contains("40", v.Message);
        Assert.Equal(5, v.Cells.Count);
    }

    [Fact]
    public void Half_hour_limits_are_compared_exactly()
    {
        var violations = Scenario(39.5)
            .AssignDays(1, D(10, 12), 5, Eight)
            .Evaluate(RuleKind.WeeklyHoursExceeded);

        Assert.Single(violations);
    }

    [Fact]
    public void Weeks_start_on_monday_not_rolling_seven_days()
    {
        // 週日 10/11 屬於上一週；10/12（一）到 10/16（五）剛好 40 小時
        var violations = Scenario(40)
            .Assign(1, D(10, 11), Eight)
            .AssignDays(1, D(10, 12), 5, Eight)
            .Evaluate(RuleKind.WeeklyHoursExceeded);

        Assert.Empty(violations);
    }

    [Fact]
    public void Overnight_shift_on_sunday_belongs_to_the_week_of_its_start_date()
    {
        // 週一到週五 40 小時 + 週日打烊班 (跨到下週一) → 48 小時歸在同一週
        var violations = Scenario(40)
            .AssignDays(1, D(10, 12), 5, Eight)
            .Assign(1, D(10, 18), Closing)
            .Evaluate(RuleKind.WeeklyHoursExceeded);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 18), v.Key.Date);
    }

    [Fact]
    public void Week_spanning_previous_month_is_reported_in_current_month()
    {
        // 週一 9/28 起：9/28–9/30 + 10/1–10/2，上限 32
        var violations = Scenario(32)
            .AssignDays(1, D(9, 28), 5, Eight)
            .Evaluate(RuleKind.WeeklyHoursExceeded);

        var v = Assert.Single(violations);
        Assert.Equal(D(10, 2), v.Key.Date);
    }
}
