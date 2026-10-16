using System.Globalization;
using Microsoft.Data.Sqlite;
using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Session;

namespace RosterGuard.Data;

/// <summary>以本機 SQLite 檔案實作 <see cref="IRosterStore"/>。每次呼叫都立即寫入。</summary>
public sealed class SqliteRosterStore : IRosterStore
{
    private const int SchemaVersion = 1;
    private const string DateFormat = "yyyy-MM-dd";
    private const string MinRestKey = "min_rest_minutes";

    private readonly string _connectionString;

    public SqliteRosterStore(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 關閉連線池，避免 Windows 上檔案在連線關閉後仍被鎖住
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false,
            ForeignKeys = true,
        }.ToString();

        WasCreated = EnsureSchema();
    }

    /// <summary>%LocalAppData%\RosterGuard\rosterguard.db</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RosterGuard", "rosterguard.db");

    /// <summary>本次開啟時才建立資料庫（用於首次啟動載入範例資料）。</summary>
    public bool WasCreated { get; }

    public IReadOnlyList<Employee> LoadEmployees() => Query(
        "SELECT id, name, role, type, weekly_limit_minutes, unavailable_days, is_active FROM employees ORDER BY id",
        r => new Employee(
            r.GetInt32(0), r.GetString(1), (Role)r.GetInt32(2), (EmploymentType)r.GetInt32(3),
            r.GetInt32(4), (Weekdays)r.GetInt32(5), r.GetBoolean(6)));

    public IReadOnlyList<ShiftType> LoadShiftTypes() => Query(
        "SELECT id, name, start_minute, end_minute, required_kitchen, required_floor, is_active FROM shift_types ORDER BY id",
        r => new ShiftType(
            r.GetInt32(0), r.GetString(1), FromMinutes(r.GetInt32(2)), FromMinutes(r.GetInt32(3)),
            r.GetInt32(4), r.GetInt32(5), r.GetBoolean(6)));

    public IReadOnlyList<Assignment> LoadAssignments() => Query(
        "SELECT employee_id, date, shift_type_id FROM assignments ORDER BY date, employee_id, shift_type_id",
        r => new Assignment(r.GetInt32(0), DateOnly.ParseExact(r.GetString(1), DateFormat, CultureInfo.InvariantCulture), r.GetInt32(2)));

    public RuleSettings LoadSettings()
    {
        var value = Query("SELECT value FROM settings WHERE key = $key", r => r.GetString(0), ("$key", MinRestKey)).SingleOrDefault();
        return value is null ? new RuleSettings() : new RuleSettings(int.Parse(value, CultureInfo.InvariantCulture));
    }

    public void ApplyChanges(RosterDelta delta)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();

        foreach (var a in delta.Removed)
        {
            Execute(connection, tx, "DELETE FROM assignments WHERE employee_id = $e AND date = $d AND shift_type_id = $s", Params(a));
        }

        foreach (var a in delta.Added)
        {
            Execute(connection, tx, "INSERT OR IGNORE INTO assignments (employee_id, date, shift_type_id) VALUES ($e, $d, $s)", Params(a));
        }

        tx.Commit();
    }

    public Employee SaveEmployee(Employee employee)
    {
        using var connection = Open();
        var id = Upsert(connection, "employees", employee.Id,
            ["name", "role", "type", "weekly_limit_minutes", "unavailable_days", "is_active"],
            [employee.Name, (int)employee.Role, (int)employee.Type, employee.WeeklyLimitMinutes, (int)employee.UnavailableDays, employee.IsActive]);
        return employee with { Id = id };
    }

    public ShiftType SaveShiftType(ShiftType shiftType)
    {
        using var connection = Open();
        var id = Upsert(connection, "shift_types", shiftType.Id,
            ["name", "start_minute", "end_minute", "required_kitchen", "required_floor", "is_active"],
            [shiftType.Name, ToMinutes(shiftType.Start), ToMinutes(shiftType.End), shiftType.RequiredKitchen, shiftType.RequiredFloor, shiftType.IsActive]);
        return shiftType with { Id = id };
    }

    public void SaveSettings(RuleSettings settings)
    {
        using var connection = Open();
        Execute(connection, null,
            "INSERT INTO settings (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$key", MinRestKey), ("$value", settings.MinRestMinutes.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>刪除所有資料（用於「重設為範例資料」）。</summary>
    public void Reset()
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        foreach (var table in new[] { "assignments", "employees", "shift_types", "settings" })
        {
            Execute(connection, tx, $"DELETE FROM {table}");
        }
        tx.Commit();
    }

    private bool EnsureSchema()
    {
        using var connection = Open();
        var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version == SchemaVersion)
        {
            return false;
        }

        if (version != 0)
        {
            throw new InvalidOperationException($"不支援的資料庫版本 {version}（預期 {SchemaVersion}）：{connection.DataSource}");
        }

        using var tx = connection.BeginTransaction();
        Execute(connection, tx, """
            CREATE TABLE employees (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                role INTEGER NOT NULL,
                type INTEGER NOT NULL,
                weekly_limit_minutes INTEGER NOT NULL,
                unavailable_days INTEGER NOT NULL,   -- bitmask: Mon = 1 ... Sun = 64
                is_active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE shift_types (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                start_minute INTEGER NOT NULL,       -- minutes since 00:00
                end_minute INTEGER NOT NULL,         -- end <= start means next day
                required_kitchen INTEGER NOT NULL,
                required_floor INTEGER NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE assignments (
                employee_id INTEGER NOT NULL REFERENCES employees(id),
                date TEXT NOT NULL,                  -- shift start date, yyyy-MM-dd
                shift_type_id INTEGER NOT NULL REFERENCES shift_types(id),
                PRIMARY KEY (employee_id, date, shift_type_id)
            );
            CREATE INDEX ix_assignments_date ON assignments(date);
            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        Execute(connection, tx, $"PRAGMA user_version = {SchemaVersion}");
        tx.Commit();
        return true;
    }

    private static int Upsert(SqliteConnection connection, string table, int id, string[] columns, object[] values)
    {
        var parameters = columns.Select((c, i) => ($"${c}", values[i])).ToList();
        var columnList = string.Join(", ", columns);
        var valueList = string.Join(", ", parameters.Select(p => p.Item1));

        if (id == 0)
        {
            Execute(connection, null, $"INSERT INTO {table} ({columnList}) VALUES ({valueList})", [.. parameters]);
            return Convert.ToInt32(Scalar(connection, "SELECT last_insert_rowid()"), CultureInfo.InvariantCulture);
        }

        var updates = string.Join(", ", columns.Select(c => $"{c} = excluded.{c}"));
        Execute(connection, null,
            $"INSERT INTO {table} (id, {columnList}) VALUES ($id, {valueList}) ON CONFLICT(id) DO UPDATE SET {updates}",
            [("$id", id), .. parameters]);
        return id;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string Name, object Value)[] parameters)
    {
        using var connection = Open();
        using var command = Command(connection, null, sql, parameters);
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            results.Add(map(reader));
        }
        return results;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? tx, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = Command(connection, tx, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = Command(connection, null, sql, []);
        return command.ExecuteScalar();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? tx, string sql, (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = tx;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return command;
    }

    private static (string, object)[] Params(Assignment a) =>
        [("$e", a.EmployeeId), ("$d", a.Date.ToString(DateFormat, CultureInfo.InvariantCulture)), ("$s", a.ShiftTypeId)];

    private static int ToMinutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    private static TimeOnly FromMinutes(int minutes) => new(minutes / 60, minutes % 60);
}
