using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using RosterGuard.App.ViewModels;
using RosterGuard.App.Views;
using RosterGuard.Data;

namespace RosterGuard.App.Tests;

public sealed class StartupErrorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RosterGuardUiTests", Guid.NewGuid().ToString("N"));
    private string DbPath => Path.Combine(_dir, "roster.db");

    public StartupErrorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static MainWindowViewModel Open(string path) => AppBootstrapper.Create(path, new DateOnly(2026, 10, 1));

    [Theory]
    [InlineData(26, "損毀")]  // SQLITE_NOTADB
    [InlineData(11, "損毀")]  // SQLITE_CORRUPT
    [InlineData(5, "另一個 RosterGuard")] // SQLITE_BUSY
    [InlineData(14, "無法存取")] // SQLITE_CANTOPEN
    public void Sqlite_errors_are_explained_in_plain_language(int errorCode, string expected)
    {
        var text = StartupErrorViewModel.Describe(new SqliteException("detail", errorCode));

        Assert.Contains(expected, text);
        Assert.Contains("detail", text);
    }

    [Fact]
    public void Unsupported_version_is_explained()
    {
        Assert.Contains("版本不符", StartupErrorViewModel.Describe(new UnsupportedSchemaException("v99")));
    }

    public static TheoryData<Exception, bool> RebuildCases => new()
    {
        { new SqliteException("not a database", 26), true },
        { new SqliteException("malformed", 11), true },
        { new UnsupportedSchemaException("v99"), true },
        { new SqliteException("busy", 5), false },
        { new SqliteException("locked", 6), false },
        { new SqliteException("readonly", 8), false },
        { new SqliteException("cantopen", 14), false },
        { new UnauthorizedAccessException("denied"), false },
        { new IOException("in use"), false },
    };

    [Theory]
    [MemberData(nameof(RebuildCases))]
    public void Rebuild_is_offered_only_when_the_file_itself_is_unusable(Exception error, bool expected)
    {
        var vm = new StartupErrorViewModel(DbPath, error, Open);

        Assert.Equal(expected, vm.CanRebuild);
        Assert.Equal(expected, vm.BackupAndRebuildCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void Locked_database_hides_the_rebuild_button()
    {
        var window = new StartupErrorWindow { DataContext = new StartupErrorViewModel(DbPath, new SqliteException("busy", 5), Open) };
        window.Show();
        MainWindowFixture.Pump();

        Assert.False(Find<Button>(window, "BackupAndRebuildButton").IsVisible);
        Assert.True(Find<Button>(window, "RetryButton").IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void Corrupted_database_shows_error_window_and_backup_and_rebuild_opens_sample_data()
    {
        File.WriteAllText(DbPath, "這不是 SQLite 檔案，這不是 SQLite 檔案，這不是 SQLite 檔案");
        var error = Record.Exception(() => Open(DbPath));
        Assert.True(AppBootstrapper.IsDatabaseOpenFailure(error));

        var vm = new StartupErrorViewModel(DbPath, error, Open);
        MainWindowViewModel? opened = null;
        vm.Opened += (_, main) => opened = main;
        var window = new StartupErrorWindow { DataContext = vm };
        window.Show();
        MainWindowFixture.Pump();

        Assert.Contains("損毀", Find<TextBlock>(window, "MessageText").Text);
        Assert.Contains(DbPath, Find<SelectableTextBlock>(window, "PathText").Text);

        Find<Button>(window, "BackupAndRebuildButton").Command!.Execute(null);
        MainWindowFixture.Pump();

        Assert.NotNull(opened);
        Assert.Equal(9, opened.Employees.Items.Count);
        var backup = Assert.Single(Directory.GetFiles(_dir, "roster.db.broken-*"));
        Assert.StartsWith("這不是 SQLite 檔案", File.ReadAllText(backup));
        window.Close();
    }

    [AvaloniaFact]
    public void Retry_that_fails_again_keeps_the_window_and_the_file()
    {
        File.WriteAllText(DbPath, "這不是 SQLite 檔案，這不是 SQLite 檔案，這不是 SQLite 檔案");
        var vm = new StartupErrorViewModel(DbPath, Record.Exception(() => Open(DbPath)), Open);
        var opened = false;
        vm.Opened += (_, _) => opened = true;

        vm.RetryCommand.Execute(null);

        Assert.False(opened);
        Assert.Contains("損毀", vm.Message);
        Assert.Empty(Directory.GetFiles(_dir, "roster.db.broken-*"));
    }

    private static T Find<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
}
