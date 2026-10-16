using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RosterGuard.Core.Candidates;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Export;
using RosterGuard.Core.History;
using RosterGuard.Core.Rules;
using RosterGuard.Core.Session;
using RosterGuard.Core.Time;

namespace RosterGuard.App.ViewModels;

/// <summary>月班表頁：格網、人力需求列、違規面板、候選人建議。所有編輯經由 <see cref="RosterSession"/>。</summary>
public sealed partial class RosterViewModel : ObservableObject
{
    private readonly RosterSession _session;
    private readonly Action<string?> _reportError;
    private string _structureKey = "";
    private Dictionary<CellRef, CellViewModel> _cells = [];
    private Dictionary<StaffingSlot, CoverageCellViewModel> _coverage = [];
    private bool _restoringSelection;

    public RosterViewModel(RosterSession session, Action<string?> reportError)
    {
        _session = session;
        _reportError = reportError;
        _session.Changed += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>要求畫面捲動到指定的格子（CellViewModel 或 CoverageCellViewModel）。</summary>
    public event EventHandler<object>? ScrollRequested;

    public ObservableCollection<DayHeaderViewModel> Days { get; } = [];
    public ObservableCollection<EmployeeRowViewModel> Rows { get; } = [];
    public ObservableCollection<CoverageRowViewModel> CoverageRows { get; } = [];
    public ObservableCollection<ViolationItemViewModel> Violations { get; } = [];
    public ObservableCollection<CandidateItemViewModel> Candidates { get; } = [];
    public ObservableCollection<WeekOption> Weeks { get; } = [];
    public IReadOnlyList<LegendItem> Legend => LegendItem.All;

    [ObservableProperty] private string _monthTitle = "";
    [ObservableProperty] private string _violationSummary = "";
    [ObservableProperty] private ViolationItemViewModel? _selectedViolation;
    [ObservableProperty] private string _candidateTitle = "點選下方人力需求列的格子以查看候選人";
    [ObservableProperty] private string? _candidateMessage;
    [ObservableProperty] private WeekOption? _copySource;
    [ObservableProperty] private WeekOption? _copyTarget;
    [ObservableProperty] private string _undoText = "復原";
    [ObservableProperty] private string _redoText = "重做";

    public StaffingSlot? SelectedSlot { get; private set; }

    public RosterSession Session => _session;

    public IReadOnlyList<Employee> ExportEmployees => _session.RosterEmployees();

    [RelayCommand]
    private void PreviousMonth() => SetMonth(_session.Month.AddMonths(-1));

    [RelayCommand]
    private void NextMonth() => SetMonth(_session.Month.AddMonths(1));

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Run(() => _session.Undo());

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Run(() => _session.Redo());

    private bool CanUndo() => _session.CanUndo;

    private bool CanRedo() => _session.CanRedo;

    [RelayCommand]
    private void CopyWeek()
    {
        if (CopySource is null || CopyTarget is null)
        {
            _reportError("請先選擇來源週與目標週。");
            return;
        }

        Run(() => _session.Execute(_session.CopyWeek(CopySource.Monday, CopyTarget.Monday)));
    }

    [RelayCommand]
    private void SelectSlot(CoverageCellViewModel? cell)
    {
        if (cell is null)
        {
            return;
        }

        SelectedSlot = cell.Slot;
        HighlightOnly(coverage: cell);
        UpdateCandidates();
    }

    [RelayCommand]
    private void AssignCandidate(CandidateItemViewModel? candidate)
    {
        if (candidate is null || SelectedSlot is null)
        {
            return;
        }

        var slot = SelectedSlot;
        Run(() => _session.Execute(new AssignCommand(new Assignment(candidate.Candidate.Employee.Id, slot.Date, slot.ShiftTypeId))));
    }

    public byte[] ExportCsv() =>
        CsvRosterExporter.Export(_session.Month.Year, _session.Month.Month, _session.RosterEmployees(),
            _session.ShiftTypes, _session.AssignmentsInMonth());

    public byte[] ExportIcs(Employee employee) =>
        IcsExporter.Export(employee, _session.ShiftTypes, _session.AssignmentsInMonth(), DateTime.UtcNow);

    /// <summary>View 層（例如存檔失敗）回報錯誤；null 代表清除錯誤訊息。</summary>
    public void ReportError(string? message) => _reportError(message);

    public string CsvFileName => $"班表_{_session.Month:yyyy-MM}.csv";

    public string IcsFileName(Employee employee) => $"{employee.Name}_{_session.Month:yyyy-MM}.ics";

    /// <summary>直接切換某格某班的指派（測試與 Flyout 共用）。</summary>
    public void Toggle(CellViewModel cell, ShiftType shift, bool assign)
    {
        var assignment = new Assignment(cell.EmployeeId, cell.Date, shift.Id);
        Run(() => _session.Execute(assign ? new AssignCommand(assignment) : new UnassignCommand(assignment)));
    }

    public CellViewModel Cell(int employeeId, DateOnly date) => _cells[new CellRef(employeeId, date)];

    public CoverageCellViewModel Coverage(StaffingSlot slot) => _coverage[slot];

    partial void OnSelectedViolationChanged(ViolationItemViewModel? value)
    {
        if (value is null)
        {
            if (!_restoringSelection)
            {
                HighlightOnly();
            }
            return;
        }

        var v = value.Violation;
        if (v.Key is { ShiftTypeId: { } shiftId, Role: { } role, EmployeeId: null })
        {
            var slot = new StaffingSlot(v.Date, shiftId, role);
            if (_coverage.TryGetValue(slot, out var coverage))
            {
                HighlightOnly(coverage: coverage);
                if (!_restoringSelection)
                {
                    SelectedSlot = slot;
                    UpdateCandidates();
                    ScrollRequested?.Invoke(this, coverage);
                }
            }
            return;
        }

        var cells = v.Cells.Select(c => _cells.GetValueOrDefault(c)).OfType<CellViewModel>().ToList();
        HighlightOnly(cells: cells);
        var target = _cells.GetValueOrDefault(new CellRef(v.Key.EmployeeId ?? 0, v.Date)) ?? cells.FirstOrDefault();
        if (target is not null && !_restoringSelection)
        {
            ScrollRequested?.Invoke(this, target);
        }
    }

    private void SetMonth(DateOnly month)
    {
        SelectedSlot = null;
        _session.SetMonth(month.Year, month.Month);
    }

    private void Run(Action action)
    {
        try
        {
            action();
            _reportError(null);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or Microsoft.Data.Sqlite.SqliteException)
        {
            _reportError($"操作失敗：{ex.Message}");
            Refresh(); // 讓勾選狀態等回到實際資料
        }
    }

    private void Refresh()
    {
        var key = StructureKey();
        if (key != _structureKey)
        {
            Rebuild();
            _structureKey = key;
        }

        var byCell = _session.Violations
            .SelectMany(v => v.Cells.Select(c => (Cell: c, Violation: v)))
            .ToLookup(x => x.Cell, x => x.Violation);
        foreach (var (cellRef, cell) in _cells)
        {
            cell.Update(_session.ShiftsOn(cellRef.EmployeeId, cellRef.Date), byCell[cellRef].ToList());
        }

        UpdateCoverage();
        UpdateViolationList();
        UpdateCandidates();

        UndoText = _session.UndoDescription is { } u ? $"復原（{u}）" : "復原";
        RedoText = _session.RedoDescription is { } r ? $"重做（{r}）" : "重做";
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private string StructureKey() => string.Join("|",
        _session.Month.ToString("yyyy-MM"),
        string.Join(",", _session.RosterEmployees().Select(e => e.GetHashCode())),
        string.Join(",", _session.ShiftTypes.Select(s => s.GetHashCode())));

    private void Rebuild()
    {
        var month = _session.Month;
        MonthTitle = $"{month.Year} 年 {month.Month} 月";
        var days = WeekMath.DaysOfMonth(month.Year, month.Month).ToList();

        Days.Clear();
        foreach (var d in days)
        {
            Days.Add(new DayHeaderViewModel(d));
        }

        _cells = [];
        Rows.Clear();
        foreach (var employee in _session.RosterEmployees())
        {
            var cells = days.Select(d => new CellViewModel(employee.Id, d)).ToList();
            foreach (var cell in cells)
            {
                _cells[cell.Ref] = cell;
            }
            Rows.Add(new EmployeeRowViewModel(employee, cells));
        }

        // 選項：啟用中的班別；停用班別只在已有指派時出現（Update 時補上）
        foreach (var cell in _cells.Values)
        {
            foreach (var shift in _session.ShiftTypes.Where(s => s.IsActive || HasAssignment(cell, s)).OrderBy(s => s.Start))
            {
                cell.Options.Add(new ShiftOptionViewModel(cell, shift, Toggle));
            }
        }

        _coverage = [];
        CoverageRows.Clear();
        foreach (var shift in _session.ActiveShiftTypes.OrderBy(s => s.Start))
        {
            foreach (var role in Enum.GetValues<Role>())
            {
                var cells = days.Select(d => new CoverageCellViewModel(new StaffingSlot(d, shift.Id, role), SelectSlotCommand)).ToList();
                foreach (var c in cells)
                {
                    _coverage[c.Slot] = c;
                }
                CoverageRows.Add(new CoverageRowViewModel(shift, role, cells));
            }
        }

        Weeks.Clear();
        for (var monday = WeekMath.WeekStart(month); monday <= _session.MonthEnd; monday = monday.AddDays(7))
        {
            Weeks.Add(new WeekOption(monday));
        }
        CopySource = Weeks.FirstOrDefault();
        CopyTarget = Weeks.Skip(1).FirstOrDefault();

        if (SelectedSlot is not null && !_coverage.ContainsKey(SelectedSlot))
        {
            SelectedSlot = null;
        }
        OnPropertyChanged(nameof(ExportEmployees));
    }

    private bool HasAssignment(CellViewModel cell, ShiftType shift) =>
        _session.Assignments.Contains(new Assignment(cell.EmployeeId, cell.Date, shift.Id));

    private void UpdateCoverage()
    {
        var counts = _session.AssignmentsInMonth()
            .GroupBy(a => (a.Date, a.ShiftTypeId, Role: _session.Employees.First(e => e.Id == a.EmployeeId).Role))
            .ToDictionary(g => g.Key, g => g.Count());
        var staffing = _session.Violations
            .Where(v => v.Key.EmployeeId is null && v.Key.ShiftTypeId is not null && v.Key.Role is not null)
            .ToDictionary(v => new StaffingSlot(v.Date, v.Key.ShiftTypeId!.Value, v.Key.Role!.Value));

        foreach (var (slot, cell) in _coverage)
        {
            var shift = _session.ShiftTypes.First(s => s.Id == slot.ShiftTypeId);
            cell.Update(counts.GetValueOrDefault((slot.Date, slot.ShiftTypeId, slot.Role)), shift.RequiredFor(slot.Role),
                staffing.GetValueOrDefault(slot));
        }
    }

    private void UpdateViolationList()
    {
        var selectedKey = SelectedViolation?.Key;
        Violations.Clear();
        foreach (var v in _session.Violations)
        {
            var name = v.Key.EmployeeId is { } id ? _session.Employees.First(e => e.Id == id).Name : "";
            var slot = v.Key.EmployeeId is null && v.Key.ShiftTypeId is { } sid && v.Key.Role is { } role
                ? $"{_session.ShiftTypes.First(s => s.Id == sid).Name}・{role.ToChinese()}"
                : null;
            Violations.Add(new ViolationItemViewModel(v, name, slot));
        }

        // 盡量保留原本的選取（同一違規鍵）；不觸發捲動
        var keep = Violations.FirstOrDefault(v => v.Key == selectedKey);
        _restoringSelection = true;
        try
        {
            SelectedViolation = keep;
        }
        finally
        {
            _restoringSelection = false;
        }
        if (keep is null && SelectedSlot is null)
        {
            HighlightOnly();
        }

        var errors = _session.Violations.Count(v => v.Severity == Severity.Error);
        ViolationSummary = $"違規 {_session.Violations.Count} 筆（錯誤 {errors}／警告 {_session.Violations.Count - errors}）";
    }

    private void UpdateCandidates()
    {
        Candidates.Clear();
        CandidateMessage = null;
        if (SelectedSlot is not { } slot)
        {
            CandidateTitle = "點選下方人力需求列的格子以查看候選人";
            return;
        }

        var shift = _session.ShiftTypes.First(s => s.Id == slot.ShiftTypeId);
        CandidateTitle = $"{slot.Date:MM/dd}({slot.Date.DayOfWeek.ToChinese()}) {shift.Name}・{slot.Role.ToChinese()}";

        var assigned = _session.AssignmentsInMonth()
            .Count(a => a.Date == slot.Date && a.ShiftTypeId == slot.ShiftTypeId && _session.Employees.First(e => e.Id == a.EmployeeId).Role == slot.Role);
        if (assigned >= shift.RequiredFor(slot.Role))
        {
            CandidateMessage = $"人力已足夠（已排 {assigned}／需求 {shift.RequiredFor(slot.Role)}）";
            return;
        }

        var result = _session.FindCandidates(slot);
        foreach (var c in result.Candidates)
        {
            Candidates.Add(new CandidateItemViewModel(c, AssignCandidateCommand));
        }
        CandidateMessage = result.NoCandidateReason;
    }

    private void HighlightOnly(IReadOnlyCollection<CellViewModel>? cells = null, CoverageCellViewModel? coverage = null)
    {
        foreach (var c in _cells.Values)
        {
            c.IsHighlighted = cells?.Contains(c) == true;
        }

        foreach (var c in _coverage.Values)
        {
            c.IsHighlighted = c == coverage;
        }
    }
}
