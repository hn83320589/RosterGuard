using System.Text;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Export;

/// <summary>
/// 單一員工的個人班表 .ics（RFC 5545）。時間以 TZID=Asia/Taipei 的本地時間輸出並附上 VTIMEZONE，
/// UTF-8 無 BOM、CRLF、長行於 75 octets 折行且不切斷多位元組字元。
/// </summary>
public static class IcsExporter
{
    public const string TimeZoneId = "Asia/Taipei";

    public static byte[] Export(Employee employee, IReadOnlyList<ShiftType> shiftTypes,
        IEnumerable<Assignment> assignments, DateTime stampUtc)
    {
        var shifts = shiftTypes.ToDictionary(s => s.Id);
        var mine = assignments
            .Where(a => a.EmployeeId == employee.Id)
            .Distinct()
            .Select(a => (Assignment: a, Shift: shifts[a.ShiftTypeId]))
            .OrderBy(x => ShiftTime.Interval(x.Assignment.Date, x.Shift).Start)
            .ThenBy(x => x.Shift.Id);

        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//RosterGuard//RosterGuard 1.0//ZH-TW",
            "CALSCALE:GREGORIAN",
            "METHOD:PUBLISH",
            $"X-WR-CALNAME:{EscapeText($"{employee.Name} 班表")}",
            $"X-WR-TIMEZONE:{TimeZoneId}",
            "BEGIN:VTIMEZONE",
            $"TZID:{TimeZoneId}",
            "BEGIN:STANDARD",
            "DTSTART:19700101T000000",
            "TZOFFSETFROM:+0800",
            "TZOFFSETTO:+0800",
            "TZNAME:CST",
            "END:STANDARD",
            "END:VTIMEZONE",
        };

        foreach (var (assignment, shift) in mine)
        {
            var interval = ShiftTime.Interval(assignment.Date, shift);
            lines.AddRange(
            [
                "BEGIN:VEVENT",
                $"UID:{employee.Id}-{assignment.Date:yyyyMMdd}-{shift.Id}@rosterguard.local",
                $"DTSTAMP:{stampUtc.ToUniversalTime():yyyyMMdd'T'HHmmss'Z'}",
                $"DTSTART;TZID={TimeZoneId}:{interval.Start:yyyyMMdd'T'HHmmss}",
                $"DTEND;TZID={TimeZoneId}:{interval.End:yyyyMMdd'T'HHmmss}",
                $"SUMMARY:{EscapeText(shift.Name)}",
                $"DESCRIPTION:{EscapeText($"{employee.Name}（{employee.Role.ToChinese()}）{shift.Start:HH\\:mm}–{shift.End:HH\\:mm}")}",
                "END:VEVENT",
            ]);
        }

        lines.Add("END:VCALENDAR");

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            Fold(sb, line);
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(sb.ToString());
    }

    internal static string EscapeText(string text) => text
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");

    /// <summary>每行最多 75 octets；續行以一個空白開頭（空白也計入 75）。</summary>
    private static void Fold(StringBuilder sb, string line)
    {
        const int maxOctets = 75;
        var used = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (used + size > maxOctets)
            {
                sb.Append("\r\n ");
                used = 1;
            }
            sb.Append(rune.ToString());
            used += size;
        }
        sb.Append("\r\n");
    }
}
