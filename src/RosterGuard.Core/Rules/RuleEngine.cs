namespace RosterGuard.Core.Rules;

public interface IRule
{
    IEnumerable<Violation> Evaluate(RosterContext context);
}

/// <summary>執行所有規則並依「嚴重度 → 日期 → 規則 → 員工姓名」排序。</summary>
public sealed class RuleEngine(IReadOnlyList<IRule> rules)
{
    public static RuleEngine Default { get; } = new(
    [
        new OverlapRule(),
        new RestGapRule(),
        new RestDayRule(),
        new WeeklyHoursRule(),
        new UnavailableDayRule(),
        new StaffingRule(),
    ]);

    public IReadOnlyList<Violation> Evaluate(RosterContext context) =>
        rules.SelectMany(r => r.Evaluate(context))
            .OrderBy(v => v.Severity)
            .ThenBy(v => v.Key.Date)
            .ThenBy(v => v.Rule)
            .ThenBy(v => EmployeeName(context, v), StringComparer.Ordinal)
            .ThenBy(v => v.Key.ShiftTypeId)
            .ThenBy(v => v.Key.Role)
            .ThenBy(v => v.Key.Discriminator, StringComparer.Ordinal)
            .ToList();

    private static string EmployeeName(RosterContext context, Violation v) =>
        v.Key.EmployeeId is { } id ? context.Employees[id].Name : "";
}
