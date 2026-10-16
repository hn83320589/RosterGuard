using CommunityToolkit.Mvvm.ComponentModel;
using RosterGuard.Core.Seed;
using RosterGuard.Core.Session;
using RosterGuard.Data;

namespace RosterGuard.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(RosterSession session, SqliteRosterStore? store = null)
    {
        Session = session;
        Roster = new RosterViewModel(session, ReportError);
        Employees = new EmployeesViewModel(session, ReportError);
        ShiftTypes = new ShiftTypesViewModel(session, ReportError);
        Settings = new SettingsViewModel(session, ReportError, store is null ? null : () => ResetToSample(store));
        session.Changed += (_, _) => StatusText = "已自動儲存";
    }

    public RosterSession Session { get; }
    public RosterViewModel Roster { get; }
    public EmployeesViewModel Employees { get; }
    public ShiftTypesViewModel ShiftTypes { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _statusText = "就緒";

    private void ReportError(string? message) => ErrorMessage = message;

    private void ResetToSample(SqliteRosterStore store)
    {
        try
        {
            store.Reset();
            SampleData.LoadInto(store);
            Session.Reload();
            ReportError(null);
            StatusText = "已重設為範例資料";
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            ReportError($"重設失敗：{ex.Message}");
        }
    }
}
