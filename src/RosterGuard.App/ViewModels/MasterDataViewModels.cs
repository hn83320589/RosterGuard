using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Session;
using RosterGuard.Core.Time;

namespace RosterGuard.App.ViewModels;

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class WeekdayChoice(DayOfWeek day) : ObservableObject
{
    public DayOfWeek Day { get; } = day;
    public string Label => Day.ToChinese();
    [ObservableProperty] private bool _isChecked;
}

/// <summary>員工管理：新增、編輯、停用（不提供刪除，歷史班表保留）。</summary>
public sealed partial class EmployeesViewModel : ObservableObject
{
    private static readonly DayOfWeek[] WeekOrder =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private readonly RosterSession _session;
    private readonly Action<string?> _reportError;

    public EmployeesViewModel(RosterSession session, Action<string?> reportError)
    {
        _session = session;
        _reportError = reportError;
        _session.Changed += (_, _) => Reload();
        Reload();
        New();
    }

    public ObservableCollection<Employee> Items { get; } = [];
    public IReadOnlyList<Choice<Role>> Roles { get; } = Enum.GetValues<Role>().Select(r => new Choice<Role>(r, r.ToChinese())).ToList();
    public IReadOnlyList<Choice<EmploymentType>> Types { get; } =
        Enum.GetValues<EmploymentType>().Select(t => new Choice<EmploymentType>(t, t.ToChinese())).ToList();
    public IReadOnlyList<WeekdayChoice> UnavailableDays { get; } = WeekOrder.Select(d => new WeekdayChoice(d)).ToList();

    [ObservableProperty] private Employee? _selected;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private Choice<Role>? _role;
    [ObservableProperty] private Choice<EmploymentType>? _type;
    [ObservableProperty] private decimal? _weeklyLimitHours = 40;
    [ObservableProperty] private bool _isActive = true;

    private int _editingId;

    partial void OnSelectedChanged(Employee? value)
    {
        if (value is null)
        {
            return;
        }

        _editingId = value.Id;
        EditTitle = $"編輯員工：{value.Name}";
        Name = value.Name;
        Role = Roles.First(r => r.Value == value.Role);
        Type = Types.First(t => t.Value == value.Type);
        WeeklyLimitHours = value.WeeklyLimitMinutes / 60m;
        IsActive = value.IsActive;
        foreach (var d in UnavailableDays)
        {
            d.IsChecked = value.UnavailableDays.Contains(d.Day);
        }
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        _editingId = 0;
        EditTitle = "新增員工";
        Name = "";
        Role = Roles[0];
        Type = Types[0];
        WeeklyLimitHours = 40;
        IsActive = true;
        foreach (var d in UnavailableDays)
        {
            d.IsChecked = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var unavailable = UnavailableDays.Where(d => d.IsChecked).Aggregate(Weekdays.None, (acc, d) => acc | d.Day.ToFlag());
            var saved = _session.SaveEmployee(new Employee(_editingId, Name.Trim(), Role!.Value, Type!.Value,
                (int)Math.Round((WeeklyLimitHours ?? 0) * 60), unavailable, IsActive));
            _reportError(null);
            Selected = Items.FirstOrDefault(e => e.Id == saved.Id);
        }
        catch (ArgumentException ex)
        {
            _reportError(ex.Message);
        }
    }

    private void Reload()
    {
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var e in _session.Employees.OrderBy(e => e.Id))
        {
            Items.Add(e);
        }
        Selected = Items.FirstOrDefault(e => e.Id == selectedId);
    }
}

/// <summary>班別管理：新增、編輯、停用。時間以 HH:mm 輸入；結束早於或等於開始代表跨日。</summary>
public sealed partial class ShiftTypesViewModel : ObservableObject
{
    private readonly RosterSession _session;
    private readonly Action<string?> _reportError;
    private int _editingId;

    public ShiftTypesViewModel(RosterSession session, Action<string?> reportError)
    {
        _session = session;
        _reportError = reportError;
        _session.Changed += (_, _) => Reload();
        Reload();
        New();
    }

