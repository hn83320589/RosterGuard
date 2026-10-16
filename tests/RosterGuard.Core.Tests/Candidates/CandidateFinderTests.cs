using RosterGuard.Core.Candidates;
using RosterGuard.Core.Domain;
using RosterGuard.Core.Session;
using RosterGuard.Core.Tests.History;
using static RosterGuard.Core.Tests.TestData;

namespace RosterGuard.Core.Tests.Candidates;

public class CandidateFinderTests
{
    private const int Morning = 1, Closing = 2;
    private static readonly StaffingSlot Slot = new(D(10, 15), Morning, Role.Floor); // 10/15 週四

    private static FakeRosterStore Store(params Employee[] employees)
    {
        var store = new FakeRosterStore();
        store.Employees.AddRange(employees);
        store.ShiftTypes.Add(Shift(Morning, "早班", "07:00", "15:00", floor: 2));
        store.ShiftTypes.Add(Shift(Closing, "打烊班", "18:00", "02:00"));
        return store;
    }

    private static CandidateResult Find(FakeRosterStore store, StaffingSlot? slot = null) =>
        new RosterSession(store, 2026, 10).FindCandidates(slot ?? Slot);

    [Fact]
    public void Lists_only_employees_whose_assignment_creates_no_new_violation()
    {
        var store = Store(Employee(1, "甲"), Employee(2, "乙"));
        store.Assignments.Add(new Assignment(1, D(10, 14), Closing)); // 甲前一晚打烊 → 間隔不足

        var result = Find(store);

        Assert.Equal([2], result.Candidates.Select(c => c.Employee.Id));
    }

    [Fact]
    public void Excludes_other_roles_inactive_and_already_assigned_employees()
    {
        var store = Store(
            Employee(1, "內場", Role.Kitchen),
            Employee(2, "停用", active: false),
            Employee(3, "已排"),
            Employee(4, "可用"));
        store.Assignments.Add(new Assignment(3, Slot.Date, Morning));

        var result = Find(store);

        Assert.Equal([4], result.Candidates.Select(c => c.Employee.Id));
    }

    [Fact]
    public void Sorted_by_month_hours_ascending_then_by_name()
    {
        var store = Store(Employee(1, "丙"), Employee(2, "乙"), Employee(3, "甲"), Employee(4, "丁"));
        store.Assignments.Add(new Assignment(4, D(10, 1), Morning)); // 丁本月 8 小時
        store.Assignments.Add(new Assignment(4, D(10, 2), Morning)); // 丁本月 16 小時
        store.Assignments.Add(new Assignment(1, D(10, 5), Morning)); // 丙本月 8 小時
        store.Assignments.Add(new Assignment(3, D(10, 6), Morning)); // 甲本月 8 小時

        var result = Find(store);

        // 乙 0h；丙、甲同為 8h 依姓名（Ordinal：丙 U+4E19 < 甲 U+7532）；丁 16h
        Assert.Equal(["乙", "丙", "甲", "丁"], result.Candidates.Select(c => c.Employee.Name));
        Assert.Equal([0, 480, 480, 960], result.Candidates.Select(c => c.MonthMinutes));
    }

    [Fact]
    public void Ordering_is_stable_regardless_of_input_order()
    {
        var a = Find(Store(Employee(1, "王"), Employee(2, "王"), Employee(3, "李")));
        var b = Find(Store(Employee(3, "李"), Employee(2, "王"), Employee(1, "王")));

        Assert.Equal(a.Candidates.Select(c => c.Employee.Id), b.Candidates.Select(c => c.Employee.Id));
        Assert.Equal([3, 1, 2], a.Candidates.Select(c => c.Employee.Id));
    }

    [Fact]
    public void Month_hours_count_only_shifts_starting_in_current_month()
    {
        var store = Store(Employee(1, "甲"));
        store.Assignments.Add(new Assignment(1, D(9, 30), Closing));

        var result = Find(store);

        Assert.Equal(0, Assert.Single(result.Candidates).MonthMinutes);
    }

