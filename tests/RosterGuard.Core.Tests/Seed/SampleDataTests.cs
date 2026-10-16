using RosterGuard.Core.Domain;
using RosterGuard.Core.Rules;
using RosterGuard.Core.Seed;
using RosterGuard.Core.Session;
using static RosterGuard.Core.Seed.SampleData;

namespace RosterGuard.Core.Tests.Seed;

/// <summary>確保 samples/expected-violations.md 與範例資料一致。</summary>
public class SampleDataTests
{
    private static RosterSession Session(IReadOnlyList<Assignment> assignments, int month)
    {
        var store = new FakeRosterStore();
        store.Employees.AddRange(Employees);
        store.ShiftTypes.AddRange(ShiftTypes);
        store.Assignments.UnionWith(assignments);
        return new RosterSession(store, 2026, month);
    }

    [Fact]
    public void Sample_has_nine_employees_and_three_shift_types()
    {
        Assert.Equal(9, Employees.Count);
        Assert.Equal(3, ShiftTypes.Count);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void Baseline_rotation_has_no_violations(int month)
    {
        Assert.Empty(Session(BaselineAssignments(), month).Violations);
    }

    [Fact]
    public void September_matches_expected_violation_list()
    {
        var actual = Session(Assignments(), 9).Violations.Select(Describe);

        Assert.Equal(
        [
            "時段重疊|吳俊傑|09-04",
            "間隔不足|蔡宗翰|09-14",
            "人力不足|09-24|午班|外場",
            "不可上班日|王小芳|09-26",
        ], actual);
    }

    [Fact]
    public void October_matches_expected_violation_list()
    {
        var actual = Session(Assignments(), 10).Violations.Select(Describe);

        Assert.Equal(
        [
            "缺少休息日|張家豪|10-06",
            "週工時超過上限|李雅婷|10-18",
            "人力超額|10-24|午班|內場",
        ], actual);
    }

    private static string Describe(Violation v) => v.Key.EmployeeId is { } id
        ? $"{v.Rule.ToChinese()}|{Employees.Single(e => e.Id == id).Name}|{v.Date:MM-dd}"
        : $"{v.Rule.ToChinese()}|{v.Date:MM-dd}|{ShiftTypes.Single(s => s.Id == v.Key.ShiftTypeId).Name}|{v.Key.Role!.Value.ToChinese()}";
}
