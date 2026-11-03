using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;

namespace SnowmeetApi.Tests;

// 分账列表：每列的来源、筛选、分页、合计。规格 §6。
public class ShareListServiceTests
{
    // 订单 1：租赁（万龙体验中心，11-01 关单）；订单 2：雪票（崇礼旗舰店，雪票 id 21）。
    //   10 万龙·自动待分            11 李教练(介绍人)·设成手动、预计 11-05、店员 28 设的
    //   12 雪票·自动已分            13 雪票·被拒绝            14 雪票·没有分账记录
    //   15 万龙·已记手动批次 MS-20261103-01   16 作废（不应出现）
    internal static async Task<ApplicationDBContext> SeedAsync()
    {
        var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        db.order.Add(new Order { id = 2, code = "XP_261030_00001", type = "雪票", shop = "崇礼旗舰店", valid = 1, create_date = new DateTime(2026, 10, 30) });
        db.skiPass.Add(new SkiPass { id = 21, order_id = 2, valid = 1, resort = "万龙", product_name = "雪票" });
        db.staff.Add(new Staff { id = 28, name = "苍杰", valid = 1, title_level = 300 });
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "万龙租赁分账", valid = true });
        db.orderShareRelation.Add(new OrderShareRelation { id = 5, type = "个人", name = "肖志强", valid = true });
        db.orderShareRelation.Add(new OrderShareRelation { id = 9, type = "个人", name = "示例·李教练关系", valid = true });
        db.introducer.Add(new Introducer { id = 1, member_id = 100, type = "教练", name = "李教练", relation_id = 9 });
        db.shareBatch.Add(new ShareBatch { id = 1, batch_no = "MS-20261103-01", share_count = 1, total_amount = 150, created_by_staff_id = 28, create_date = new DateTime(2026, 11, 3, 18, 30, 0) });

        OrderShare S(int id, int order, int relation, double amount, DateTime created, DateTime? plan = null, bool auto = true, int? batch = null, bool valid = true)
            => new() { id = id, order_id = order, relation_id = relation, amount = amount, create_date = created, plan_date = plan, auto_share = auto, batch_id = batch, valid = valid };
        db.orderShare.Add(S(10, 1, 4, 200, new DateTime(2026, 11, 1, 16, 0, 0)));
        db.orderShare.Add(S(11, 1, 9, 40, new DateTime(2026, 11, 3, 10, 0, 0), plan: new DateTime(2026, 11, 5), auto: false));
        db.orderShare.Add(S(12, 2, 5, 1, new DateTime(2026, 10, 30, 12, 0, 0)));
        db.orderShare.Add(S(13, 2, 5, 1, new DateTime(2026, 10, 30, 12, 5, 0)));
        db.orderShare.Add(S(14, 2, 5, 1, new DateTime(2026, 10, 30, 12, 10, 0)));
        db.orderShare.Add(S(15, 1, 4, 150, new DateTime(2026, 11, 3, 9, 0, 0), batch: 1));
        db.orderShare.Add(S(16, 1, 4, 99, new DateTime(2026, 11, 3, 9, 0, 0), valid: false));

        PaymentShare P(int id, int share, bool? success = null, DateTime? submit = null, DateTime? response = null, bool cannot = false)
            => new() { id = id, payment_id = 4, share_id = share, amount = 1, out_trade_no = "T_" + id, success = success, submit_time = submit, response_time = response, can_not_share = cannot };
        db.paymentShare.Add(P(101, 10));
        db.paymentShare.Add(P(102, 11, cannot: true));
        db.paymentShare.Add(P(103, 12, true, new DateTime(2026, 10, 31, 23, 0, 3), new DateTime(2026, 10, 31, 23, 0, 5)));
        db.paymentShare.Add(P(104, 13, null, new DateTime(2026, 10, 31, 23, 0, 3)));
        db.paymentShare.Add(P(105, 15, true, new DateTime(2026, 11, 3, 18, 30, 0), new DateTime(2026, 11, 3, 18, 30, 0)));
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "amount", 11, "设置介绍人分账", null, 28, null, "40", "订单 1"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }

    internal static ShareListQuery Q(string? status = null, string? type = null, string? mode = null, string? keyword = null,
        DateTime? start = null, DateTime? end = null, int page = 1, int size = 50) => new(status, type, mode, keyword, start, end, page, size);

    private static ShareListItem Item(ShareListPage page, int id) => page.items.Single(i => i.orderShareId == id);

    [Fact]
    public async Task Each_column_comes_from_the_right_place()
    {
        await using var db = await SeedAsync();

        var page = await new ShareListService(db).ListAsync(Q());

        var wanlong = Item(page, 10);
        Assert.Equal(("WT_ZL_261031_00001", "万龙体验中心", "租赁", "万龙租赁分账", 200d), (wanlong.orderCode, wanlong.shop, wanlong.orderType, wanlong.payee, wanlong.amount));
        Assert.Equal(("系统", "2026-11-02", "2026-11-01", "未分", "自动"), (wanlong.applicant, wanlong.planDate, wanlong.listDate, wanlong.status, wanlong.mode));
        Assert.Equal(1, wanlong.orderId);

        var intro = Item(page, 11);
        Assert.Equal(("李教练", "苍杰", "2026-11-05", "2026-11-05"), (intro.payee, intro.applicant, intro.planDate, intro.listDate));
        Assert.Equal(("手动", "设为手动", "未分"), (intro.mode, intro.manualReason, intro.status));
        Assert.Null(wanlong.batch);
        Assert.Null(wanlong.sharedTime);
    }

    [Fact]
    public async Task Done_rows_show_actual_time_and_batch()
    {
        await using var db = await SeedAsync();

        var page = await new ShareListService(db).ListAsync(Q());

        var auto = Item(page, 12);
        Assert.Equal(("已分", "自动", "2026-10-31 23:00", "20261031"), (auto.status, auto.mode, auto.sharedTime, auto.batch));
        var manual = Item(page, 15);
        Assert.Equal(("已分", "手动", "2026-11-03 18:30", "MS-20261103-01"), (manual.status, manual.mode, manual.sharedTime, manual.batch));
        Assert.Equal("已记手动批次", manual.manualReason);
    }

    [Fact]
    public async Task Manual_rows_are_selectable_and_counted()
    {
        await using var db = await SeedAsync();

        var page = await new ShareListService(db).ListAsync(Q());

        Assert.Equal(new[] { 11, 13, 14 }, page.items.Where(i => i.selectable).Select(i => i.orderShareId).OrderBy(x => x).ToArray());
        Assert.Equal(3, page.selectableCount);
        Assert.Equal("自动被拒绝", Item(page, 13).manualReason);
        Assert.Equal("没有分账记录", Item(page, 14).manualReason);
        Assert.False(Item(page, 10).selectable);   // 自动待分
        Assert.False(Item(page, 12).selectable);   // 自动已分
        Assert.False(Item(page, 15).selectable);   // 已记批次
        Assert.DoesNotContain(page.items, i => i.orderShareId == 16);   // 作废的不出现
    }

    [Fact]
    public async Task Filters_narrow_the_list_and_the_totals_follow()
    {
        await using var db = await SeedAsync();
        var svc = new ShareListService(db);
        int[] Ids(ShareListPage p) => p.items.Select(i => i.orderShareId).OrderBy(x => x).ToArray();

        Assert.Equal(new[] { 10, 11, 13, 14 }, Ids(await svc.ListAsync(Q(status: "未分"))));
        Assert.Equal(new[] { 12, 15 }, Ids(await svc.ListAsync(Q(status: "已分"))));
        Assert.Equal(new[] { 12, 13, 14 }, Ids(await svc.ListAsync(Q(type: "雪票"))));
        Assert.Equal(new[] { 11, 13, 14, 15 }, Ids(await svc.ListAsync(Q(mode: "手动"))));
        Assert.Equal(new[] { 10, 12 }, Ids(await svc.ListAsync(Q(mode: "自动"))));
        Assert.Equal(new[] { 11 }, Ids(await svc.ListAsync(Q(keyword: "李教练"))));            // 收款人
        Assert.Equal(new[] { 10, 11, 15 }, Ids(await svc.ListAsync(Q(keyword: "WT_ZL_261031_00001"))));   // 订单号
        // 日期按 listDate（有计划日期取计划日期，否则取创建日）：11-03 ~ 11-05 只有 11（11-05）和 15（11-03）
        Assert.Equal(new[] { 11, 15 }, Ids(await svc.ListAsync(Q(start: new DateTime(2026, 11, 3), end: new DateTime(2026, 11, 5)))));

        var unfinishedSnow = await svc.ListAsync(Q(status: "未分", type: "雪票"));
        Assert.Equal(2, unfinishedSnow.total);
        Assert.Equal(2, unfinishedSnow.totalAmount);
        Assert.Equal(2, unfinishedSnow.selectableCount);
    }

    [Fact]
    public async Task Paging_orders_unfinished_first_and_totals_cover_the_whole_filtered_list()
    {
        await using var db = await SeedAsync();
        var svc = new ShareListService(db);

        var p1 = await svc.ListAsync(Q(size: 2, page: 1));
        var p2 = await svc.ListAsync(Q(size: 2, page: 2));
        var p3 = await svc.ListAsync(Q(size: 2, page: 3));

        // 未分在前（按日期倒序、同日 id 倒序），已分在后
        Assert.Equal(new[] { 11, 10 }, p1.items.Select(i => i.orderShareId).ToArray());
        Assert.Equal(new[] { 14, 13 }, p2.items.Select(i => i.orderShareId).ToArray());
        Assert.Equal(new[] { 15, 12 }, p3.items.Select(i => i.orderShareId).ToArray());
        foreach (var p in new[] { p1, p2, p3 })
        {
            Assert.Equal(6, p.total);
            Assert.Equal(393, p.totalAmount);   // 200 + 40 + 1 + 1 + 1 + 150
            Assert.Equal(3, p.selectableCount);
        }
        Assert.Empty((await svc.ListAsync(Q(size: 2, page: 4))).items);
    }

    [Fact]
    public async Task Ski_pass_rows_carry_the_skipass_id()
    {
        await using var db = await SeedAsync();

        var page = await new ShareListService(db).ListAsync(Q());

        Assert.All(new[] { 12, 13, 14 }, id => Assert.Equal(21, Item(page, id).skipassId));
        Assert.All(new[] { 10, 11, 15 }, id => Assert.Null(Item(page, id).skipassId));
    }

    [Fact]
    public async Task Total_amount_is_rounded_to_cents()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "万龙租赁分账", valid = true });
        foreach (var (id, amount) in new[] { (1, 0.1), (2, 0.2), (3, 0.0) })
        {
            db.orderShare.Add(new OrderShare { id = id, order_id = 1, relation_id = 4, amount = amount, create_date = new DateTime(2026, 11, 1), valid = true });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var page = await new ShareListService(db).ListAsync(Q());

        Assert.Equal(0.3, page.totalAmount);
    }
}
