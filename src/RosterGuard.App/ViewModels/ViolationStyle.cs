using Avalonia.Media;
using RosterGuard.Core.Rules;

namespace RosterGuard.App.ViewModels;

/// <summary>各違規類型在格網上的顏色與徽章文字（圖例同步使用）。</summary>
public static class ViolationStyle
{
    public static readonly IBrush NormalCell = Brushes.White;
    public static readonly IBrush WeekendHeader = new SolidColorBrush(Color.Parse("#FFF3E0"));
    public static readonly IBrush NormalHeader = new SolidColorBrush(Color.Parse("#F5F5F5"));

    public static IBrush Background(RuleKind rule) => new SolidColorBrush(Color.Parse(rule switch
    {
        RuleKind.Overlap => "#FFCDD2",
        RuleKind.InsufficientRest => "#FFE0B2",
        RuleKind.MissingRestDay => "#E1BEE7",
        RuleKind.WeeklyHoursExceeded => "#FFF59D",
        RuleKind.UnavailableDay => "#B3E5FC",
        RuleKind.Understaffed => "#FFCCBC",
        RuleKind.Overstaffed => "#C8E6C9",
        _ => "#FFFFFF",
    }));

    public static IBrush Accent(RuleKind rule) => new SolidColorBrush(Color.Parse(rule switch
    {
        RuleKind.Overlap => "#C62828",
        RuleKind.InsufficientRest => "#E65100",
        RuleKind.MissingRestDay => "#6A1B9A",
        RuleKind.WeeklyHoursExceeded => "#F57F17",
        RuleKind.UnavailableDay => "#0277BD",
        RuleKind.Understaffed => "#D84315",
        RuleKind.Overstaffed => "#2E7D32",
        _ => "#000000",
    }));

    /// <summary>單字徽章，顯示於格子右上角。</summary>
    public static string Badge(RuleKind rule) => rule switch
    {
        RuleKind.Overlap => "疊",
        RuleKind.InsufficientRest => "隔",
        RuleKind.MissingRestDay => "休",
        RuleKind.WeeklyHoursExceeded => "時",
        RuleKind.UnavailableDay => "禁",
        RuleKind.Understaffed => "缺",
        RuleKind.Overstaffed => "超",
        _ => "?",
    };
}

public sealed record LegendItem(string Badge, string Name, IBrush Background, IBrush Accent)
{
    public static IReadOnlyList<LegendItem> All { get; } = Enum.GetValues<RuleKind>()
        .Select(r => new LegendItem(ViolationStyle.Badge(r), r.ToChinese(), ViolationStyle.Background(r), ViolationStyle.Accent(r)))
        .ToList();
}

public sealed record BadgeViewModel(string Text, IBrush Accent);
