using RosterGuard.Core.Candidates;
using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Rules;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Session;

/// <summary>
/// UI 的唯一入口：持有目前狀態、執行可撤銷的操作、寫入儲存區，並在每次變更後重算違規。
/// 違規清單永遠由狀態導出，因此撤銷後自動同步還原。
/// </summary>
public sealed class RosterSession
{
    /// <summary>評估時額外載入的前後天數，讓跨月的間隔、休息日與週工時能被判定。</summary>
    public const int ContextDays = 7;

    private readonly IRosterStore _store;
    private readonly RuleEngine _engine;
    private readonly HashSet<Assignment> _assignments;
    private readonly UndoRedoStack<RosterEdit> _history;
    private List<Employee> _employees;
    private List<ShiftType> _shiftTypes;

    public RosterSession(IRosterStore store, int year, int month, int undoCapacity = 100, RuleEngine? engine = null)
    {
        _store = store;
        _engine = engine ?? RuleEngine.Default;
        _history = new UndoRedoStack<RosterEdit>(undoCapacity);
        _employees = store.LoadEmployees().ToList();
        _shiftTypes = store.LoadShiftTypes().ToList();
        _assignments = store.LoadAssignments().ToHashSet();
        Settings = store.LoadSettings();
        Month = new DateOnly(year, month, 1);
        Violations = Evaluate(_assignments);
    }

    public event EventHandler? Changed;

    /// <summary>目前顯示月份的第一天。</summary>
    public DateOnly Month { get; private set; }

    public DateOnly MonthEnd => Month.AddMonths(1).AddDays(-1);

    public RuleSettings Settings { get; private set; }

    public IReadOnlyList<Violation> Violations { get; private set; }

    public IReadOnlySet<Assignment> Assignments => _assignments;

    public IReadOnlyList<Employee> Employees => _employees;

    public IReadOnlyList<ShiftType> ShiftTypes => _shiftTypes;

    public IReadOnlyList<Employee> ActiveEmployees => _employees.Where(e => e.IsActive).ToList();

    public IReadOnlyList<ShiftType> ActiveShiftTypes => _shiftTypes.Where(s => s.IsActive).ToList();

    public bool CanUndo => _history.CanUndo;

    public bool CanRedo => _history.CanRedo;

    public string? UndoDescription => _history.TryPeekUndo(out var e) ? e.Description : null;

    public string? RedoDescription => _history.TryPeekRedo(out var e) ? e.Description : null;

    /// <summary>重新從儲存區載入所有資料並清空復原歷史（例如重設為範例資料後）。</summary>
    public void Reload()
    {
        _employees = _store.LoadEmployees().ToList();
        _shiftTypes = _store.LoadShiftTypes().ToList();
        _assignments.Clear();
        _assignments.UnionWith(_store.LoadAssignments());
        Settings = _store.LoadSettings();
        _history.Clear();
        Recompute();
    }

    public void SetMonth(int year, int month)
    {
        Month = new DateOnly(year, month, 1);
        Recompute();
    }

    /// <summary>格網要顯示的員工：在職者，加上本月仍有歷史指派的停用者。</summary>
    public IReadOnlyList<Employee> RosterEmployees()
    {
        var withHistory = AssignmentsInMonth().Select(a => a.EmployeeId).ToHashSet();
        return _employees.Where(e => e.IsActive || withHistory.Contains(e.Id)).ToList();
    }

    public IReadOnlyList<Assignment> AssignmentsInMonth() =>
        _assignments.Where(a => a.Date >= Month && a.Date <= MonthEnd)
            .OrderBy(a => a.Date).ThenBy(a => a.EmployeeId).ThenBy(a => a.ShiftTypeId).ToList();

    public IReadOnlyList<ShiftType> ShiftsOn(int employeeId, DateOnly date) =>
        _assignments.Where(a => a.EmployeeId == employeeId && a.Date == date)
            .Select(a => _shiftTypes.First(s => s.Id == a.ShiftTypeId))
            .OrderBy(s => s.Start).ThenBy(s => s.Id).ToList();

    /// <summary>評估「假設狀態」的違規，用於候選人建議。</summary>
    public IReadOnlyList<Violation> Evaluate(IEnumerable<Assignment> assignments) =>
        _engine.Evaluate(BuildContext(assignments));