    public ObservableCollection<ShiftType> Items { get; } = [];

    [ObservableProperty] private ShiftType? _selected;
    [ObservableProperty] private string _editTitle = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DurationText))] private string _startText = "07:00";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DurationText))] private string _endText = "15:00";
    [ObservableProperty] private decimal? _requiredKitchen = 1;
    [ObservableProperty] private decimal? _requiredFloor = 1;
    [ObservableProperty] private bool _isActive = true;

    public string DurationText => TryParse(StartText, out var s) && TryParse(EndText, out var e)
        ? Describe(new ShiftType(0, "", s, e, 0, 0))
        : "時間格式應為 HH:mm";

    partial void OnSelectedChanged(ShiftType? value)
    {
        if (value is null)
        {
            return;
        }

        _editingId = value.Id;
        EditTitle = $"編輯班別：{value.Name}";
        Name = value.Name;
        StartText = value.Start.ToString("HH:mm", CultureInfo.InvariantCulture);
        EndText = value.End.ToString("HH:mm", CultureInfo.InvariantCulture);
        RequiredKitchen = value.RequiredKitchen;
        RequiredFloor = value.RequiredFloor;
        IsActive = value.IsActive;
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        _editingId = 0;
        EditTitle = "新增班別";
        Name = "";
        StartText = "07:00";
        EndText = "15:00";
        RequiredKitchen = 1;
        RequiredFloor = 1;
        IsActive = true;
    }

    [RelayCommand]
    private void Save()
    {
        if (!TryParse(StartText, out var start) || !TryParse(EndText, out var end))
        {
            _reportError("時間格式應為 HH:mm，例如 18:00。");
            return;
        }

        try
        {
            var saved = _session.SaveShiftType(new ShiftType(_editingId, Name.Trim(), start, end,
                (int)(RequiredKitchen ?? 0), (int)(RequiredFloor ?? 0), IsActive));
            _reportError(null);
            Selected = Items.FirstOrDefault(s => s.Id == saved.Id);
        }
        catch (ArgumentException ex)
        {
            _reportError(ex.Message);
        }
    }

    public static string Describe(ShiftType shift)
    {
        var hours = ShiftTime.Duration(shift).TotalHours;
        var overnight = shift.End <= shift.Start ? "，結束於隔天" : "";
        return $"共 {hours:0.##} 小時{overnight}";
    }

    private static bool TryParse(string text, out TimeOnly time) =>
        TimeOnly.TryParseExact(text?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private void Reload()
    {
        var selectedId = Selected?.Id;
        Items.Clear();
        foreach (var s in _session.ShiftTypes.OrderBy(s => s.Id))
        {
            Items.Add(s);
        }
        Selected = Items.FirstOrDefault(s => s.Id == selectedId);
    }
}

/// <summary>規則設定（最小間隔小時數）與範例資料重設。</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly RosterSession _session;
    private readonly Action<string?> _reportError;
    private readonly Action? _resetToSample;

    public SettingsViewModel(RosterSession session, Action<string?> reportError, Action? resetToSample)
    {
        _session = session;
        _reportError = reportError;
        _resetToSample = resetToSample;
        _minRestHours = session.Settings.MinRestMinutes / 60m;
    }

    [ObservableProperty] private decimal? _minRestHours;
    [ObservableProperty] private bool _isConfirmingReset;

    public bool CanReset => _resetToSample is not null;

    [RelayCommand]
    private void Save()
    {
        try
        {
            _session.UpdateSettings(new Core.Domain.RuleSettings((int)Math.Round((MinRestHours ?? 0) * 60)));
            _reportError(null);
        }
        catch (ArgumentException ex)
        {
            _reportError(ex.Message);
        }
    }

    [RelayCommand]
    private void RequestReset() => IsConfirmingReset = true;

    [RelayCommand]
    private void CancelReset() => IsConfirmingReset = false;

    [RelayCommand]
    private void ConfirmReset()
    {
        IsConfirmingReset = false;
        _resetToSample?.Invoke();
        MinRestHours = _session.Settings.MinRestMinutes / 60m;
    }
}
