using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Rules;

public class RuleEngineTests
{
    [Fact]
    public void Violations_are_sorted_by_severity_then_date()
    {
        var violations = new RosterBuilder()
            .With(Employee(1, "小明", unavailable: Weekdays.Thursday))
            .With(Shift(1, "早班", "07:00", "15:00", floor: 1))
            .With(Shift(2, "打烊班", "18:00", "02:00"))
            .Range(D(10, 1), D(10, 3))
            .Assign(1, D(10, 1), 1)  // 週四 → 不可上班日 (Warning)
            .Assign(1, D(10, 2), 2)
            .Assign(1, D(10, 3), 1)  // 間隔不足 (Error)
            .Evaluate();

        Assert.Equal(Severity.Error, violations[0].Severity);
        Assert.Equal(RuleKind.InsufficientRest, violations[0].Rule);
        var warnings = violations.Skip(1).ToList();
        Assert.All(warnings, v => Assert.Equal(Severity.Warning, v.Severity));
        Assert.Equal(warnings.OrderBy(v => v.Key.Date).Select(v => v.Key.Date), warnings.Select(v => v.Key.Date));
    }

    [Fact]
    public void Assignment_with_unknown_shift_type_fails_fast()
    {
        var builder = new RosterBuilder()
            .With(Employee(1, "小明"))
            .Assign(1, D(10, 1), 99);

        var ex = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("99", ex.Message);
    }
}
