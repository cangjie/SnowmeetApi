using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 夜间 23:00 提交哪些分账记录：先万龙、后介绍人；只提交有效的、没标手动的、关单日 <= 昨天的。
public class DueShareSelectionTests
{
    private static async Task<SnowmeetApi.Data.ApplicationDBContext> SeedAsync()
    {
        var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);   // 关单 11-01 15:42，付款 id 4
        // orderShare.relation 是必填关系，Include 会变成内连接，收款方行要有（生产里有）
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "万龙租赁分账", valid = true });
        db.orderShareRelation.Add(new OrderShareRelation { id = 9, type = "个人", name = "示例", valid = true });
        db.orderShare.Add(new OrderShare { id = 10, order_id = 1, relation_id = 9, amount = 40, valid = true, create_date = DateTime.Now });
        db.orderShare.Add(new OrderShare { id = 11, order_id = 1, relation_id = 4, amount = 200, valid = true, create_date = DateTime.Now });
        db.orderShare.Add(new OrderShare { id = 12, order_id = 1, relation_id = 4, amount = 99, valid = false, create_date = DateTime.Now });
        PaymentShare Row(int id, int shareId, bool valid = true, bool manual = false, DateTime? submit = null) => new()
        {
            id = id, payment_id = 4, share_id = shareId, amount = 1, valid = valid, can_not_share = manual, submit_time = submit,
            out_trade_no = "WT_ZL_261031_00001_ZF_01_FZ_" + id.ToString().PadLeft(2, '0'),
        };
        db.paymentShare.Add(Row(1, 10));                                 // 介绍人，id 比万龙小
        db.paymentShare.Add(Row(2, 11));                                 // 万龙
        db.paymentShare.Add(Row(3, 11, manual: true));                   // 标了手动
        db.paymentShare.Add(Row(4, 11, valid: false));                   // 记录作废
        db.paymentShare.Add(Row(5, 10, submit: DateTime.Now));           // 已提交
        db.paymentShare.Add(Row(6, 12));                                 // 应分作废
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Wanlong_first_then_introducers_and_only_valid_due_rows()
    {
        await using var db = await SeedAsync();
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);

        var due = await helper.GetDueWanlongShares(new DateTime(2026, 11, 1));

        Assert.Equal(new[] { 2, 1 }, due.Select(s => s.id).ToArray());
    }

    [Fact]
    public async Task Nothing_is_due_before_the_close_date()
    {
        await using var db = await SeedAsync();
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);

        Assert.Empty(await helper.GetDueWanlongShares(new DateTime(2026, 10, 31)));
    }
}
