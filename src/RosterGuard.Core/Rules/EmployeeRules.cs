using RosterGuard.Core.Domain;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Rules;

internal static class Format
{
    public static string Hours(int minutes) =>
        minutes % 60 == 0 ? $"{minutes / 60}" : $"{minutes / 60.0:0.##}";

    public static string Day(DateOnly d) => $"{d:MM/dd}({d.DayOfWeek.ToChinese()})";

    public static string Time(DateTime t) => $"{t:MM/dd HH:mm}";
}

/// <summary>R1 時段重疊：同一員工任兩個班別的時段有交集。</summary>
public sealed class OverlapRule : IRule
{
    public IEnumerable<Violation> Evaluate(RosterContext context)
    {
        foreach (var employeeId in context.EmployeeIdsWithShifts)
        {
            var shifts = context.ShiftsOf(employeeId);
            for (var i = 0; i < shifts.Count; i++)
            {
                for (var j = i + 1; j < shifts.Count && shifts[j].Interval.Start < shifts[i].Interval.End; j++)
                {
                    var (earlier, later) = (shifts[i], shifts[j]);
                    if (!context.InRange(later.Date))
                    {
                        continue;
                    }

                    yield return new Violation(
                        new ViolationKey(RuleKind.Overlap, employeeId, later.Date, later.Shift.Id,
                            Discriminator: $"{earlier.Date:yyyy-MM-dd}/{earlier.Shift.Id}"),
                        Severity.Error,
                        $"{earlier.Shift.Name}（{Format.Time(earlier.Interval.Start)}–{Format.Time(earlier.Interval.End)}）"
                        + $"與{later.Shift.Name}（{Format.Time(later.Interval.Start)}–{Format.Time(later.Interval.End)}）時段重疊",
                        Cells(employeeId, context, earlier.Date, later.Date));
                }
            }
        }
    }

    private static IReadOnlyList<CellRef> Cells(int employeeId, RosterContext context, params DateOnly[] dates) =>
        dates.Distinct().Where(context.InRange).Select(d => new CellRef(employeeId, d)).ToList();
}

/// <summary>
/// R2 間隔不足：依開始時間排序，下一班開始時間減去「之前所有班中最晚的結束時間」小於最小間隔。
/// 已重疊（間隔為負）的情況交給 <see cref="OverlapRule"/>，不重複回報。
/// </summary>
public sealed class RestGapRule : IRule
{
    public IEnumerable<Violation> Evaluate(RosterContext context)
    {
        var minRest = TimeSpan.FromMinutes(context.Settings.MinRestMinutes);

        foreach (var employeeId in context.EmployeeIdsWithShifts)
        {
            ScheduledShift? latestEnding = null;
            foreach (var current in context.ShiftsOf(employeeId))
            {
                if (latestEnding is not null)
                {
                    var gap = current.Interval.Start - latestEnding.Interval.End;
                    if (gap >= TimeSpan.Zero && gap < minRest && context.InRange(current.Date))
                    {
                        yield return new Violation(
                            new ViolationKey(RuleKind.InsufficientRest, employeeId, current.Date, current.Shift.Id,
                                Discriminator: $"{latestEnding.Date:yyyy-MM-dd}/{latestEnding.Shift.Id}"),
                            Severity.Error,
                            $"{latestEnding.Shift.Name}於 {Format.Time(latestEnding.Interval.End)} 結束，"
                            + $"{current.Shift.Name}於 {Format.Time(current.Interval.Start)} 開始，"
                            + $"間隔 {Format.Hours((int)gap.TotalMinutes)} 小時，少於 {Format.Hours(context.Settings.MinRestMinutes)} 小時",
                            [new CellRef(employeeId, current.Date)]);
                    }
                }

                if (latestEnding is null || current.Interval.End > latestEnding.Interval.End)
                {
                    latestEnding = current;
                }
            }
        }
    }
}

/// <summary>
/// R3 缺少休息日：休息日 = 該日曆日 [00:00, 24:00) 沒有任何班別時段落入。
/// 連續 7 個以上非休息日即違規，每段連續區間只回報一筆，錨點為第 7 天（若早於評估範圍則為範圍首日）。
/// </summary>
public sealed class RestDayRule : IRule
{
    private const int MaxConsecutiveWorkDays = 6;

