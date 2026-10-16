using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Candidates;

/// <summary>某天某班某職稱的人力缺口。</summary>
public sealed record StaffingSlot(DateOnly Date, int ShiftTypeId, Role Role);

public sealed record Candidate(Employee Employee, int MonthMinutes);

/// <param name="Candidates">可指派者，依本月累計工時由少到多、姓名、Id 排序。</param>
/// <param name="NoCandidateReason">沒有候選人時的原因，否則為 null。</param>
public sealed record CandidateResult(IReadOnlyList<Candidate> Candidates, string? NoCandidateReason);

/// <summary>找出「指派後不會產生任何新違規」的員工。</summary>
public sealed class CandidateFinder(Func<IEnumerable<Assignment>, IReadOnlyList<Violation>> evaluate)
{
    public CandidateResult Find(
        StaffingSlot slot,
        IReadOnlyCollection<Employee> employees,
        IReadOnlyDictionary<int, ShiftType> shiftTypes,
        IReadOnlySet<Assignment> current,
        DateOnly monthStart,
        DateOnly monthEnd)
    {
        var pool = employees
            .Where(e => e.IsActive && e.Role == slot.Role)
            .Where(e => !current.Contains(new Assignment(e.Id, slot.Date, slot.ShiftTypeId)))
            .ToList();

        if (pool.Count == 0)
        {
            return new CandidateResult([], $"沒有可指派的在職{slot.Role.ToChinese()}員工");
        }

        var before = evaluate(current).Select(v => v.Key).ToHashSet();
        var accepted = new List<Candidate>();
        var blockedBy = new List<RuleKind>();

        foreach (var employee in pool)
        {
            var proposed = new Assignment(employee.Id, slot.Date, slot.ShiftTypeId);
            var firstNew = evaluate(current.Append(proposed)).FirstOrDefault(v => !before.Contains(v.Key));
            if (firstNew is null)
            {
                accepted.Add(new Candidate(employee, MonthMinutes(employee.Id, current, shiftTypes, monthStart, monthEnd)));
            }
            else
            {
                blockedBy.Add(firstNew.Rule);
            }
        }

        var sorted = accepted
            .OrderBy(c => c.MonthMinutes)
            .ThenBy(c => c.Employee.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Employee.Id)
            .ToList();

        return new CandidateResult(sorted, sorted.Count > 0 ? null : Explain(blockedBy));
    }

    public static int MonthMinutes(int employeeId, IEnumerable<Assignment> assignments,
        IReadOnlyDictionary<int, ShiftType> shiftTypes, DateOnly monthStart, DateOnly monthEnd) =>
        assignments
            .Where(a => a.EmployeeId == employeeId && a.Date >= monthStart && a.Date <= monthEnd)
            .Sum(a => (int)ShiftTime.Duration(shiftTypes[a.ShiftTypeId]).TotalMinutes);

    private static string Explain(List<RuleKind> blockedBy)
    {
        var groups = blockedBy.GroupBy(r => r).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();
        if (groups.Count == 1)
        {
            return $"全部候選人都會違反{ShortName(groups[0].Key)}規則";
        }

        return "沒有可用的候選人：" + string.Join("、", groups.Select(g => $"{g.Count()} 人違反{ShortName(g.Key)}規則"));
    }

    private static string ShortName(RuleKind rule) => rule switch
    {
        RuleKind.InsufficientRest => "間隔",
        RuleKind.MissingRestDay => "休息日",
        RuleKind.WeeklyHoursExceeded => "週工時",
        _ => rule.ToChinese(),
    };
}
