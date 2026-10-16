using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Rules;
using RosterGuard.Core.Session;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Session;

public class RosterSessionTests
{
    private const int Morning = 1, Closing = 2;

    private static FakeRosterStore Store()
    {
        var store = new FakeRosterStore();
        store.Employees.Add(Employee(1, "小明"));
        store.Employees.Add(Employee(2, "小華"));
        store.Employees.Add(Employee(3, "離職者", active: false));
        store.ShiftTypes.Add(Shift(Morning, "早班", "07:00", "15:00", floor: 1));
        store.ShiftTypes.Add(Shift(Closing, "打烊班", "18:00", "02:00"));
        return store;
    }

    private static RosterSession Session(FakeRosterStore store) => new(store, 2026, 10);

    [Fact]
    public void Assign_updates_violations_and_persists_immediately()
    {
        var store = Store();
        var session = Session(store);
        var before = session.Violations.Count(v => v.Rule == RuleKind.Understaffed);

        session.Execute(new AssignCommand(new Assignment(1, D(10, 5), Morning)));

        Assert.Equal(before - 1, session.Violations.Count(v => v.Rule == RuleKind.Understaffed));
        Assert.Contains(new Assignment(1, D(10, 5), Morning), store.Assignments);
    }

    [Fact]
    public void Undo_restores_assignments_and_violations()
    {
        var store = Store();
        var session = Session(store);
        var violationsBefore = session.Violations.Select(v => v.Key).ToList();

        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Closing)));
        session.Execute(new AssignCommand(new Assignment(1, D(10, 2), Morning))); // 間隔不足
        Assert.Contains(session.Violations, v => v.Rule == RuleKind.InsufficientRest);

        session.Undo();
        session.Undo();

        Assert.Empty(session.Assignments);
        Assert.Empty(store.Assignments);
        Assert.Equal(violationsBefore, session.Violations.Select(v => v.Key).ToList());
    }

    [Fact]
    public void Redo_reapplies_undone_change()
    {
        var session = Session(Store());
        var a = new Assignment(1, D(10, 1), Morning);
        session.Execute(new AssignCommand(a));
        session.Undo();

        session.Redo();

        Assert.Contains(a, session.Assignments);
    }

    [Fact]
    public void At_least_20_levels_can_be_undone()
    {
        var session = Session(Store());
        for (var day = 1; day <= 25; day++)
        {
            session.Execute(new AssignCommand(new Assignment(1 + day % 2, D(10, day), Morning)));
        }

        var undone = 0;
        while (session.CanUndo)
        {
            session.Undo();
            undone++;
        }

        Assert.True(undone >= 20);
        Assert.Equal(25 - undone, session.Assignments.Count);
    }

    [Fact]
    public void New_operation_after_undo_clears_redo()
    {
        var session = Session(Store());
        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Morning)));
        session.Undo();

        session.Execute(new AssignCommand(new Assignment(2, D(10, 1), Morning)));

        Assert.False(session.CanRedo);
    }

    [Fact]
    public void Unassign_can_be_undone()
    {
        var store = Store();
        var a = new Assignment(1, D(10, 1), Morning);
        store.Assignments.Add(a);
        var session = Session(store);

        session.Execute(new UnassignCommand(a));
        Assert.DoesNotContain(a, session.Assignments);

        session.Undo();
        Assert.Contains(a, session.Assignments);
        Assert.Contains(a, store.Assignments);
    }

    [Fact]
    public void No_op_command_does_not_enter_history_or_touch_storage()
    {
        var store = Store();
        var a = new Assignment(1, D(10, 1), Morning);
        store.Assignments.Add(a);
        var session = Session(store);

        var changed = session.Execute(new AssignCommand(a));

        Assert.False(changed);
        Assert.False(session.CanUndo);
        Assert.Equal(0, store.ApplyCount);
    }

    [Fact]
    public void Storage_failure_leaves_state_unchanged()
    {
        var store = Store();
        var session = Session(store);
        store.FailNextApply = true;

        Assert.Throws<IOException>(() => session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Morning))));

        Assert.Empty(session.Assignments);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Changed_event_is_raised_on_execute_and_undo()
    {
        var session = Session(Store());
        var raised = 0;
        session.Changed += (_, _) => raised++;

        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Morning)));
        session.Undo();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void Months_are_independent_and_reloaded_from_storage()
    {
        var store = Store();
        var session = Session(store);
        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Morning)));
        session.SetMonth(2026, 11);
        session.Execute(new AssignCommand(new Assignment(2, D(11, 3), Morning)));

        var reopened = new RosterSession(store, 2026, 10);

        Assert.Equal([new Assignment(1, D(10, 1), Morning)], reopened.AssignmentsInMonth());
        reopened.SetMonth(2026, 11);
        Assert.Equal([new Assignment(2, D(11, 3), Morning)], reopened.AssignmentsInMonth());
    }

    [Fact]
    public void Undoing_a_change_from_another_month_switches_to_that_month()
    {
        var session = Session(Store());
        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Morning)));
        session.SetMonth(2026, 11);

        session.Undo();

        Assert.Equal(new DateOnly(2026, 10, 1), session.Month);
    }

    [Fact]
    public void Inactive_employees_are_excluded_from_selection_lists_but_history_is_kept()
    {
        var store = Store();
        store.Assignments.Add(new Assignment(3, D(10, 1), Morning));
        var session = Session(store);

        Assert.DoesNotContain(session.ActiveEmployees, e => e.Id == 3);
        Assert.Contains(session.RosterEmployees(), e => e.Id == 3);
        Assert.Contains(new Assignment(3, D(10, 1), Morning), session.AssignmentsInMonth());
    }

    [Fact]
    public void Reload_reads_storage_again_and_clears_history()
    {
        var store = Store();
        var session = Session(store);
        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Morning)));
        store.Assignments.Clear();
        store.Assignments.Add(new Assignment(2, D(10, 9), Morning));

        session.Reload();

        Assert.Equal([new Assignment(2, D(10, 9), Morning)], session.AssignmentsInMonth());
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Changing_min_rest_setting_recomputes_violations()
    {
        var session = Session(Store());
        session.Execute(new AssignCommand(new Assignment(1, D(10, 1), Closing)));
        session.Execute(new AssignCommand(new Assignment(1, D(10, 2), Morning)));

        session.UpdateSettings(new RuleSettings(4 * 60));

        Assert.DoesNotContain(session.Violations, v => v.Rule == RuleKind.InsufficientRest);
    }
}