    public IEnumerable<Violation> Evaluate(RosterContext context)
    {
        foreach (var employeeId in context.EmployeeIdsWithShifts)
        {
            var workDays = WorkDays(context.ShiftsOf(employeeId));
            foreach (var (first, last) in Runs(workDays))
            {
                var length = last.DayNumber - first.DayNumber + 1;
                if (length <= MaxConsecutiveWorkDays)
                {
                    continue;
                }

                var seventh = first.AddDays(MaxConsecutiveWorkDays);
                var anchor = seventh < context.From ? context.From : seventh;
                if (anchor > last || !context.InRange(anchor))
                {
                    continue;
                }

                var cells = Enumerable.Range(0, length)
                    .Select(first.AddDays)
                    .Where(context.InRange)
                    .Select(d => new CellRef(employeeId, d))
                    .ToList();

                yield return new Violation(
                    new ViolationKey(RuleKind.MissingRestDay, employeeId, anchor, Discriminator: $"{first:yyyy-MM-dd}"),
                    Severity.Error,
                    $"{Format.Day(first)} 起連續 {length} 天沒有休息日（任 7 日內至少需 1 個休息日）",
                    cells);
            }
        }
    }

    private static SortedSet<DateOnly> WorkDays(IEnumerable<ScheduledShift> shifts)
    {
        var days = new SortedSet<DateOnly>();
        foreach (var s in shifts)
        {
            for (var d = s.Date; s.Interval.Touches(d); d = d.AddDays(1))
            {
                days.Add(d);
            }
        }
        return days;
    }

    private static IEnumerable<(DateOnly First, DateOnly Last)> Runs(SortedSet<DateOnly> days)
    {
        DateOnly? first = null, last = null;
        foreach (var d in days)
        {
            if (last is { } l && d == l.AddDays(1))
            {
                last = d;
                continue;
            }

            if (first is { } f && last is { } prev)
            {
                yield return (f, prev);
            }
            first = last = d;
        }

        if (first is { } ff && last is { } ll)
        {
            yield return (ff, ll);
        }
    }
}

/// <summary>
/// R4 週工時超過上限：每週自週一起算，班別依開始日歸週，週總時數「大於」上限才違規。
/// 錨點為使累計超過上限的那一班（若早於評估範圍則為範圍首日）。
/// </summary>
public sealed class WeeklyHoursRule : IRule
{
    public IEnumerable<Violation> Evaluate(RosterContext context)
    {
        foreach (var employeeId in context.EmployeeIdsWithShifts)
        {
            var employee = context.Employees[employeeId];
            foreach (var week in context.ShiftsOf(employeeId).GroupBy(s => WeekMath.WeekStart(s.Date)))
            {
                var total = week.Sum(s => s.Interval.Minutes);
                if (total <= employee.WeeklyLimitMinutes)
                {
                    continue;
                }

                var running = 0;
                var exceeding = week.First(s => (running += s.Interval.Minutes) > employee.WeeklyLimitMinutes);
                var anchor = exceeding.Date < context.From ? context.From : exceeding.Date;
                var weekEnd = week.Key.AddDays(6);
                if (anchor > weekEnd || !context.InRange(anchor))
                {
                    continue;
                }

                yield return new Violation(
                    new ViolationKey(RuleKind.WeeklyHoursExceeded, employeeId, anchor, Discriminator: $"{week.Key:yyyy-MM-dd}"),
                    Severity.Error,
                    $"{Format.Day(week.Key)} 起該週共 {Format.Hours(total)} 小時，超過上限 {Format.Hours(employee.WeeklyLimitMinutes)} 小時",
                    week.Select(s => s.Date).Distinct().Where(context.InRange).Select(d => new CellRef(employeeId, d)).ToList());
            }
        }
    }
}

/// <summary>R5 不可上班日：班別開始日的星期屬於員工固定不可上班的星期。</summary>
public sealed class UnavailableDayRule : IRule
{
    public IEnumerable<Violation> Evaluate(RosterContext context) =>
        from s in context.Shifts
        where context.InRange(s.Date) && s.Employee.UnavailableDays.Contains(s.Date.DayOfWeek)
        select new Violation(
            new ViolationKey(RuleKind.UnavailableDay, s.Employee.Id, s.Date, s.Shift.Id),
            Severity.Warning,
            $"{s.Employee.Name}每週{s.Date.DayOfWeek.ToChinese()}不可上班，卻被排入{s.Shift.Name}",
            [new CellRef(s.Employee.Id, s.Date)]);
}
