using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Session;

namespace RosterGuard.Core.Tests;

/// <summary>In-memory IRosterStore used to test the session without SQLite.</summary>
internal sealed class FakeRosterStore : IRosterStore
{
    public List<Employee> Employees { get; } = [];
    public List<ShiftType> ShiftTypes { get; } = [];
    public HashSet<Assignment> Assignments { get; } = [];
    public RuleSettings Settings { get; set; } = new();
    public int ApplyCount { get; private set; }
    public bool FailNextApply { get; set; }

    public IReadOnlyList<Employee> LoadEmployees() => Employees.ToList();
    public IReadOnlyList<ShiftType> LoadShiftTypes() => ShiftTypes.ToList();
    public IReadOnlyList<Assignment> LoadAssignments() => Assignments.ToList();
    public RuleSettings LoadSettings() => Settings;

    public void ApplyChanges(RosterDelta delta)
    {
        if (FailNextApply)
        {
            FailNextApply = false;
            throw new IOException("disk full");
        }

        ApplyCount++;
        Assignments.ExceptWith(delta.Removed);
        Assignments.UnionWith(delta.Added);
    }

    public Employee SaveEmployee(Employee employee)
    {
        var saved = employee.Id == 0 ? employee with { Id = Employees.Count + 1 } : employee;
        Employees.RemoveAll(e => e.Id == saved.Id);
        Employees.Add(saved);
        return saved;
    }

    public ShiftType SaveShiftType(ShiftType shiftType)
    {
        var saved = shiftType.Id == 0 ? shiftType with { Id = ShiftTypes.Count + 1 } : shiftType;
        ShiftTypes.RemoveAll(s => s.Id == saved.Id);
        ShiftTypes.Add(saved);
        return saved;
    }

    public void SaveSettings(RuleSettings settings) => Settings = settings;
}
