using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RosterGuard.Core.Candidates;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;

namespace RosterGuard.App.ViewModels;

public sealed record DayHeaderViewModel(DateOnly Date)
{
    public string DayText => $"{Date:MM/dd}";
    public string WeekdayText => Date.DayOfWeek.ToChinese();
    public bool IsWeekend => Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
    public IBrush Background => IsWeekend ? ViolationStyle.WeekendHeader : ViolationStyle.NormalHeader;
}

/// <summary>格子 Flyout 裡的一個班別勾選項；勾選 = 指派，取消勾選 = 取消指派。</summary>
public sealed partial class ShiftOptionViewModel(CellViewModel cell, ShiftType shift, Action<CellViewModel, ShiftType, bool> toggle)
    : ObservableObject
{
    private bool _isChecked;

    public ShiftType Shift { get; } = shift;
    public string Label => $"{Shift.Name}  {Shift.Start:HH\\:mm}–{Shift.End:HH\\:mm}{(Shift.IsActive ? "" : "（已停用）")}";

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }
            toggle(cell, Shift, value);
            OnPropertyChanged(); // 若執行失敗，Sync 會把狀態校正回實際指派
        }
    }

    /// <summary>由資料同步勾選狀態，不觸發指派。</summary>
    internal void Sync(bool isChecked) => SetProperty(ref _isChecked, isChecked, nameof(IsChecked));
}

public sealed partial class CellViewModel(int employeeId, DateOnly date) : ObservableObject
{
    public int EmployeeId { get; } = employeeId;
    public DateOnly Date { get; } = date;
    public CellRef Ref => new(EmployeeId, Date);

    public ObservableCollection<ShiftOptionViewModel> Options { get; } = [];

    [ObservableProperty] private string _shiftText = "";
    [ObservableProperty] private IBrush _background = ViolationStyle.NormalCell;
    [ObservableProperty] private IReadOnlyList<BadgeViewModel> _badges = [];
    [ObservableProperty] private string? _toolTip;
    [ObservableProperty] private bool _isHighlighted;

    internal void Update(IReadOnlyList<ShiftType> assigned, IReadOnlyList<Violation> violations)
    {
        ShiftText = string.Join("\n", assigned.Select(s => s.Name));
        foreach (var option in Options)
        {
            option.Sync(assigned.Any(s => s.Id == option.Shift.Id));
        }

        var kinds = violations.Select(v => v.Rule).Distinct().OrderBy(r => r.Severity()).ThenBy(r => r).ToList();
        Background = kinds.Count > 0 ? ViolationStyle.Background(kinds[0]) : ViolationStyle.NormalCell;
        Badges = kinds.Select(k => new BadgeViewModel(ViolationStyle.Badge(k), ViolationStyle.Accent(k))).ToList();
        ToolTip = violations.Count > 0 ? string.Join("\n", violations.Select(v => $"[{v.Rule.ToChinese()}] {v.Message}")) : null;
    }
}

public sealed class EmployeeRowViewModel(Employee employee, IReadOnlyList<CellViewModel> cells)
{
    public Employee Employee { get; } = employee;
    public string Name => Employee.Name;
    public string SubTitle => $"{Employee.Role.ToChinese()}・{Employee.Type.ToChinese()}{(Employee.IsActive ? "" : "・停用")}";
    public IReadOnlyList<CellViewModel> Cells { get; } = cells;
}

/// <summary>人力需求列的一格：某天某班某職稱「已排/需求」。</summary>
public sealed partial class CoverageCellViewModel(StaffingSlot slot, IRelayCommand<CoverageCellViewModel> select) : ObservableObject
{
    public StaffingSlot Slot { get; } = slot;
    public IRelayCommand<CoverageCellViewModel> SelectCommand { get; } = select;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private IBrush _background = ViolationStyle.NormalCell;
    [ObservableProperty] private bool _isHighlighted;
    [ObservableProperty] private string? _toolTip;

    internal void Update(int assigned, int required, Violation? violation)
    {
        Text = $"{assigned}/{required}";
        Background = violation is null ? ViolationStyle.NormalCell : ViolationStyle.Background(violation.Rule);
        ToolTip = violation?.Message;
    }
}

public sealed class CoverageRowViewModel(ShiftType shift, Role role, IReadOnlyList<CoverageCellViewModel> cells)
{
    public string Name => shift.Name;
    public string SubTitle => $"{role.ToChinese()}需求 {shift.RequiredFor(role)}";
    public IReadOnlyList<CoverageCellViewModel> Cells { get; } = cells;
}

public sealed class ViolationItemViewModel(Violation violation, string employeeName, string? slotText)
{
    public Violation Violation { get; } = violation;
    public ViolationKey Key => Violation.Key;
    public string SeverityText => Violation.Severity == Severity.Error ? "錯誤" : "警告";
    public IBrush SeverityBrush => Violation.Severity == Severity.Error ? Brushes.Firebrick : Brushes.DarkOrange;
    public string RuleText => Violation.Rule.ToChinese();
    public string Badge => ViolationStyle.Badge(Violation.Rule);
    public IBrush Accent => ViolationStyle.Accent(Violation.Rule);
    public string Who => slotText ?? employeeName;
    public string DateText => $"{Violation.Date:MM/dd}({Violation.Date.DayOfWeek.ToChinese()})";
    public string Message => Violation.Message;
}

public sealed record CandidateItemViewModel(Candidate Candidate, IRelayCommand<CandidateItemViewModel> AssignCommand)
{
    public string Name => Candidate.Employee.Name;
    public string HoursText => $"本月 {Candidate.MonthMinutes / 60.0:0.#} 小時";
}

public sealed record WeekOption(DateOnly Monday)
{
    public string Label => $"{Monday:MM/dd}–{Monday.AddDays(6):MM/dd}";
}
