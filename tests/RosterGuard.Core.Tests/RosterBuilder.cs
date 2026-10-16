using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;

namespace RosterGuard.Core.Tests;

/// <summary>Fluent builder for a roster scenario. Defaults: evaluation range = 2026-10, min rest = 11h.</summary>
internal sealed class RosterBuilder
{
    private readonly List<Employee> _employees = [];
    private readonly List<ShiftType> _shifts = [];
    private readonly List<Assignment> _assignments = [];
    private RuleSettings _settings = new();
    private DateOnly _from = new(2026, 10, 1);
    private DateOnly _to = new(2026, 10, 31);

    public RosterBuilder With(Employee employee)
    {
        _employees.Add(employee);
        return this;
    }

    public RosterBuilder With(ShiftType shift)
    {
        _shifts.Add(shift);
        return this;
    }

    public RosterBuilder Assign(int employeeId, DateOnly date, int shiftTypeId)
    {
        _assignments.Add(new Assignment(employeeId, date, shiftTypeId));
        return this;
    }

    public RosterBuilder AssignDays(int employeeId, DateOnly first, int days, int shiftTypeId)
    {
        for (var i = 0; i < days; i++)
        {
            Assign(employeeId, first.AddDays(i), shiftTypeId);
        }
        return this;
    }

    public RosterBuilder MinRestHours(int hours)
    {
        _settings = new RuleSettings(hours * 60);
        return this;
    }

    public RosterBuilder Range(DateOnly from, DateOnly to)
    {
        _from = from;
        _to = to;
        return this;
    }

    public RosterContext Build() => new(_employees, _shifts, _settings, _assignments, _from, _to);

    public IReadOnlyList<Violation> Evaluate() => RuleEngine.Default.Evaluate(Build());

    public IReadOnlyList<Violation> Evaluate(RuleKind rule) => Evaluate().Where(v => v.Rule == rule).ToList();

    /// <summary>Only per-employee rules; staffing would otherwise flag every shift whose requirement is 0.</summary>
    public IReadOnlyList<Violation> EvaluateIgnoringStaffing() =>
        Evaluate().Where(v => v.Rule is not (RuleKind.Understaffed or RuleKind.Overstaffed)).ToList();
}
