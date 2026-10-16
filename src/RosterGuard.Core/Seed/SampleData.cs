using RosterGuard.Core.Domain;
using RosterGuard.Core.History;
using RosterGuard.Core.Session;
using RosterGuard.Core.Time;

namespace RosterGuard.Core.Seed;

/// <summary>
/// 範例資料：9 位員工、3 個班別，以及 2026 年 9、10 月的班表。
/// 基準輪班（<see cref="BaselineAssignments"/>）沒有任何違規；<see cref="Injections"/> 刻意加入各類違規，
/// 預期結果見 samples/expected-violations.md，並由 SampleDataTests 自動驗證。
/// </summary>
public static class SampleData
{
    public const int Morning = 1, Midday = 2, Closing = 3;

    public const int ChenZhiming = 1, LinJianhong = 2, HuangMeiling = 3;                             // 內場
    public const int WangXiaofang = 4, ZhangJiahao = 5, LiYating = 6, WuJunjie = 7, LiuYijun = 8, CaiZonghan = 9; // 外場

    public static DateOnly FirstDay { get; } = new(2026, 9, 1);
    public static DateOnly LastDay { get; } = new(2026, 10, 31);

    private const int FullTimeLimit = 48 * 60;
    private const int PartTimeLimit = 32 * 60;

    public static IReadOnlyList<Employee> Employees { get; } =
    [
        new(ChenZhiming, "陳志明", Role.Kitchen, EmploymentType.FullTime, FullTimeLimit, Weekdays.None),
        new(LinJianhong, "林建宏", Role.Kitchen, EmploymentType.FullTime, FullTimeLimit, Weekdays.None),
        new(HuangMeiling, "黃美玲", Role.Kitchen, EmploymentType.PartTime, PartTimeLimit, Weekdays.Thursday),
        new(WangXiaofang, "王小芳", Role.Floor, EmploymentType.FullTime, FullTimeLimit, Weekdays.Saturday | Weekdays.Sunday),
        new(ZhangJiahao, "張家豪", Role.Floor, EmploymentType.FullTime, FullTimeLimit, Weekdays.None),
        new(LiYating, "李雅婷", Role.Floor, EmploymentType.PartTime, PartTimeLimit, Weekdays.Wednesday),
        new(WuJunjie, "吳俊傑", Role.Floor, EmploymentType.FullTime, FullTimeLimit, Weekdays.None),
        new(LiuYijun, "劉怡君", Role.Floor, EmploymentType.PartTime, PartTimeLimit, Weekdays.Friday),
        new(CaiZonghan, "蔡宗翰", Role.Floor, EmploymentType.FullTime, FullTimeLimit, Weekdays.None),
    ];

    public static IReadOnlyList<ShiftType> ShiftTypes { get; } =
    [
        new(Morning, "早班", new TimeOnly(7, 0), new TimeOnly(15, 0), RequiredKitchen: 1, RequiredFloor: 2),
        new(Midday, "午班", new TimeOnly(11, 0), new TimeOnly(19, 0), RequiredKitchen: 0, RequiredFloor: 1),
        new(Closing, "打烊班", new TimeOnly(18, 0), new TimeOnly(2, 0), RequiredKitchen: 1, RequiredFloor: 1),
    ];

    /// <summary>每週固定輪班：(員工, 班別, 星期)。</summary>
    private static readonly (int Employee, int Shift, DayOfWeek[] Days)[] WeeklyPattern =
    [
        (ChenZhiming, Morning, Days(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday)),
        (LinJianhong, Closing, Days(DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday)),
        (HuangMeiling, Closing, Days(DayOfWeek.Monday, DayOfWeek.Tuesday)),
        (HuangMeiling, Morning, Days(DayOfWeek.Saturday, DayOfWeek.Sunday)),
        (WangXiaofang, Morning, Days(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday)),
        (ZhangJiahao, Morning, Days(DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday)),
        (LiYating, Morning, Days(DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday)),
        (WuJunjie, Midday, Days(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday)),
        (LiuYijun, Closing, Days(DayOfWeek.Monday, DayOfWeek.Tuesday)),
        (LiuYijun, Midday, Days(DayOfWeek.Saturday, DayOfWeek.Sunday)),
        (CaiZonghan, Closing, Days(DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday)),
    ];

    /// <summary>刻意製造違規的修改，編號對應 samples/expected-violations.md。</summary>
    public static IReadOnlyList<(string Id, RosterDelta Delta)> Injections { get; } =
    [
        ("A", Swap(new(CaiZonghan, D(9, 4), Closing), new(WuJunjie, D(9, 4), Closing))),
        ("B", Swap(new(WangXiaofang, D(9, 14), Morning), new(CaiZonghan, D(9, 14), Morning))),
        ("C", new RosterDelta([], [new(WuJunjie, D(9, 24), Midday)])),
        ("D", Swap(new(LiYating, D(9, 26), Morning), new(WangXiaofang, D(9, 26), Morning))),
        ("E", new RosterDelta(
            [new(ZhangJiahao, D(10, 5), Morning), new(ZhangJiahao, D(10, 6), Morning), new(LiYating, D(10, 8), Morning)],
            [new(LiYating, D(10, 5), Morning), new(LiYating, D(10, 6), Morning), new(ZhangJiahao, D(10, 8), Morning)])),
        ("F", Swap(new(WuJunjie, D(10, 16), Midday), new(LiYating, D(10, 16), Midday))),
        ("G", new RosterDelta([new(ChenZhiming, D(10, 24), Midday)], [])),
    ];

    public static IReadOnlyList<Assignment> BaselineAssignments()
    {
        var result = new List<Assignment>();
        for (var date = FirstDay; date <= LastDay; date = date.AddDays(1))
        {
            foreach (var (employee, shift, days) in WeeklyPattern)
            {
                if (days.Contains(date.DayOfWeek))
                {
                    result.Add(new Assignment(employee, date, shift));
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<Assignment> Assignments()
    {
        var set = BaselineAssignments().ToHashSet();
        foreach (var (id, delta) in Injections)
        {
            foreach (var removed in delta.Removed)
            {
                if (!set.Remove(removed))
                {
                    throw new InvalidOperationException($"範例修改 {id} 要移除的指派不存在：{removed}");
                }
            }
            set.UnionWith(delta.Added);
        }
        return set.OrderBy(a => a.Date).ThenBy(a => a.EmployeeId).ThenBy(a => a.ShiftTypeId).ToList();
    }

    /// <summary>寫入範例資料到（空的）儲存區。</summary>
    public static void LoadInto(IRosterStore store)
    {
        foreach (var shift in ShiftTypes)
        {
            store.SaveShiftType(shift);
        }

        foreach (var employee in Employees)
        {
            store.SaveEmployee(employee);
        }

        store.SaveSettings(new RuleSettings());
        store.ApplyChanges(new RosterDelta(Assignments(), []));
    }

    private static RosterDelta Swap(Assignment remove, Assignment add) => new([add], [remove]);

    private static DateOnly D(int month, int day) => new(2026, month, day);

    private static DayOfWeek[] Days(params DayOfWeek[] days) => days;
}
