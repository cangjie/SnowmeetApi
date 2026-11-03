using SnowmeetApi.Helpers;

namespace SnowmeetApi.Tests;

// 分账列表的规则：状态（已分/未分）、自动/手动、可勾选、默认批次号、批次号校验。规格 §6.2、§6.3、§7.2。
public class ShareListRulesTests
{
    private static readonly DateTime T = new(2026, 11, 3, 23, 0, 5);

    private static RecordFacts Ok() => new(true, T, T.AddSeconds(2), false);
    private static RecordFacts Pending() => new(null, null, null, false);              // 未提交，等夜间自动分
    private static RecordFacts PendingManual() => new(null, null, null, true);         // 未提交，系统标了不能自动分
    private static RecordFacts Rejected() => new(null, T, T.AddSeconds(1), false);     // 提交过但被拒绝
    private static ShareFacts F(bool auto, int? batch, params RecordFacts[] records) => new(auto, batch, records);

    [Fact]
    public void Status_is_done_only_with_a_batch_or_when_every_record_succeeded()
    {
        Assert.Equal("已分", ShareListRules.Status(F(true, 7)));                       // 手动批次，没有记录也算
        Assert.Equal("已分", ShareListRules.Status(F(true, 7, Pending())));
        Assert.Equal("已分", ShareListRules.Status(F(true, null, Ok(), Ok())));
        Assert.Equal("未分", ShareListRules.Status(F(true, null, Ok(), Pending())));   // 部分成功仍是未分
        Assert.Equal("未分", ShareListRules.Status(F(true, null, Rejected())));
        Assert.Equal("未分", ShareListRules.Status(F(true, null)));                     // 没有任何分账记录
    }

    [Theory]
    [InlineData("batch", "手动", "已记手动批次")]
    [InlineData("off", "手动", "设为手动")]
    [InlineData("system", "手动", "系统标手动")]
    [InlineData("rejected", "手动", "自动被拒绝")]
    [InlineData("none", "手动", "没有分账记录")]
    [InlineData("pending", "自动", null)]
    [InlineData("done", "自动", null)]
    [InlineData("partial", "自动", null)]
    public void Mode_and_reason_follow_spec_6_3(string kind, string mode, string? reason)
    {
        ShareFacts f = kind switch
        {
            "batch" => F(true, 7, Pending()),
            "off" => F(false, null, Pending()),
            "system" => F(true, null, PendingManual()),
            "rejected" => F(true, null, Rejected()),
            "none" => F(true, null),
            "pending" => F(true, null, Pending()),
            "done" => F(true, null, Ok(), Ok()),
            _ => F(true, null, Ok(), Pending()),            // 一笔成功、一笔等自动：仍是自动待分
        };
        Assert.Equal(mode, ShareListRules.Mode(f));
        Assert.Equal(reason, ShareListRules.ManualReason(f));
    }

    [Fact]
    public void Reason_precedence_batch_then_off_then_system_then_rejected_then_none()
    {
        Assert.Equal("已记手动批次", ShareListRules.ManualReason(F(false, 7, PendingManual(), Rejected())));
        Assert.Equal("设为手动", ShareListRules.ManualReason(F(false, null)));             // 关了自动，且没有记录
        Assert.Equal("系统标手动", ShareListRules.ManualReason(F(true, null, PendingManual(), Rejected())));
        Assert.Equal("自动被拒绝", ShareListRules.ManualReason(F(true, null, Ok(), Rejected())));
    }

    [Fact]
    public void Only_unfinished_manual_shares_are_selectable()
    {
        Assert.True(ShareListRules.Selectable(F(false, null, Pending())));
        Assert.True(ShareListRules.Selectable(F(true, null, PendingManual())));
        Assert.True(ShareListRules.Selectable(F(true, null, Rejected())));
        Assert.True(ShareListRules.Selectable(F(true, null)));
        Assert.False(ShareListRules.Selectable(F(true, null, Pending())));                 // 自动待分
        Assert.False(ShareListRules.Selectable(F(true, null, Ok())));                      // 自动已分
        Assert.False(ShareListRules.Selectable(F(true, 7, PendingManual())));              // 已记批次
    }

    [Theory]
    [InlineData(0, "MS-20261103-01")]
    [InlineData(1, "MS-20261103-02")]
    [InlineData(11, "MS-20261103-12")]
    [InlineData(99, "MS-20261103-100")]
    public void Default_batch_number_is_date_plus_next_sequence(int batchesToday, string expected)
    {
        Assert.Equal(expected, ShareListRules.DefaultBatchNo(new DateTime(2026, 11, 3, 18, 0, 0), batchesToday));
    }

    [Fact]
    public void Batch_number_problems()
    {
        Assert.NotNull(ShareListRules.BatchNoProblem(null));
        Assert.NotNull(ShareListRules.BatchNoProblem(""));
        Assert.NotNull(ShareListRules.BatchNoProblem("   "));
        Assert.NotNull(ShareListRules.BatchNoProblem(new string('A', 41)));
        Assert.Null(ShareListRules.BatchNoProblem("MS-20261103-01"));
        Assert.Null(ShareListRules.BatchNoProblem(" a "));
        Assert.Null(ShareListRules.BatchNoProblem(new string('A', 40)));
        Assert.Null(ShareListRules.BatchNoProblem(" " + new string('A', 40) + " "));          // 去掉首尾空格后 40
    }
}
