using RosterGuard.Core.Domain;

namespace RosterGuard.Core.Time;

/// <summary>半開區間 [Start, End)，以本地牆鐘時間表示。</summary>
public readonly record struct ShiftInterval(DateTime Start, DateTime End)
{
    public int Minutes => (int)(End - Start).TotalMinutes;

    public bool Overlaps(ShiftInterval other) => Start < other.End && other.Start < End;

    /// <summary>此區間是否碰到該日曆日的 [00:00, 24:00)。</summary>
    public bool Touches(DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        return Overlaps(new ShiftInterval(dayStart, dayStart.AddDays(1)));
    }
}

public static class ShiftTime
{
    public static TimeSpan Duration(ShiftType shift)
    {
        var diff = shift.End.ToTimeSpan() - shift.Start.ToTimeSpan();
        return diff > TimeSpan.Zero ? diff : diff + TimeSpan.FromDays(1);
    }

    public static ShiftInterval Interval(DateOnly date, ShiftType shift)
    {
        var start = date.ToDateTime(shift.Start);
        return new ShiftInterval(start, start + Duration(shift));
    }
}

public static class WeekMath
{
    /// <summary>該日期所屬週的週一。</summary>
    public static DateOnly WeekStart(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0 ... Sunday = 6
        return date.AddDays(-offset);
    }

    public static DateOnly MonthStart(DateOnly date) => new(date.Year, date.Month, 1);

    public static IEnumerable<DateOnly> DaysOfMonth(int year, int month)
    {
        var first = new DateOnly(year, month, 1);
        for (var d = first; d.Month == month; d = d.AddDays(1))
        {
            yield return d;
        }
    }
}
