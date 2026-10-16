using Microsoft.Data.Sqlite;
using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Session;
using RosterGuard.Data;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Data;

public sealed class SqliteRosterStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RosterGuardTests", Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "roster.db");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Creates_database_and_directory_on_first_open()
    {
        var store = new SqliteRosterStore(DbPath);

        Assert.True(store.WasCreated);
        Assert.True(File.Exists(DbPath));
        Assert.False(new SqliteRosterStore(DbPath).WasCreated);
    }

    [Fact]
    public void Employees_round_trip_and_new_ones_get_an_id()
    {
        var store = new SqliteRosterStore(DbPath);
        var saved = store.SaveEmployee(new Employee(0, "王小明", Role.Kitchen, EmploymentType.PartTime, 20 * 60 + 30,
            Weekdays.Saturday | Weekdays.Sunday, IsActive: false));

        var loaded = Assert.Single(new SqliteRosterStore(DbPath).LoadEmployees());

        Assert.NotEqual(0, saved.Id);
        Assert.Equal(saved, loaded);
    }

    [Fact]
    public void Editing_an_employee_updates_the_existing_row()
    {
        var store = new SqliteRosterStore(DbPath);
        var saved = store.SaveEmployee(Employee(0, "王小明"));

        store.SaveEmployee(saved with { Name = "王大明", IsActive = false });

        var loaded = Assert.Single(store.LoadEmployees());
        Assert.Equal("王大明", loaded.Name);
        Assert.False(loaded.IsActive);
    }

    [Fact]
    public void Shift_types_round_trip_including_overnight_times_and_requirements()
    {
        var store = new SqliteRosterStore(DbPath);
        var saved = store.SaveShiftType(new ShiftType(0, "打烊班", new TimeOnly(18, 0), new TimeOnly(2, 0), 1, 2));

        Assert.Equal(saved, Assert.Single(new SqliteRosterStore(DbPath).LoadShiftTypes()));
    }

    [Fact]
    public void Assignment_changes_persist_across_reopen()
    {
        var store = Seeded();
        store.ApplyChanges(new RosterDelta([new(1, D(10, 1), 1), new(1, D(10, 2), 1)], []));
        store.ApplyChanges(new RosterDelta([], [new(1, D(10, 1), 1)]));

        var loaded = new SqliteRosterStore(DbPath).LoadAssignments();

        Assert.Equal([new Assignment(1, D(10, 2), 1)], loaded);
    }

    [Fact]
    public void Failed_change_set_is_rolled_back_entirely()
    {
        var store = Seeded();

        // 第二筆參照不存在的員工 → FK 失敗，第一筆也不得寫入
        Assert.Throws<SqliteException>(() =>
            store.ApplyChanges(new RosterDelta([new(1, D(10, 1), 1), new(99, D(10, 1), 1)], [])));

        Assert.Empty(store.LoadAssignments());
    }

    [Fact]
    public void Settings_default_to_11_hours_and_persist_changes()
    {
        var store = new SqliteRosterStore(DbPath);
        Assert.Equal(11 * 60, store.LoadSettings().MinRestMinutes);

        store.SaveSettings(new RuleSettings(10 * 60));

        Assert.Equal(10 * 60, new SqliteRosterStore(DbPath).LoadSettings().MinRestMinutes);
    }

    [Fact]
    public void Session_months_survive_restart_independently()
    {
        var session = new RosterSession(Seeded(), 2026, 10);
        session.Execute(new AssignCommand(new Assignment(1, D(10, 31), 1)));
        session.SetMonth(2026, 11);
        session.Execute(new AssignCommand(new Assignment(1, D(11, 2), 1)));

        var reopened = new RosterSession(new SqliteRosterStore(DbPath), 2026, 10);

        Assert.Equal([new Assignment(1, D(10, 31), 1)], reopened.AssignmentsInMonth());
        reopened.SetMonth(2026, 11);
        Assert.Equal([new Assignment(1, D(11, 2), 1)], reopened.AssignmentsInMonth());
    }

    [Fact]
    public void Deactivated_employee_keeps_historical_assignments()
    {
        var store = Seeded();
        store.ApplyChanges(new RosterDelta([new(1, D(10, 1), 1)], []));
        var employee = store.LoadEmployees().Single();

        store.SaveEmployee(employee with { IsActive = false });

        Assert.Single(new SqliteRosterStore(DbPath).LoadAssignments());
    }

    [Fact]
    public void Reset_clears_all_data()
    {
        var store = Seeded();
        store.ApplyChanges(new RosterDelta([new(1, D(10, 1), 1)], []));

        store.Reset();

        Assert.Empty(store.LoadEmployees());
        Assert.Empty(store.LoadShiftTypes());
        Assert.Empty(store.LoadAssignments());
    }

    [Fact]
    public void Default_path_is_under_local_app_data()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RosterGuard");

        Assert.Equal(expected, Path.GetDirectoryName(SqliteRosterStore.DefaultPath));
    }

    private SqliteRosterStore Seeded()
    {
        var store = new SqliteRosterStore(DbPath);
        store.SaveEmployee(Employee(1, "王小明"));
        store.SaveShiftType(Shift(1, "早班", "07:00", "15:00"));
        return store;
    }
}
