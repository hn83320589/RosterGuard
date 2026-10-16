using RosterGuard.Core.Domain;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Rules;

/// <summary>一筆已展開成實際時段的指派。</summary>
public sealed record ScheduledShift(Assignment Assignment, Employee Employee, ShiftType Shift, ShiftInterval Interval)
{
    public DateOnly Date => Assignment.Date;
}

/// <summary>
/// 規則評估的輸入。Assignments 可以包含評估範圍之外的指派（例如前後 7 天）作為上下文，
/// 但規則只回報錨點日期落在 [From, To] 的違規。
/// </summary>
public sealed class RosterContext
{
    private readonly Dictionary<int, List<ScheduledShift>> _byEmployee;

    public RosterContext(
        IEnumerable<Employee> employees,
        IEnumerable<ShiftType> shiftTypes,
        RuleSettings settings,
        IEnumerable<Assignment> assignments,
        DateOnly from,
        DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException($"評估範圍無效：{from} 晚於 {to}", nameof(to));
        }

        Employees = employees.ToDictionary(e => e.Id);
        ShiftTypes = shiftTypes.ToDictionary(s => s.Id);
        Settings = settings;
        From = from;
        To = to;
        Shifts = assignments.Distinct().Select(Expand).OrderBy(s => s.Interval.Start).ThenBy(s => s.Shift.Id).ToList();
        _byEmployee = Shifts.GroupBy(s => s.Employee.Id).ToDictionary(g => g.Key, g => g.ToList());
    }

    public IReadOnlyDictionary<int, Employee> Employees { get; }
    public IReadOnlyDictionary<int, ShiftType> ShiftTypes { get; }
    public RuleSettings Settings { get; }
    public DateOnly From { get; }
    public DateOnly To { get; }

    /// <summary>所有指派，依開始時間排序。</summary>
    public IReadOnlyList<ScheduledShift> Shifts { get; }

    public bool InRange(DateOnly date) => date >= From && date <= To;

    /// <summary>該員工的所有指派，依開始時間排序。</summary>
    public IReadOnlyList<ScheduledShift> ShiftsOf(int employeeId) =>
        _byEmployee.TryGetValue(employeeId, out var list) ? list : [];

    public IEnumerable<int> EmployeeIdsWithShifts => _byEmployee.Keys;

    private ScheduledShift Expand(Assignment a)
    {
        if (!Employees.TryGetValue(a.EmployeeId, out var employee))
        {
            throw new InvalidOperationException($"指派參照不存在的員工 Id {a.EmployeeId}（{a.Date}）。");
        }

        if (!ShiftTypes.TryGetValue(a.ShiftTypeId, out var shift))
        {
            throw new InvalidOperationException($"指派參照不存在的班別 Id {a.ShiftTypeId}（{a.Date}）。");
        }

        return new ScheduledShift(a, employee, shift, ShiftTime.Interval(a.Date, shift));
    }
}
