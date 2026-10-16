using RosterGuard.Core.Domain;

namespace RosterGuard.Core.Rules;

/// <summary>規則種類。宣告順序即同嚴重度、同日期時的排序。</summary>
public enum RuleKind
{
    Overlap,
    InsufficientRest,
    MissingRestDay,
    WeeklyHoursExceeded,
    UnavailableDay,
    Understaffed,
    Overstaffed,
}

public enum Severity
{
    Error = 0,
    Warning = 1,
}

/// <summary>
/// 違規的身分鍵：不含說明文字與數量，因此「人力 0/2 → 1/2」仍是同一筆違規。
/// 候選人建議以鍵的集合差判斷「是否產生新違規」。
/// </summary>
public readonly record struct ViolationKey(
    RuleKind Rule,
    int? EmployeeId,
    DateOnly Date,
    int? ShiftTypeId = null,
    Role? Role = null,
    string Discriminator = "");

/// <summary>格網中「員工 × 日期」的一格。</summary>
public readonly record struct CellRef(int EmployeeId, DateOnly Date);

public sealed record Violation(ViolationKey Key, Severity Severity, string Message, IReadOnlyList<CellRef> Cells)
{
    public RuleKind Rule => Key.Rule;
    public DateOnly Date => Key.Date;
}

public static class RuleKindExtensions
{
    public static Severity Severity(this RuleKind rule) => rule switch
    {
        RuleKind.Overlap or RuleKind.InsufficientRest or RuleKind.MissingRestDay or RuleKind.WeeklyHoursExceeded
            => Rules.Severity.Error,
        _ => Rules.Severity.Warning,
    };

    public static string ToChinese(this RuleKind rule) => rule switch
    {
        RuleKind.Overlap => "時段重疊",
        RuleKind.InsufficientRest => "間隔不足",
        RuleKind.MissingRestDay => "缺少休息日",
        RuleKind.WeeklyHoursExceeded => "週工時超過上限",
        RuleKind.UnavailableDay => "不可上班日",
        RuleKind.Understaffed => "人力不足",
        RuleKind.Overstaffed => "人力超額",
        _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, null),
    };
}
