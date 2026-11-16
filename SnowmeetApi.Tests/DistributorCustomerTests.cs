using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Controllers.DistributorPortal;
using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

// 分销商顾客归属（先到先得）和「我的顾客与消费」（逐单归属）。
// 规则（用户 2026-11-15 定）：接了分销商转发的券 / 领了卡 / 取了分销商雪票码订的票，顾客还没有归属就归属这位分销商，
// 已有归属不变；但这一单记在当前分销商名下。
public class DistributorCustomerTests
{
    private static async Task<Introducer> AddIntro(CoachFixture f, int memberId, int coachId, string type = "教练")
    {
        var relation = new OrderShareRelation { name = type + memberId, type = "个人", valid = true };
        f.Db.orderShareRelation.Add(relation);
        await f.Db.SaveChangesAsync();
        var intro = new Introducer { member_id = memberId, coach_id = coachId, type = type, name = type + memberId, relation_id = relation.id, valid = 1 };
        f.Db.introducer.Add(intro);
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        return intro;
    }

    private static TicketMigrationTests.TestTicketController Tickets(CoachFixture f) => new(f.NewContext(), f.Config);

    private static async Task<string> GrantOne(CoachFixture f, int coachId, string request)
    {
        Staff op = await f.Db.staff.AsNoTracking().FirstAsync(s => s.id == 9);
        await f.Admin_().GrantCouponsAsync(op, new() { coachId }, 16, 1, request);
        return await f.Db.coachCouponGrant.AsNoTracking().Where(g => g.grant_key.StartsWith(request + "-")).Select(g => g.ticket_code).FirstAsync();
    }

    private static async Task AcceptCoupon(CoachFixture f, string code, string coachSession, string accepterSession)
    {
        Assert.Equal(0, TicketMigrationTests.Result(await Tickets(f).SetTicketToMiniShare(code, coachSession)).code);
        Assert.Equal(0, TicketMigrationTests.Result(await Tickets(f).AcceptMiniTicket(code, "", accepterSession)).code);
    }

