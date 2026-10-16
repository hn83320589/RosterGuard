using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using RosterGuard.Data;

namespace RosterGuard.App.ViewModels;

/// <summary>啟動時資料庫打不開：說明原因，並讓使用者重試、備份舊檔後重建，或結束程式。</summary>
public sealed partial class StartupErrorViewModel(
    string databasePath, Exception error, Func<string, MainWindowViewModel> open) : ObservableObject
{
    private const int SqliteBusy = 5, SqliteLocked = 6, SqliteCorrupt = 11, SqliteCantOpen = 14, SqliteNotADatabase = 26;

    public string DatabasePath { get; } = databasePath;

    [ObservableProperty] private string _message = Describe(error);

    /// <summary>成功開啟資料庫，參數為主視窗的 ViewModel。</summary>
    public event EventHandler<MainWindowViewModel>? Opened;

    public event EventHandler? ExitRequested;

    public static string Describe(Exception ex)
    {
        var reason = ex switch
        {
            SqliteException { SqliteErrorCode: SqliteNotADatabase or SqliteCorrupt } =>
                "資料庫檔案已損毀，或不是 RosterGuard 的資料庫。可以把舊檔改名保留，再重建一個新的（會載入範例資料）。",
            SqliteException { SqliteErrorCode: SqliteBusy or SqliteLocked } =>
                "資料庫正被其他程式使用，可能已經開著另一個 RosterGuard。請關閉後按「重試」。",
            UnsupportedSchemaException =>
                "資料庫版本不符，可能是由較新版的 RosterGuard 建立。請改用較新版本開啟；若要改用新的資料庫，可把舊檔改名保留後重建。",
            SqliteException or IOException or UnauthorizedAccessException =>
                "無法存取資料庫檔案，請確認檔案沒有被設為唯讀、資料夾有寫入權限。",
            _ => "開啟資料庫時發生未預期的錯誤。",
        };
        return $"{reason}\n\n技術細節：{ex.Message}";
    }

    [RelayCommand]
    private void Retry() => TryOpen(() => open(DatabasePath));

    [RelayCommand]
    private void BackupAndRebuild() => TryOpen(() =>
    {
        SqliteRosterStore.MoveAside(DatabasePath, DateTime.Now);
        return open(DatabasePath);
    });

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void TryOpen(Func<MainWindowViewModel> attempt)
    {
        try
        {
            Opened?.Invoke(this, attempt());
        }
        catch (Exception ex) when (AppBootstrapper.IsDatabaseOpenFailure(ex))
        {
            Message = Describe(ex);
        }
    }
}
