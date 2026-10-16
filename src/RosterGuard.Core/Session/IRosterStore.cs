using RosterGuard.Core.Domain;
using RosterGuard.Core.History;

namespace RosterGuard.Core.Session;

/// <summary>持久化介面。由 RosterGuard.Data 以 SQLite 實作；每次呼叫即寫入（autosave）。</summary>
public interface IRosterStore
{
    IReadOnlyList<Employee> LoadEmployees();
    IReadOnlyList<ShiftType> LoadShiftTypes();
    IReadOnlyList<Assignment> LoadAssignments();
    RuleSettings LoadSettings();

    /// <summary>以單一交易寫入差量；失敗時丟出例外且不留下部分寫入。</summary>
    void ApplyChanges(RosterDelta delta);

    /// <summary>Id 為 0 時新增並回傳帶有新 Id 的物件，否則更新。</summary>
    Employee SaveEmployee(Employee employee);

    /// <summary>Id 為 0 時新增並回傳帶有新 Id 的物件，否則更新。</summary>
    ShiftType SaveShiftType(ShiftType shiftType);

    void SaveSettings(RuleSettings settings);
}