    private static async Task<int> PaidOrder(CoachFixture f, int memberId, string type, double paid, double refund = 0, string? careEquip = null)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = type, shop = "万龙服务中心", member_id = memberId, valid = 1, biz_date = DateTime.Now, total_amount = paid };
        db.order.Add(order); await db.SaveChangesAsync();
        var pay = new OrderPayment { order_id = order.id, amount = paid, pay_method = "微信支付", status = "支付成功", valid = 1 };
        db.orderPayment.Add(pay); await db.SaveChangesAsync();
        if (refund > 0) { db.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = order.id, payment_id = pay.id, amount = refund, state = 1, refund_id = "r-" + order.id }); await db.SaveChangesAsync(); }
        if (careEquip != null) { db.care.Add(new Care { order_id = order.id, equipment = careEquip, need_edge = 1, need_wax = 1, valid = 1 }); await db.SaveChangesAsync(); }
        return order.id;
    }

    private static async Task Attribute(CoachFixture f, int orderId, int introducerId, int memberId)
        => await new DistributorCustomerService(f.NewContext()).RecordOrderAsync(orderId, introducerId, memberId, DistributorOrder.SourceQr, DateTime.Now);

    private static async Task<int?> OwnerOf(CoachFixture f, int memberId)
        => (await f.NewContext().distributorCustomer.AsNoTracking().FirstOrDefaultAsync(c => c.member_id == memberId && c.valid == 1))?.introducer_id;

    [Fact]
    public async Task Establish_is_first_come_and_ignores_self_invalid_and_stopped_distributors()
    {
        await using var f = await CoachFixture.Create();
        var one = await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var two = await AddIntro(f, CoachFixture.CoachMember2, f.Coach2Id, "机构");
        var svc = () => new DistributorCustomerService(f.NewContext());
        Assert.False(await svc().EstablishAsync(CoachFixture.CoachMember, one.id, "雪票码", "1", DateTime.Now));   // 分销商本人
        Assert.False(await svc().EstablishAsync(CoachFixture.StudentA, 9999, "雪票码", "1", DateTime.Now));         // 不存在
        Assert.True(await svc().EstablishAsync(CoachFixture.StudentA, one.id, "雪票码", "7", DateTime.Now));
        Assert.False(await svc().EstablishAsync(CoachFixture.StudentA, two.id, "接券", "x", DateTime.Now));         // 已有归属不变
        Assert.Equal(one.id, await OwnerOf(f, CoachFixture.StudentA));
        await f.NewContext().introducer.Where(i => i.id == two.id).ExecuteUpdateAsync(x => x.SetProperty(i => i.valid, 0));
        Assert.False(await svc().EstablishAsync(CoachFixture.StudentB, two.id, "接券", "x", DateTime.Now));         // 已停用
        Assert.Null(await OwnerOf(f, CoachFixture.StudentB));
    }

    [Fact]
    public async Task Card_claim_and_accepted_coupon_establish_the_relation_but_never_change_an_existing_one()
    {
        await using var f = await CoachFixture.Create();
        var one = await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var two = await AddIntro(f, CoachFixture.CoachMember2, f.Coach2Id, "机构");
        await f.Admin_().SetPricingAsync(f.Coach2Id, CoachFixture.Standard());          // 二位也要有价格才能发卡

        await f.IssueAndClaim(CoachFixture.StudentA);                                  // 学员A 领一位的卡 → 归属一位
        var rel = await f.NewContext().distributorCustomer.AsNoTracking().SingleAsync(c => c.member_id == CoachFixture.StudentA);
        Assert.Equal((one.id, "领卡"), (rel.introducer_id, rel.source));
        f.Db.coachPriceFloor.Add(new CoachPriceFloor { coach_id = f.Coach2Id, biz_type = "租赁", shop = "", tier = "", min_rate = 0.5 });
        var template = new CoachCardTemplate { coach_id = f.Coach2Id, name = "学员卡", token = CoachCardRules.NewToken(), care_prices = "[]", rental_rate = 0.9 };
        f.Db.coachCardTemplate.Add(template); await f.Db.SaveChangesAsync();
        var share = await f.Cards().CreateCardAsync(f.Coach2Id, DateTime.Now, template.id);
        Assert.Equal(ShareCardState.ClaimedByMe, (await f.Cards().ClaimAsync(share.token, CoachFixture.StudentA, DateTime.Now)).state); // 领二位的卡 → 不变
        Assert.Equal(one.id, await OwnerOf(f, CoachFixture.StudentA));

        string code = await GrantOne(f, f.Coach2Id, "g-two");                          // 学员B 接二位转发的券 → 归属二位
        await AcceptCoupon(f, code, "session-6", "session-4");
        rel = await f.NewContext().distributorCustomer.AsNoTracking().SingleAsync(c => c.member_id == CoachFixture.StudentB);
        Assert.Equal((two.id, "接券", code), (rel.introducer_id, rel.source, rel.source_ref));

        string other = await GrantOne(f, f.CoachId, "g-one");                          // 学员B 再接一位的券 → 不变
        await AcceptCoupon(f, other, "session-2", "session-4");
        Assert.Equal(two.id, await OwnerOf(f, CoachFixture.StudentB));
    }

    [Fact]
    public async Task A_coupon_not_forwarded_by_its_coach_does_not_create_a_relation()
    {
        await using var f = await CoachFixture.Create();
        await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        string code = await GrantOne(f, f.CoachId, "g-one");
        await AcceptCoupon(f, code, "session-2", "session-3");     // 教练转给学员A：建立
        Assert.NotNull(await OwnerOf(f, CoachFixture.StudentA));
        // 学员A 再转给学员B：转出的人不是教练，不建立关系
        Assert.Equal(0, TicketMigrationTests.Result(await Tickets(f).SetTicketToMiniShare(code, "session-3")).code);
        Assert.Equal(0, TicketMigrationTests.Result(await Tickets(f).AcceptMiniTicket(code, "", "session-4")).code);
        Assert.Null(await OwnerOf(f, CoachFixture.StudentB));
    }

    [Fact]
    public async Task Backfill_turns_existing_student_links_into_relations_first_come_and_is_repeatable()
    {
        await using var f = await CoachFixture.Create();
        await f.Admin_().SetPricingAsync(f.Coach2Id, CoachFixture.Standard());
        await f.IssueAndClaim(CoachFixture.StudentA, f.Coach2Id);                       // 还没有分销商记录时的老学员：先领的是二位
        await Task.Delay(30);
        await f.IssueAndClaim(CoachFixture.StudentA, f.CoachId);
        await f.IssueAndClaim(CoachFixture.StudentC, f.CoachId);
        Assert.Empty(await f.NewContext().distributorCustomer.ToListAsync());          // 钩子没有分销商可记
        var one = await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var two = await AddIntro(f, CoachFixture.CoachMember2, f.Coach2Id, "机构");

        Assert.Equal(2, await new DistributorCustomerService(f.NewContext()).BackfillLegacyLinksAsync());
        Assert.Equal(two.id, await OwnerOf(f, CoachFixture.StudentA));                 // 更早领的是二位
        Assert.Equal(one.id, await OwnerOf(f, CoachFixture.StudentC));
        Assert.Equal(0, await new DistributorCustomerService(f.NewContext()).BackfillLegacyLinksAsync());
    }

    [Fact]
    public async Task Customers_and_consumption_count_only_orders_attributed_to_me()
    {
        await using var f = await CoachFixture.Create();
        var one = await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var two = await AddIntro(f, CoachFixture.CoachMember2, f.Coach2Id, "机构");
        // 学员A：归属我；用我的学员卡下的一单算我的，另一单（没用卡）不算
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);
        int cardOrder = await PaidOrder(f, CoachFixture.StudentA, "养护", 134.4, careEquip: "双板");
        await f.Pricing().RecordUseAsync(card, cardOrder, "养护", 1, 0, "rate", 0.8, 168, 33.6, DateTime.Now);
        await PaidOrder(f, CoachFixture.StudentA, "养护", 500);
        // 学员B：归属二位（接了二位的券），但通过我的雪票码买了一单、用了我转发的券又一单 → 这两单都记在我名下
        await AcceptCoupon(f, await GrantOne(f, f.Coach2Id, "g-two"), "session-6", "session-4");
        int qrOrder = await PaidOrder(f, CoachFixture.StudentB, "雪票", 200);
        await Attribute(f, qrOrder, one.id, CoachFixture.StudentB);
        string myCoupon = await GrantOne(f, f.CoachId, "g-one");
        int couponOrder = await PaidOrder(f, CoachFixture.StudentB, "养护", 70, careEquip: "单板");
        f.Db.discount.Add(new Discount { ticket_code = myCoupon, order_id = couponOrder, amount = 10, valid = 1 });
        await f.Db.SaveChangesAsync();
        // 学员C：没有归属，只有我的雪票码订单；另有一单全额退款的不算
        int cOrder = await PaidOrder(f, CoachFixture.StudentC, "雪票", 309);
        await Attribute(f, cOrder, one.id, CoachFixture.StudentC);
        await Attribute(f, await PaidOrder(f, CoachFixture.StudentC, "雪票", 100, refund: 100), one.id, CoachFixture.StudentC);
        // 别的分销商的订单不出现在我这里
        await Attribute(f, await PaidOrder(f, CoachFixture.StudentC, "雪票", 88), two.id, CoachFixture.StudentC);

        var svc = new DistributorConsumptionService(f.NewContext());
        var (summary, customers) = await svc.GetCustomersAsync(one);
        Assert.Equal(new[] { CoachFixture.StudentA, CoachFixture.StudentB, CoachFixture.StudentC }.OrderBy(x => x), customers.Select(c => c.memberId).OrderBy(x => x));
        var a = customers.Single(c => c.memberId == CoachFixture.StudentA);
        Assert.Equal(("mine", 1, 134.4), (a.relation, a.orderCount, a.amount));
        var b = customers.Single(c => c.memberId == CoachFixture.StudentB);
        Assert.Equal(("other", 2, 270.0), (b.relation, b.orderCount, b.amount));
        var c = customers.Single(x => x.memberId == CoachFixture.StudentC);
        Assert.Equal(("none", 1, 309.0), (c.relation, c.orderCount, c.amount));
        var json = JsonSerializer.SerializeToElement(summary);
        Assert.Equal(1, json.GetProperty("customers").GetInt32());                      // 名下顾客只算归属我的
        Assert.Equal(4, json.GetProperty("orders").GetInt32());
        Assert.Equal(CoachCardRules.Money(134.4 + 270 + 309), json.GetProperty("amount").GetDouble());

        var (_, orders) = await svc.GetCustomerOrdersAsync(one, CoachFixture.StudentB);
        Assert.Equal(2, orders.Count);
        Assert.Single(orders, o => o.type == "雪票" && o.sources.SequenceEqual(new[] { "雪票码" }));
        Assert.Single(orders, o => o.type == "养护" && o.sources.SequenceEqual(new[] { "优惠券" }));
        await Assert.ThrowsAsync<CoachException>(() => svc.GetCustomerOrdersAsync(one, CoachFixture.CoachMember2));   // 不是我的顾客
    }

    [Fact]
    public async Task Customers_endpoints_only_answer_for_an_active_coach_or_organization()
    {
        await using var f = await CoachFixture.Create();
        await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        await f.IssueAndClaim(CoachFixture.StudentA);
        var ctl = new DistributorController(f.NewContext(), f.Config);
        var ok = await ctl.GetMyCustomers("session-2");
        Assert.Equal(0, ok.code);
        Assert.Equal(CoachFixture.StudentA, JsonSerializer.SerializeToElement(ok.data).GetProperty("customers")[0].GetProperty("memberId").GetInt32());
        Assert.Equal(3, (await ctl.GetMyCustomers("session-3")).code);                  // 学员不是分销商
        Assert.Equal(2, (await ctl.GetMyCustomers("nope")).code);
        Assert.Equal(1, (await ctl.GetCustomerOrders("session-2", CoachFixture.StudentC)).code);   // 不是我的顾客
    }

    [Fact]
    public async Task Backfill_endpoint_is_admin_only()
    {
        await using var f = await CoachFixture.Create();
        await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        await f.IssueAndClaim(CoachFixture.StudentA);
        await f.NewContext().distributorCustomer.ExecuteDeleteAsync();
        var ctl = new IntroducerController(f.NewContext(), f.Config, ShareFixture.Http);
        Assert.Equal(3, (await ctl.BackfillCustomers("session-7")).code);               // 店员
        var res = await ctl.BackfillCustomers("session-1");                             // 系统管理员
        Assert.Equal(0, res.code);
        Assert.Equal(1, JsonSerializer.SerializeToElement(res.data).GetProperty("created").GetInt32());
    }

    // ---------- 归属记录是附带的：新表没建或写入失败，都不能影响取票 / 领卡 / 接券 / 下单 ----------

    [Fact]
    public async Task Guarded_hooks_return_false_instead_of_throwing_when_the_new_tables_are_missing()
    {
        await using var f = await CoachFixture.Create();
        var intro = await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var db = f.NewContext();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE distributor_order");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE distributor_customer");
        var svc = new DistributorCustomerService(db);
        Assert.False(await svc.TryRecordOrderAsync(1, intro.id, CoachFixture.StudentA, "雪票码", DateTime.Now));
        Assert.False(await svc.TryEstablishForPickedUpOrderAsync(1, DateTime.Now));
        Assert.False(await svc.TryOnCardClaimedAsync(f.CoachId, CoachFixture.StudentA, 1, DateTime.Now));
        Assert.False(await svc.TryOnTicketAcceptedAsync(await GrantOne(f, f.CoachId, "g-x"), CoachFixture.CoachMember, CoachFixture.StudentA, DateTime.Now));
        // 没有留下待插入的归属行，调用方后面的 SaveChanges 不受牵连
        Assert.Empty(db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added));
        db.member.Add(new Member { id = 99, real_name = "后面的保存" });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_failed_insert_is_swallowed_leaves_nothing_tracked_and_the_next_write_works()
    {
        await using var f = await CoachFixture.Create();
        var intro = await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var db = f.NewContext();
        db.distributorOrder.Add(new DistributorOrder { order_id = 7, introducer_id = intro.id, member_id = CoachFixture.StudentA, source = "雪票码" });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_customer BEFORE INSERT ON distributor_customer BEGIN SELECT RAISE(ABORT, 'test-failure'); END");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_order BEFORE INSERT ON distributor_order BEGIN SELECT RAISE(ABORT, 'test-failure'); END");
        var svc = new DistributorCustomerService(db);
        Assert.True(await svc.TryEstablishForPickedUpOrderAsync(7, DateTime.Now));      // 内部已处理插入失败，没抛
        Assert.True(await svc.TryRecordOrderAsync(8, intro.id, CoachFixture.StudentB, "雪票码", DateTime.Now));
        Assert.Empty(db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added));
        Assert.Null(await OwnerOf(f, CoachFixture.StudentA));
        Assert.Equal(1, await f.NewContext().distributorOrder.CountAsync());
        // 触发器去掉后，同一个上下文里再来一次就能记上
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_customer");
        Assert.True(await svc.TryEstablishForPickedUpOrderAsync(7, DateTime.Now));
        Assert.NotNull(await OwnerOf(f, CoachFixture.StudentA));
    }

    [Fact]
    public async Task Card_claim_and_coupon_accept_still_succeed_when_the_new_tables_are_missing()
    {
        await using var f = await CoachFixture.Create();
        await AddIntro(f, CoachFixture.CoachMember, f.CoachId);
        var db = f.NewContext();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE distributor_order");
        await db.Database.ExecuteSqlRawAsync("DROP TABLE distributor_customer");
        CoachCard card = await f.IssueAndClaim(CoachFixture.StudentA);                  // 里面断言了领卡成功
        Assert.Equal(CoachCard.StatusClaimed, card.status);
        await AcceptCoupon(f, await GrantOne(f, f.CoachId, "g-one"), "session-2", "session-4");   // 里面断言了接券 code == 0
        Assert.Equal(CoachFixture.StudentB, (await f.NewContext().ticket.AsNoTracking().SingleAsync(t => t.member_id == CoachFixture.StudentB)).member_id);
    }
}
