using RosterGuard.Core.Domain;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.History;

/// <summary>一次編輯造成的指派差量。復原時套用 <see cref="Inverse"/>。</summary>
public sealed record RosterDelta(IReadOnlyList<Assignment> Added, IReadOnlyList<Assignment> Removed)
{
    public static RosterDelta Empty { get; } = new([], []);

    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    public RosterDelta Inverse() => new(Removed, Added);

    public IEnumerable<DateOnly> Dates => Added.Concat(Removed).Select(a => a.Date);
}

/// <summary>可撤銷的班表操作：只根據目前狀態計算差量，不直接修改狀態。</summary>
public interface IRosterCommand
{
    string Description { get; }

    RosterDelta Plan(IReadOnlySet<Assignment> current);
}

public sealed record AssignCommand(Assignment Assignment) : IRosterCommand
{
    public string Description => "指派";

    public RosterDelta Plan(IReadOnlySet<Assignment> current) =>
        current.Contains(Assignment) ? RosterDelta.Empty : new RosterDelta([Assignment], []);
}

public sealed record UnassignCommand(Assignment Assignment) : IRosterCommand
{
    public string Description => "取消指派";

    public RosterDelta Plan(IReadOnlySet<Assignment> current) =>
        current.Contains(Assignment) ? new RosterDelta([], [Assignment]) : RosterDelta.Empty;
}

/// <summary>
/// 複製上週：先清空目標週，再把來源週的指派平移過去。
/// 只處理落在 [LimitFrom, LimitTo]（目前月份）內的目標日期；停用員工與班別的指派不複製，目標週裡的也保留不清除。
/// </summary>
public sealed class CopyWeekCommand : IRosterCommand
{
    private readonly DateOnly _sourceMonday;
    private readonly DateOnly _targetMonday;
    private readonly DateOnly _limitFrom;
    private readonly DateOnly _limitTo;
    private readonly IReadOnlySet<int> _activeEmployeeIds;
    private readonly IReadOnlySet<int> _activeShiftTypeIds;

    public CopyWeekCommand(DateOnly sourceMonday, DateOnly targetMonday, DateOnly limitFrom, DateOnly limitTo,
        IReadOnlySet<int> activeEmployeeIds, IReadOnlySet<int> activeShiftTypeIds)
    {
        RequireMonday(sourceMonday, nameof(sourceMonday));
        RequireMonday(targetMonday, nameof(targetMonday));
        if (sourceMonday == targetMonday)
        {
            throw new ArgumentException("來源週與目標週相同。", nameof(targetMonday));
        }

        _sourceMonday = sourceMonday;
        _targetMonday = targetMonday;
        _limitFrom = limitFrom;
        _limitTo = limitTo;
        _activeEmployeeIds = activeEmployeeIds;
        _activeShiftTypeIds = activeShiftTypeIds;
    }

    public string Description => $"複製 {_sourceMonday:MM/dd} 週到 {_targetMonday:MM/dd} 週";

    public RosterDelta Plan(IReadOnlySet<Assignment> current)
    {
        var offset = _targetMonday.DayNumber - _sourceMonday.DayNumber;

        // 停用員工或班別的歷史指派不會被複製回來，所以也不清除
        var removed = current.Where(a => InTargetWeek(a.Date) && IsActive(a)).ToHashSet();
        var added = current
            .Where(a => a.Date >= _sourceMonday && a.Date < _sourceMonday.AddDays(7))
            .Where(IsActive)
            .Select(a => a with { Date = a.Date.AddDays(offset) })
            .Where(a => InTargetWeek(a.Date))
            .ToHashSet();

        // 同一筆既被刪除又被新增就是沒變
        var unchanged = removed.Intersect(added).ToList();
        removed.ExceptWith(unchanged);
        added.ExceptWith(unchanged);

        return new RosterDelta(Sorted(added), Sorted(removed));
    }

    private bool IsActive(Assignment a) =>
        _activeEmployeeIds.Contains(a.EmployeeId) && _activeShiftTypeIds.Contains(a.ShiftTypeId);

    private bool InTargetWeek(DateOnly date) =>
        date >= _targetMonday && date < _targetMonday.AddDays(7) && date >= _limitFrom && date <= _limitTo;

    private static List<Assignment> Sorted(IEnumerable<Assignment> items) =>
        items.OrderBy(a => a.Date).ThenBy(a => a.EmployeeId).ThenBy(a => a.ShiftTypeId).ToList();

    private static void RequireMonday(DateOnly date, string paramName)
    {
        if (WeekMath.WeekStart(date) != date)
        {
            throw new ArgumentException($"{date:yyyy-MM-dd} 不是星期一。", paramName);
        }
    }
}