    public RosterContext BuildContext(IEnumerable<Assignment> assignments)
    {
        var from = Month.AddDays(-ContextDays);
        var to = MonthEnd.AddDays(ContextDays);
        return new RosterContext(_employees, _shiftTypes, Settings,
            assignments.Where(a => a.Date >= from && a.Date <= to), Month, MonthEnd);
    }

    public CandidateResult FindCandidates(StaffingSlot slot) =>
        new CandidateFinder(Evaluate).Find(slot, _employees, ShiftTypeMap(), _assignments, Month, MonthEnd);

    public int MonthMinutes(int employeeId) =>
        CandidateFinder.MonthMinutes(employeeId, _assignments, ShiftTypeMap(), Month, MonthEnd);

    private Dictionary<int, ShiftType> ShiftTypeMap() => _shiftTypes.ToDictionary(s => s.Id);

    public IRosterCommand CopyWeek(DateOnly sourceMonday, DateOnly targetMonday) =>
        new CopyWeekCommand(sourceMonday, targetMonday, Month, MonthEnd,
            ActiveEmployees.Select(e => e.Id).ToHashSet(),
            ActiveShiftTypes.Select(s => s.Id).ToHashSet());

    /// <summary>執行操作；沒有實際變更時回傳 false，且不進入歷史、不寫入儲存區。</summary>
    public bool Execute(IRosterCommand command)
    {
        var delta = command.Plan(_assignments);
        if (delta.IsEmpty)
        {
            return false;
        }

        Apply(delta);
        _history.Push(new RosterEdit(command.Description, delta));
        Recompute();
        return true;
    }

    public bool Undo()
    {
        if (!_history.TryPeekUndo(out var edit))
        {
            return false;
        }

        Apply(edit.Delta.Inverse());
        _history.TryUndo(out _);
        ShowMonthOf(edit.Delta);
        Recompute();
        return true;
    }

    public bool Redo()
    {
        if (!_history.TryPeekRedo(out var edit))
        {
            return false;
        }

        Apply(edit.Delta);
        _history.TryRedo(out _);
        ShowMonthOf(edit.Delta);
        Recompute();
        return true;
    }

    public Employee SaveEmployee(Employee employee)
    {
        ValidateEmployee(employee);
        var saved = _store.SaveEmployee(employee);
        _employees = _store.LoadEmployees().ToList();
        Recompute();
        return saved;
    }

    public ShiftType SaveShiftType(ShiftType shiftType)
    {
        ValidateShiftType(shiftType);
        var saved = _store.SaveShiftType(shiftType);
        _shiftTypes = _store.LoadShiftTypes().ToList();
        Recompute();
        return saved;
    }

    public void UpdateSettings(RuleSettings settings)
    {
        if (settings.MinRestMinutes is < 0 or > 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "最小間隔必須介於 0 到 24 小時。");
        }

        _store.SaveSettings(settings);
        Settings = settings;
        Recompute();
    }

    private void Apply(RosterDelta delta)
    {
        _store.ApplyChanges(delta); // 先寫入；失敗時記憶體狀態不變
        _assignments.ExceptWith(delta.Removed);
        _assignments.UnionWith(delta.Added);
    }

    private void ShowMonthOf(RosterDelta delta)
    {
        if (delta.Dates.Any(d => d >= Month && d <= MonthEnd))
        {
            return;
        }

        var first = delta.Dates.Min();
        Month = new DateOnly(first.Year, first.Month, 1);
    }

    private void Recompute()
    {
        Violations = Evaluate(_assignments);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static void ValidateEmployee(Employee e)
    {
        if (string.IsNullOrWhiteSpace(e.Name))
        {
            throw new ArgumentException("員工姓名不可空白。", nameof(e));
        }

        if (e.WeeklyLimitMinutes is <= 0 or > 7 * 24 * 60)
        {
            throw new ArgumentOutOfRangeException(nameof(e), "每週工時上限必須介於 0 到 168 小時。");
        }
    }

    private static void ValidateShiftType(ShiftType s)
    {
        if (string.IsNullOrWhiteSpace(s.Name))
        {
            throw new ArgumentException("班別名稱不可空白。", nameof(s));
        }

        if (s.RequiredKitchen < 0 || s.RequiredFloor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(s), "需求人數不可為負數。");
        }
    }

    private sealed record RosterEdit(string Description, RosterDelta Delta);
}
