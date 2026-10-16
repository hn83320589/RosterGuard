using RosterGuard.Core.Domain;

namespace RosterGuard.Core.Rules;

/// <summary>
/// R6 人力不足／超額：評估範圍內每一天 × 每個啟用中的班別 × 每個職稱，比對已排人數與需求人數。
/// 停用的班別不檢查。
/// </summary>
public sealed class StaffingRule : IRule
{
    public IEnumerable<Violation> Evaluate(RosterContext context)
    {
        var counts = context.Shifts
            .Where(s => context.InRange(s.Date))
            .GroupBy(s => (s.Date, ShiftId: s.Shift.Id, s.Employee.Role))
            .ToDictionary(g => g.Key, g => g.Count());

        for (var date = context.From; date <= context.To; date = date.AddDays(1))
        {
            foreach (var shift in context.ShiftTypes.Values.Where(s => s.IsActive).OrderBy(s => s.Id))
            {
                foreach (var role in Enum.GetValues<Role>())
                {
                    var required = shift.RequiredFor(role);
                    var assigned = counts.GetValueOrDefault((date, shift.Id, role));
                    if (assigned == required)
                    {
                        continue;
                    }

                    var rule = assigned < required ? RuleKind.Understaffed : RuleKind.Overstaffed;
                    yield return new Violation(
                        new ViolationKey(rule, null, date, shift.Id, role),
                        Severity.Warning,
                        $"{Format.Day(date)} {shift.Name} {role.ToChinese()}：已排 {assigned} 人，需求 {required} 人",
                        []);
                }
            }
        }
    }
}
