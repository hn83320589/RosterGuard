using RosterGuard.Core.Domain;

namespace RosterGuard.Core.Tests;

/// <summary>Small builders that keep test arrangements readable.</summary>
internal static class TestData
{
    public static ShiftType Shift(int id, string name, string start, string end, int kitchen = 0, int floor = 0, bool active = true) =>
        new(id, name, TimeOnly.Parse(start), TimeOnly.Parse(end), kitchen, floor, active);

    public static Employee Employee(int id, string name, Role role = Role.Floor, double weeklyLimitHours = 40,
        Weekdays unavailable = Weekdays.None, bool active = true) =>
        new(id, name, role, EmploymentType.FullTime, (int)(weeklyLimitHours * 60), unavailable, active);

    public static DateOnly D(int month, int day) => new(2026, month, day);
}
