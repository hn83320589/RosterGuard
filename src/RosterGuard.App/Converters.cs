using Avalonia.Data.Converters;
using RosterGuard.Core.Domain;

namespace RosterGuard.App;

public static class Converters
{
    /// <summary>把職稱、類型等列舉轉成中文顯示。</summary>
    public static readonly IValueConverter Chinese = new FuncValueConverter<object?, string>(value => value switch
    {
        Role r => r.ToChinese(),
        EmploymentType t => t.ToChinese(),
        null => "",
        _ => value.ToString() ?? "",
    });
}
