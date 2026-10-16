namespace RosterGuard.Core.Domain;

/// <summary>職稱。</summary>
public enum Role
{
    Kitchen = 0, // 內場
    Floor = 1,   // 外場
}

/// <summary>僱用類型。</summary>
public enum EmploymentType
{
    FullTime = 0, // 正職
    PartTime = 1, // 兼職
}

/// <summary>星期的位元集合，用於「固定不可上班的星期」。</summary>
[Flags]
public enum Weekdays
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
}

public static class DomainText
{
    public static string ToChinese(this Role role) => role switch
    {
        Role.Kitchen => "內場",
        Role.Floor => "外場",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public static string ToChinese(this EmploymentType type) => type switch
    {
        EmploymentType.FullTime => "正職",
        EmploymentType.PartTime => "兼職",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static string ToChinese(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "一",
        DayOfWeek.Tuesday => "二",
        DayOfWeek.Wednesday => "三",
        DayOfWeek.Thursday => "四",
        DayOfWeek.Friday => "五",
        DayOfWeek.Saturday => "六",
        DayOfWeek.Sunday => "日",
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
    };
}

public static class WeekdaysExtensions
{
    public static Weekdays ToFlag(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => Weekdays.Monday,
        DayOfWeek.Tuesday => Weekdays.Tuesday,
        DayOfWeek.Wednesday => Weekdays.Wednesday,
        DayOfWeek.Thursday => Weekdays.Thursday,
        DayOfWeek.Friday => Weekdays.Friday,
        DayOfWeek.Saturday => Weekdays.Saturday,
        DayOfWeek.Sunday => Weekdays.Sunday,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
    };

    public static bool Contains(this Weekdays days, DayOfWeek day) => (days & day.ToFlag()) != 0;
}
