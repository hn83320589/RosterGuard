namespace RosterGuard.Core.Domain;

/// <summary>員工。停用（IsActive = false）者不出現在新排班選單，但歷史指派保留。</summary>
public sealed record Employee(
    int Id,
    string Name,
    Role Role,
    EmploymentType Type,
    int WeeklyLimitMinutes,
    Weekdays UnavailableDays,
    bool IsActive = true);

/// <summary>
/// 班別。End &lt;= Start 代表跨到隔天（例如 18:00 到 02:00）；End == Start 代表 24 小時班。
/// </summary>
public sealed record ShiftType(
    int Id,
    string Name,
    TimeOnly Start,
    TimeOnly End,
    int RequiredKitchen,
    int RequiredFloor,
    bool IsActive = true)
{
    public int RequiredFor(Role role) => role switch
    {
        Role.Kitchen => RequiredKitchen,
        Role.Floor => RequiredFloor,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };
}

/// <summary>一筆指派。Date 為班別的「開始日」。</summary>
public readonly record struct Assignment(int EmployeeId, DateOnly Date, int ShiftTypeId);

/// <summary>可調整的規則參數。</summary>
public sealed record RuleSettings(int MinRestMinutes = RuleSettings.DefaultMinRestMinutes)
{
    public const int DefaultMinRestMinutes = 11 * 60;
}
