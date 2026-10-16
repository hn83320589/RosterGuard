using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using RosterGuard.App.ViewModels;
using RosterGuard.Core.Domain;

namespace RosterGuard.App.Views;

public partial class RosterView : UserControl
{
    private RosterViewModel? _subscribed;

    public RosterView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Subscribe(DataContext as RosterViewModel);
    }

    private RosterViewModel? ViewModel => DataContext as RosterViewModel;

    private void Subscribe(RosterViewModel? vm)
    {
        if (_subscribed is not null)
        {
            _subscribed.ScrollRequested -= OnScrollRequested;
        }

        _subscribed = vm;
        if (vm is not null)
        {
            vm.ScrollRequested += OnScrollRequested;
        }
    }

    /// <summary>找到對應格子的控制項並捲動到可見範圍。</summary>
    private void OnScrollRequested(object? sender, object target)
    {
        var control = this.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => ReferenceEquals(b.DataContext, target) && (b.Classes.Contains("cell") || b.Classes.Contains("coverage")));
        control?.BringIntoView();
    }

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        await SaveAsync(vm.CsvFileName, "CSV", "*.csv", vm.ExportCsv());
    }

    private async void ExportIcs_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm || IcsEmployeeBox.SelectedItem is not Employee employee)
        {
            IcsEmployeeBox.Focus();
            return;
        }

        await SaveAsync(vm.IcsFileName(employee), "iCalendar", "*.ics", vm.ExportIcs(employee));
    }

    private async Task SaveAsync(string suggestedName, string typeName, string pattern, byte[] content)
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            return;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = [new FilePickerFileType(typeName) { Patterns = [pattern] }],
        });
        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(content);
            ViewModel?.ReportError(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel?.ReportError($"匯出失敗：{ex.Message}");
        }
    }
}
