using System.Globalization;
using System.Text;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Export;

/// <summary>月班表 CSV：員工為列、日期為欄；UTF-8 with BOM 讓 Excel 直接開啟不亂碼。</summary>
public static class CsvRosterExporter
{
    public static byte[] Export(int year, int month, IReadOnlyList<Employee> employees,
        IReadOnlyList<ShiftType> shiftTypes, IEnumerable<Assignment> assignments)
    {
        var days = WeekMath.DaysOfMonth(year, month).ToList();
        var shifts = shiftTypes.ToDictionary(s => s.Id);
        var byCell = assignments
            .Where(a => a.Date.Year == year && a.Date.Month == month)
            .GroupBy(a => (a.EmployeeId, a.Date))
            .ToDictionary(g => g.Key, g => g.Select(a => shifts[a.ShiftTypeId]).OrderBy(s => s.Start).ThenBy(s => s.Id).ToList());

        var sb = new StringBuilder();
        WriteRow(sb, ["員工", "職稱", "類型", .. days.Select(d => $"{d:MM/dd}({d.DayOfWeek.ToChinese()})"), "本月工時"]);

        foreach (var employee in employees)
        {
            var cells = days.Select(d => byCell.GetValueOrDefault((employee.Id, d)) ?? []).ToList();
            var minutes = cells.SelectMany(c => c).Sum(s => (int)ShiftTime.Duration(s).TotalMinutes);
            WriteRow(sb,
            [
                employee.Name,
                employee.Role.ToChinese(),
                employee.Type.ToChinese(),
                .. cells.Select(c => string.Join("/", c.Select(s => s.Name))),
                (minutes / 60m).ToString("0.#", CultureInfo.InvariantCulture),
            ]);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static void WriteRow(StringBuilder sb, IEnumerable<string> fields)
    {
        sb.AppendJoin(',', fields.Select(Escape));
        sb.Append("\r\n");
    }

    private static string Escape(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
}
