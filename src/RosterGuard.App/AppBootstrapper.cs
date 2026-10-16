using Microsoft.Data.Sqlite;
using RosterGuard.App.ViewModels;
using RosterGuard.Core.Seed;
using RosterGuard.Core.Session;
using RosterGuard.Data;

namespace RosterGuard.App;

/// <summary>Composition root：開啟（必要時建立並載入範例）資料庫，建立 Session 與主視窗 ViewModel。</summary>
public static class AppBootstrapper
{
    /// <summary>可用環境變數覆寫資料庫位置（測試、示範用）。</summary>
    public const string DatabasePathVariable = "ROSTERGUARD_DB";

    public static string ResolveDatabasePath(string? databasePath = null) =>
        databasePath
        ?? Environment.GetEnvironmentVariable(DatabasePathVariable)
        ?? SqliteRosterStore.DefaultPath;

    /// <summary>開啟資料庫時可預期、可由使用者處理的失敗（其餘例外視為程式錯誤，照常拋出）。</summary>
    public static bool IsDatabaseOpenFailure(Exception ex) =>
        ex is SqliteException or UnsupportedSchemaException or IOException or UnauthorizedAccessException;

    public static MainWindowViewModel Create(string? databasePath = null, DateOnly? today = null)
    {
        var store = new SqliteRosterStore(ResolveDatabasePath(databasePath));
        if (store.WasCreated)
        {
            SampleData.LoadInto(store);
        }

        var month = today ?? DateOnly.FromDateTime(DateTime.Today);
        var session = new RosterSession(store, month.Year, month.Month);
        return new MainWindowViewModel(session, store);
    }
}
