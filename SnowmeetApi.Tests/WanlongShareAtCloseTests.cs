using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 2026-11-02 用户定：万龙租赁的应分记录在关单时生成，不在退押金时生成。
// 关单（RentController.CloseOrder，每晚 22:00）时按最终的租金明细算应分，再生成分账记录；
// 没有退款的单也要有。已经有分账记录的单不重复生成。
public class WanlongShareAtCloseTests
{
    // 付 500，退 430，租金 70 → 关单；应分 35 = 净额 70 的 50%，超过 30%，只生成记录、不自动分
    [Fact]
    public async Task CloseOrder_creates_order_share_and_payment_share_for_a_refunded_wanlong_rental()
    {
        await using var db = await SeedAsync(shop: "万龙体验中心", payMethod: "微信支付", paid: 500, refunded: 430, rent: 70);

        await new RentController(db, Config, Http).CloseOrder();

        Assert.Equal(1, (await db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
        var orderShare = await db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        Assert.Equal(35, orderShare.amount, 2);
        var paymentShare = await db.paymentShare.AsNoTracking().SingleAsync(s => s.share_id == orderShare.id);
        Assert.Equal(35, paymentShare.amount, 2);
        Assert.True(paymentShare.can_not_share);
    }

    // 没有任何退款（押金刚好等于租金）的单以前永远没有应分记录
    [Fact]
    public async Task CloseOrder_creates_shares_for_a_wanlong_rental_without_any_refund()
    {
        await using var db = await SeedAsync(shop: "万龙体验中心", payMethod: "支付宝", paid: 180, refunded: 0, rent: 180);

        await new RentController(db, Config, Http).CloseOrder();

        Assert.Equal(1, (await db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
        var orderShare = await db.orderShare.AsNoTracking().SingleAsync(s => s.order_id == 1 && s.valid);
        Assert.Equal(90, orderShare.amount, 2);
        var paymentShare = await db.paymentShare.AsNoTracking().SingleAsync(s => s.share_id == orderShare.id);
        Assert.Equal(90, paymentShare.amount, 2);
    }

    [Fact]
    public async Task CloseOrder_does_not_create_shares_for_other_shops()
    {
        await using var db = await SeedAsync(shop: "测试店", payMethod: "微信支付", paid: 500, refunded: 430, rent: 70);

        await new RentController(db, Config, Http).CloseOrder();

        Assert.Equal(1, (await db.order.AsNoTracking().SingleAsync(o => o.id == 1)).closed);
        Assert.Empty(await db.orderShare.AsNoTracking().ToListAsync());
        Assert.Empty(await db.paymentShare.AsNoTracking().ToListAsync());
    }

    // 退款时留下的旧应分记录（按当时的数据算的 99）还没有分账记录：关单时按最终数据重算，旧的作废
    [Fact]
    public async Task CloseOrder_recomputes_a_stale_order_share_that_has_no_payment_share_yet()
    {
        await using var db = await SeedAsync(shop: "万龙体验中心", payMethod: "微信支付", paid: 500, refunded: 430, rent: 70);
        db.orderShare.Add(new OrderShare { id = 10, order_id = 1, relation_id = 4, amount = 99, valid = true,
            create_date = new DateTime(2026, 11, 1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new RentController(db, Config, Http).CloseOrder();

        var valid = await db.orderShare.AsNoTracking().Where(s => s.order_id == 1 && s.valid).ToListAsync();
        Assert.Single(valid);
        Assert.Equal(35, valid[0].amount, 2);
        Assert.False((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == 10)).valid);
        Assert.Single(await db.paymentShare.AsNoTracking().Where(s => s.share_id == valid[0].id).ToListAsync());
    }

    // 已经有分账记录（可能手工调过金额）的单，关单时不动它、不重复生成
    [Fact]
    public async Task CloseOrder_leaves_an_order_that_already_has_payment_shares_alone()
    {
        await using var db = await SeedAsync(shop: "万龙体验中心", payMethod: "微信支付", paid: 500, refunded: 430, rent: 70);
        db.orderShare.Add(new OrderShare { id = 10, order_id = 1, relation_id = 4, amount = 99, valid = true,
            create_date = new DateTime(2026, 11, 1) });
        db.paymentShare.Add(new PaymentShare { id = 20, payment_id = 4, share_id = 10, amount = 99, valid = true,
            out_trade_no = "WT_ZL_261028_00001_ZF_01_FZ_01", create_date = new DateTime(2026, 11, 1) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await new RentController(db, Config, Http).CloseOrder();

        var shares = await db.orderShare.AsNoTracking().Where(s => s.order_id == 1).ToListAsync();
        Assert.Single(shares);
        Assert.True(shares[0].valid);
        Assert.Equal(99, shares[0].amount, 2);
        Assert.Single(await db.paymentShare.AsNoTracking().ToListAsync());
    }

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
    private static readonly IHttpContextAccessor Http = new HttpContextAccessor();

    // 一张已结清、押金退完、可以关单的租赁单：一次付款 paid，退 refunded，租金明细 rent
    private static async Task<ApplicationDBContext> SeedAsync(string shop, string payMethod, double paid,
        double refunded, double rent)
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        var db = new ApplicationDBContext(options);
        await db.Database.EnsureCreatedAsync();
        db.order.Add(new Order { id = 1, code = "WT_ZL_261028_00001", type = "租赁", shop = shop, valid = 1,
            create_date = new DateTime(2026, 10, 28) });
        db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = 1, guaranty = paid });
        db.rentalDetail.Add(new RentalDetail { id = 3, rental_id = 2, charge_type = "租金", amount = rent, valid = 1 });
        db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, amount = paid, pay_method = payMethod,
            status = "支付成功", valid = 1, out_trade_no = "WT_ZL_261028_00001_ZF_01" });
        if (refunded > 0)
        {
            db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { id = 5, order_id = 1, payment_id = 4,
                amount = refunded, refund_id = "refunded" });
        }
        db.guaranty.Add(new Guaranty { id = 6, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = paid });
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 6, payment_id = 4 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
