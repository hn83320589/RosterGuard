using System.Text;
using Ical.Net;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Export;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Export;

public class IcsExporterTests
{
    private static readonly DateTime Stamp = new(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ShiftType[] Shifts =
    [
        Shift(1, "早班", "07:00", "15:00"),
        Shift(2, "打烊班", "18:00", "02:00"),
    ];

    private static byte[] Export(Employee employee, params Assignment[] assignments) =>
        IcsExporter.Export(employee, Shifts, assignments, Stamp);

    [Fact]
    public void Standard_parser_reads_events_with_chinese_titles_and_correct_times()
    {
        var bytes = Export(Employee(1, "王小明"),
            new Assignment(1, D(10, 2), 1),
            new Assignment(1, D(10, 1), 2),
            new Assignment(2, D(10, 1), 1)); // 其他員工的指派不應匯出

        var calendar = Calendar.Load(Encoding.UTF8.GetString(bytes))!;
        var events = calendar.Events.OrderBy(e => e.DtStart!.Value).ToList();

        Assert.Equal(2, events.Count);
        Assert.Equal("打烊班", events[0].Summary);
        Assert.Equal(new DateTime(2026, 10, 1, 18, 0, 0), events[0].DtStart!.Value);
        Assert.Equal(new DateTime(2026, 10, 2, 2, 0, 0), events[0].DtEnd!.Value);
        Assert.Equal("Asia/Taipei", events[0].DtStart!.TzId);
        Assert.Equal("早班", events[1].Summary);
        Assert.Equal(new DateTime(2026, 10, 2, 7, 0, 0), events[1].DtStart!.Value);
    }

    [Fact]
    public void Output_is_utf8_without_bom_with_crlf_line_endings()
    {
        var bytes = Export(Employee(1, "王小明"), new Assignment(1, D(10, 1), 1));
        var text = Encoding.UTF8.GetString(bytes);

        Assert.NotEqual(0xEF, bytes[0]);
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", text);
        Assert.EndsWith("END:VCALENDAR\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
    }

    [Fact]
    public void Long_lines_are_folded_at_75_octets_without_splitting_characters()
    {
        ShiftType[] shifts = [Shift(1, string.Concat(Enumerable.Repeat("超長的班別名稱", 8)), "07:00", "15:00")];
        var bytes = IcsExporter.Export(Employee(1, "王小明"), shifts, [new Assignment(1, D(10, 1), 1)], Stamp);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.All(text.Split("\r\n"), line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.DoesNotContain('�', text);
        var evt = Assert.Single(Calendar.Load(text)!.Events);
        Assert.Equal(shifts[0].Name, evt.Summary);
    }

    [Fact]
    public void Special_characters_in_text_are_escaped()
    {
        ShiftType[] shifts = [Shift(1, "早班,外場;支援", "07:00", "15:00")];
        var bytes = IcsExporter.Export(Employee(1, "王小明"), shifts, [new Assignment(1, D(10, 1), 1)], Stamp);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains(@"SUMMARY:早班\,外場\;支援", text);
        Assert.Equal("早班,外場;支援", Assert.Single(Calendar.Load(text)!.Events).Summary);
    }

    [Fact]
    public void Event_uids_are_unique_and_reproducible()
    {
        var a = Encoding.UTF8.GetString(Export(Employee(1, "王小明"), new Assignment(1, D(10, 1), 1), new Assignment(1, D(10, 1), 2)));
        var b = Encoding.UTF8.GetString(Export(Employee(1, "王小明"), new Assignment(1, D(10, 1), 2), new Assignment(1, D(10, 1), 1)));

        var uids = Calendar.Load(a)!.Events.Select(e => e.Uid).ToList();
        Assert.Equal(2, uids.Distinct().Count());
        Assert.Equal(a, b);
    }
}
