using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Session;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.History;

public class CopyWeekCommandTests
{
    private const int Morning = 1, Retired = 2;

    private static FakeRosterStore Store()
    {
        var store = new FakeRosterStore();
        store.Employees.Add(Employee(1, "小明"));
        store.Employees.Add(Employee(2, "小華"));
        store.Employees.Add(Employee(3, "離職者", active: false));
        store.ShiftTypes.Add(Shift(Morning, "早班", "07:00", "15:00"));
        store.ShiftTypes.Add(Shift(Retired, "舊班", "09:00", "17:00", active: false));
        return store;
    }

    [Fact]
    public void Copies_source_week_onto_target_week_replacing_existing_assignments()
    {
        var store = Store();
        store.Assignments.Add(new Assignment(1, D(10, 12), Morning)); // 來源週一
        store.Assignments.Add(new Assignment(2, D(10, 14), Morning)); // 來源週三
        store.Assignments.Add(new Assignment(2, D(10, 20), Morning)); // 目標週二（會被覆蓋）
        var session = new RosterSession(store, 2026, 10);

        session.Execute(session.CopyWeek(D(10, 12), D(10, 19)));

        var target = session.AssignmentsInMonth().Where(a => a.Date >= D(10, 19) && a.Date <= D(10, 25)).ToHashSet();
        Assert.Equal([new Assignment(1, D(10, 19), Morning), new Assignment(2, D(10, 21), Morning)], target.OrderBy(a => a.Date));
    }

    [Fact]
    public void Inactive_employees_and_shift_types_are_skipped()
    {
        var store = Store();
        store.Assignments.Add(new Assignment(3, D(10, 12), Morning));
        store.Assignments.Add(new Assignment(1, D(10, 13), Retired));
        store.Assignments.Add(new Assignment(1, D(10, 14), Morning));
        var session = new RosterSession(store, 2026, 10);

        session.Execute(session.CopyWeek(D(10, 12), D(10, 19)));

        Assert.Equal([new Assignment(1, D(10, 21), Morning)],
            session.AssignmentsInMonth().Where(a => a.Date >= D(10, 19)).ToList());
    }

    [Fact]
    public void Only_target_days_inside_the_current_month_are_touched()
    {
        var store = Store();
        store.AssignmentsFromDays(1, Morning, D(10, 19), 7); // 10/19–10/25 整週
        store.Assignments.Add(new Assignment(2, D(11, 1), Morning)); // 目標週中屬於 11 月的日子
        var session = new RosterSession(store, 2026, 10);

        session.Execute(session.CopyWeek(D(10, 19), D(10, 26))); // 目標週 10/26–11/1

        Assert.Contains(new Assignment(1, D(10, 31), Morning), session.Assignments);
        Assert.DoesNotContain(new Assignment(1, D(11, 1), Morning), session.Assignments);
        Assert.Contains(new Assignment(2, D(11, 1), Morning), session.Assignments);
    }

    [Fact]
    public void Copy_week_is_a_single_undo_step()
    {
        var store = Store();
        store.AssignmentsFromDays(1, Morning, D(10, 12), 5);
        store.Assignments.Add(new Assignment(2, D(10, 20), Morning));
        var session = new RosterSession(store, 2026, 10);
        var before = session.Assignments.ToHashSet();

        session.Execute(session.CopyWeek(D(10, 12), D(10, 19)));
        session.Undo();

        Assert.True(before.SetEquals(session.Assignments));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Source_and_target_must_be_mondays()
    {
        var session = new RosterSession(Store(), 2026, 10);

        Assert.Throws<ArgumentException>(() => session.CopyWeek(D(10, 13), D(10, 19)));
    }
}

internal static class FakeRosterStoreExtensions
{
    public static void AssignmentsFromDays(this FakeRosterStore store, int employeeId, int shiftId, DateOnly first, int days)
    {
        for (var i = 0; i < days; i++)
        {
            store.Assignments.Add(new Assignment(employeeId, first.AddDays(i), shiftId));
        }
    }
}
