using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

// 介绍人分账各组测试共用的造数据：SQLite 内存库、一张已关单的万龙租赁单、介绍人。
internal static class ShareFixture
{
    public static readonly IConfiguration Config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Settings:AppId"] = "local-test-app" }).Build();
    public static readonly IHttpContextAccessor Http = new HttpContextAccessor();

    public static async Task<ApplicationDBContext> NewDbAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;
        var db = new ApplicationDBContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    // 订单 id 1（万龙体验中心租赁，11-01 已关单），一笔付款 id 4，退款一条，租金明细一条。
    public static async Task<ApplicationDBContext> ClosedWanlongOrderAsync(string payMethod = "微信支付", double paid = 500,
        double refunded = 430, double rent = 70)
    {
        var db = await NewDbAsync();
        db.order.Add(new Order { id = 1, code = "WT_ZL_261031_00001", type = "租赁", shop = "万龙体验中心", valid = 1, closed = 1,
            close_date = new DateTime(2026, 11, 1, 15, 42, 0), create_date = new DateTime(2026, 10, 31) });
        db.rental.Add(new Rental { id = 2, order_id = 1, valid = 1, settled = 1, guaranty = paid });
        db.rentalDetail.Add(new RentalDetail { id = 3, rental_id = 2, charge_type = "租金", amount = rent, valid = 1 });
        db.guaranty.Add(new Guaranty { id = 6, order_id = 1, biz_type = "租赁", biz_id = 2, valid = 1, amount = paid });
        await db.SaveChangesAsync();
        await AddPaymentAsync(db, 4, paid, payMethod, refunded);
        return db;
    }

    // 给订单 1 再加一笔成功付款，refunded > 0 时带一条已成功的退款。
    public static async Task AddPaymentAsync(ApplicationDBContext db, int id, double amount, string payMethod = "微信支付",
        double refunded = 0)
    {
        db.orderPayment.Add(new OrderPayment { id = id, order_id = 1, amount = amount, pay_method = payMethod, status = "支付成功",
            valid = 1, out_trade_no = "WT_ZL_261031_00001_ZF_" + (id - 3).ToString().PadLeft(2, '0') });
        if (refunded > 0)
        {
            db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { id = id + 100, order_id = 1, payment_id = id,
                amount = refunded, refund_id = "refunded-" + id });
        }
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = 6, payment_id = id });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    // 建会员、小程序 openid（openid 为 null 则不建）、收款方账号、介绍人；aliBound 为真再补一条支付宝绑定记录。
    public static async Task<Introducer> AddIntroducerAsync(ApplicationDBContext db, int memberId = 100, string type = "KOL",
        string? openid = "oINTRO", string? aliType = null, string? aliNum = null, bool aliBound = false)
    {
        db.member.Add(new Member { id = memberId, real_name = "示例" + memberId });
        if (openid != null)
        {
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId,
                type = MemberSocialAccount.TYPE_WECHAT_MINI_OPENID, num = openid, valid = 1 });
        }
        var relation = new OrderShareRelation { type = type == "机构" ? "公司" : "个人", name = "示例" + memberId, valid = true,
            wepay_account_type = openid == null ? null : "PERSONAL_OPENID", wepay_account_num = openid,
            ali_account_type = aliType, ali_account_num = aliNum };
        db.orderShareRelation.Add(relation);
        await db.SaveChangesAsync();
        if (aliBound)
        {
            db.shareRelationBind.Add(new ShareRelationBind { share_relation_id = relation.id, pay_method = "支付宝", valid = true });
        }
        var introducer = new Introducer { member_id = memberId, type = type, name = "示例" + memberId, relation_id = relation.id };
        db.introducer.Add(introducer);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return introducer;
    }

    public static Staff Manager(int level = 200) => new Staff { id = 1, name = "店长", valid = 1, title_level = level };

    // 造一个员工和他的小程序会话，供控制器测试按 sessionKey 识别身份（做法同 CoachFixture）。
    public static async Task AddStaffSessionAsync(ApplicationDBContext db, int memberId, int level, string sessionKey)
    {
        db.member.Add(new Member { id = memberId, real_name = "员工" + memberId });
        db.miniSession.Add(new MiniSession { session_key = sessionKey, session_type = "wechat_mini_openid", member_id = memberId,
            wechat_openid = "mini-" + memberId, valid = 1, expire_date = DateTime.Now.AddHours(2) });
        db.staff.Add(new Staff { id = memberId + 1000, name = "员工" + memberId, title_level = level, valid = 1 });
        db.socialAccountForJob.Add(new SocialAccountForJob { id = memberId + 1000, member_id = memberId, wechat_mini_openid = "mini-" + memberId });
        db.staffSocialAccount.Add(new StaffSocialAccount { staff_id = memberId + 1000, social_account_id = memberId + 1000, valid = 1,
            start_date = DateTime.Now.AddDays(-2) });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }
}
