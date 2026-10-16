using RosterGuard.Core.Domain;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Tests.Time;

public class ShiftTimeTests
{
    private static ShiftType Shift(string start, string end) =>
        TestData.Shift(1, "班", start, end);

    [Fact]
    public void Overnight_shift_18_to_02_is_8_hours()
    {
        Assert.Equal(TimeSpan.FromHours(8), ShiftTime.Duration(Shift("18:00", "02:00")));
    }

    [Fact]
    public void Overnight_shift_ends_on_next_day()
    {
        var interval = ShiftTime.Interval(new DateOnly(2026, 10, 1), Shift("18:00", "02:00"));

        Assert.Equal(new DateTime(2026, 10, 1, 18, 0, 0), interval.Start);
        Assert.Equal(new DateTime(2026, 10, 2, 2, 0, 0), interval.End);
    }

    [Fact]
    public void Same_day_shift_ends_on_same_day()
    {
        var interval = ShiftTime.Interval(new DateOnly(2026, 10, 1), Shift("07:00", "15:00"));

        Assert.Equal(new DateTime(2026, 10, 1, 7, 0, 0), interval.Start);
        Assert.Equal(new DateTime(2026, 10, 1, 15, 0, 0), interval.End);
        Assert.Equal(8 * 60, interval.Minutes);
    }

    [Fact]
    public void End_equal_to_start_means_24_hour_shift()
    {
        Assert.Equal(TimeSpan.FromHours(24), ShiftTime.Duration(Shift("08:00", "08:00")));
    }

    [Fact]
    public void Shift_ending_at_midnight_ends_at_start_of_next_day()
    {
        var interval = ShiftTime.Interval(new DateOnly(2026, 10, 31), Shift("16:00", "00:00"));

        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0), interval.End);
        Assert.Equal(8 * 60, interval.Minutes);
    }

    [Theory]
    [InlineData("2026-10-12", "2026-10-12")] // Monday
    [InlineData("2026-10-14", "2026-10-12")] // Wednesday
    [InlineData("2026-10-18", "2026-10-12")] // Sunday belongs to the preceding Monday
    [InlineData("2026-11-01", "2026-10-26")] // week crossing month boundary
    public void Week_starts_on_monday(string date, string expectedMonday)
    {
        Assert.Equal(DateOnly.Parse(expectedMonday), WeekMath.WeekStart(DateOnly.Parse(date)));
    }

    [Fact]
    public void Weekdays_flags_contain_selected_days_only()
    {
        var days = Weekdays.Saturday | Weekdays.Sunday;

        Assert.True(days.Contains(DayOfWeek.Sunday));
        Assert.True(days.Contains(DayOfWeek.Saturday));
        Assert.False(days.Contains(DayOfWeek.Monday));
    }
}
