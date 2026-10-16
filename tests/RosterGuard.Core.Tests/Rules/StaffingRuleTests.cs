using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Rules;

public class StaffingRuleTests
{
    private const int Morning = 1;

    private static RosterBuilder Scenario(int kitchen, int floor, bool shiftActive = true) => new RosterBuilder()
        .Range(D(10, 1), D(10, 1))
        .With(Employee(1, "阿廚", Role.Kitchen))
        .With(Employee(2, "二廚", Role.Kitchen))
        .With(Employee(3, "小外", Role.Floor))
        .With(Employee(4, "大外", Role.Floor))
        .With(Shift(Morning, "早班", "07:00", "15:00", kitchen, floor, shiftActive));

    [Fact]
    public void Fewer_than_required_is_understaffed()
    {
        var violations = Scenario(kitchen: 0, floor: 2)
            .Assign(3, D(10, 1), Morning)
            .Evaluate();

        var v = Assert.Single(violations);
        Assert.Equal(RuleKind.Understaffed, v.Rule);
        Assert.Equal(Role.Floor, v.Key.Role);
        Assert.Equal(Morning, v.Key.ShiftTypeId);
        Assert.Contains("1", v.Message);
        Assert.Contains("2", v.Message);
    }

    [Fact]
    public void More_than_required_is_overstaffed()
    {
        var violations = Scenario(kitchen: 1, floor: 0)
            .Assign(1, D(10, 1), Morning)
            .Assign(2, D(10, 1), Morning)
            .Evaluate();

        var v = Assert.Single(violations);
        Assert.Equal(RuleKind.Overstaffed, v.Rule);
        Assert.Equal(Role.Kitchen, v.Key.Role);
    }

    [Fact]
    public void Roles_are_counted_separately()
    {
        // 外場排了 2 人（需求 1），內場 0 人（需求 1）：不可互相抵銷
        var violations = Scenario(kitchen: 1, floor: 1)
            .Assign(3, D(10, 1), Morning)
            .Assign(4, D(10, 1), Morning)
            .Evaluate();

        Assert.Single(violations, v => v.Rule == RuleKind.Understaffed && v.Key.Role == Role.Kitchen);
        Assert.Single(violations, v => v.Rule == RuleKind.Overstaffed && v.Key.Role == Role.Floor);
        Assert.Equal(2, violations.Count);
    }

    [Fact]
    public void Exact_staffing_has_no_violation()
    {
        var violations = Scenario(kitchen: 1, floor: 1)
            .Assign(1, D(10, 1), Morning)
            .Assign(3, D(10, 1), Morning)
            .Evaluate();

        Assert.Empty(violations);
    }

    [Fact]
    public void Inactive_shift_type_is_not_checked()
    {
        var violations = Scenario(kitchen: 1, floor: 1, shiftActive: false).Evaluate();

        Assert.Empty(violations);
    }

    [Fact]
    public void Every_day_in_range_is_checked()
    {
        var violations = Scenario(kitchen: 1, floor: 0)
            .Range(D(10, 1), D(10, 7))
            .Evaluate(RuleKind.Understaffed);

        Assert.Equal(7, violations.Count);
    }
}
