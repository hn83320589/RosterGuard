using System.Text;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Export;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Export;

public class CsvRosterExporterTests
{
    private static readonly ShiftType[] Shifts =
    [
        Shift(1, "早班", "07:00", "15:00"),
        Shift(2, "打烊班", "18:00", "02:00"),
    ];

    private static string[] Lines(byte[] bytes) =>
        Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Output_starts_with_utf8_bom_so_excel_detects_encoding()
    {
        var bytes = CsvRosterExporter.Export(2026, 10, [Employee(1, "王小明")], Shifts, []);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public void Employees_are_rows_and_dates_are_columns()
    {
        var bytes = CsvRosterExporter.Export(2026, 10, [Employee(1, "王小明"), Employee(2, "李小華", Role.Kitchen)], Shifts,
        [
            new Assignment(1, D(10, 1), 1),
            new Assignment(1, D(10, 1), 2),
            new Assignment(2, D(10, 31), 2),
        ]);

        var lines = Lines(bytes);
        var header = lines[0].Split(',');
        Assert.Equal(3, lines.Length);
        Assert.Equal(["員工", "職稱", "類型"], header[..3]);
        Assert.Equal("10/01(四)", header[3]);
        Assert.Equal("10/31(六)", header[33]);
        Assert.Equal("本月工時", header[^1]);
        Assert.Equal(3 + 31 + 1, header.Length);

        var row1 = lines[1].Split(',');
        Assert.Equal("王小明", row1[0]);
        Assert.Equal("外場", row1[1]);
        Assert.Equal("早班/打烊班", row1[3]);
        Assert.Equal("16", row1[^1]);

        var row2 = lines[2].Split(',');
        Assert.Equal("內場", row2[1]);
        Assert.Equal("打烊班", row2[33]);
    }

    [Fact]
    public void Fields_with_commas_or_quotes_are_escaped()
    {
        var bytes = CsvRosterExporter.Export(2026, 2, [Employee(1, "王,\"阿明\"")], Shifts, []);

        var row = Lines(bytes)[1];
        Assert.StartsWith("\"王,\"\"阿明\"\"\",", row);
    }

    [Fact]
    public void Column_count_follows_days_in_month()
    {
        var bytes = CsvRosterExporter.Export(2026, 2, [Employee(1, "王小明")], Shifts, []);

        Assert.Equal(3 + 28 + 1, Lines(bytes)[0].Split(',').Length);
    }

    [Fact]
    public void Half_hours_are_written_with_one_decimal()
    {
        ShiftType[] shifts = [Shift(1, "短班", "07:00", "11:30")];
        var bytes = CsvRosterExporter.Export(2026, 10, [Employee(1, "王小明")], shifts, [new Assignment(1, D(10, 1), 1)]);

        Assert.Equal("4.5", Lines(bytes)[1].Split(',')[^1]);
    }
}