    [Fact]
    public void Pre_existing_violations_do_not_block_a_candidate()
    {
        var store = Store(Employee(1, "甲", unavailable: Weekdays.Monday));
        store.Assignments.Add(new Assignment(1, D(10, 5), Morning)); // 週一，已有違規

        var result = Find(store);

        Assert.Single(result.Candidates);
    }

    [Theory]
    [InlineData(12)] // 缺口早於既有班
    [InlineData(15)] // 缺口在既有班之間
    [InlineData(17)] // 缺口晚於既有班
    public void Employee_already_over_weekly_limit_is_blocked_wherever_the_slot_falls_in_that_week(int day)
    {
        var store = Store(Employee(1, "甲", weeklyLimitHours: 20));
        store.Assignments.Add(new Assignment(1, D(10, 13), Morning));
        store.Assignments.Add(new Assignment(1, D(10, 14), Morning));
        store.Assignments.Add(new Assignment(1, D(10, 16), Morning)); // 已 24h > 20h

        var result = Find(store, Slot with { Date = D(10, day) });

        Assert.Empty(result.Candidates);
        Assert.Equal("全部候選人都會違反週工時規則", result.NoCandidateReason);
    }

    [Fact]
    public void Over_limit_in_another_week_does_not_block()
    {
        var store = Store(Employee(1, "甲", weeklyLimitHours: 20));
        store.AssignmentsFromDays(1, Morning, D(10, 5), 3); // 10/5 週已 24h

        Assert.Single(Find(store).Candidates);
    }

    [Fact]
    public void Week_over_limit_because_of_next_month_shift_blocks_slot_at_month_end()
    {
        var store = Store(Employee(1, "甲", weeklyLimitHours: 20));
        store.Assignments.Add(new Assignment(1, D(10, 26), Morning));
        store.Assignments.Add(new Assignment(1, D(11, 1), Morning)); // 指派 10/31 後，跨過上限的是 11/1 這班

        var result = Find(store, Slot with { Date = D(10, 31) });

        Assert.Equal("全部候選人都會違反週工時規則", result.NoCandidateReason);
    }

    [Fact]
    public void Employee_already_missing_a_rest_day_is_not_offered_the_next_day()
    {
        var store = Store(Employee(1, "甲"));
        store.AssignmentsFromDays(1, Morning, D(10, 8), 7); // 10/8–10/14 已連上 7 天

        var result = Find(store); // 10/15 會是第 8 天

        Assert.Equal("全部候選人都會違反休息日規則", result.NoCandidateReason);
    }

    [Fact]
    public void Reason_names_the_rule_when_all_candidates_violate_the_same_rule()
    {
        var store = Store(Employee(1, "甲"), Employee(2, "乙"));
        store.Assignments.Add(new Assignment(1, D(10, 14), Closing));
        store.Assignments.Add(new Assignment(2, D(10, 14), Closing));

        var result = Find(store);

        Assert.Empty(result.Candidates);
        Assert.Equal("全部候選人都會違反間隔規則", result.NoCandidateReason);
    }

    [Fact]
    public void Reason_lists_counts_when_candidates_are_blocked_by_different_rules()
    {
        var store = Store(Employee(1, "甲"), Employee(2, "乙", unavailable: Weekdays.Thursday), Employee(3, "丙"));
        store.Assignments.Add(new Assignment(1, D(10, 14), Closing));
        store.Assignments.Add(new Assignment(3, D(10, 14), Closing));

        var result = Find(store);

        Assert.Empty(result.Candidates);
        Assert.Equal("沒有可用的候選人：2 人違反間隔規則、1 人違反不可上班日規則", result.NoCandidateReason);
    }

    [Fact]
    public void Reason_explains_empty_pool()
    {
        var result = Find(Store(Employee(1, "內場", Role.Kitchen)));

        Assert.Empty(result.Candidates);
        Assert.Equal("沒有可指派的在職外場員工", result.NoCandidateReason);
    }

    [Fact]
    public void Reason_is_null_when_candidates_exist()
    {
        Assert.Null(Find(Store(Employee(1, "甲"))).NoCandidateReason);
    }
}
