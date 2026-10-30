using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;

namespace SnowmeetApi.Tests;

/// <summary>
/// 教练引流平台测试夹具：共享缓存的内存 SQLite（多个 DbContext 可并发，用来测并发领取）。
/// 成员：1 管理员（staff 9，职级 300）、2 张教练、3/4/5 学员、6 李教练、7 普通店员（职级 100）。
/// 门店：万龙服务中心(1)、崇礼旗舰店(2)、南山(3)；养护商品按店配价。
/// </summary>
internal sealed class CoachFixture : IAsyncDisposable
{
    public const int Admin = 1, CoachMember = 2, StudentA = 3, StudentB = 4, StudentC = 5, CoachMember2 = 6, Clerk = 7;

    public SqliteConnection Connection = new("Data Source=coach-" + Guid.NewGuid().ToString("N") + ";Mode=Memory;Cache=Shared;Foreign Keys=False");
    public ApplicationDBContext Db = null!;
    public IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Settings:AppId"] = "test-app", ["Settings:AppSecret"] = "test-secret" }).Build();
    public int CoachId { get; private set; }
    public int Coach2Id { get; private set; }

    public static async Task<CoachFixture> Create(bool withPricing = true)
    {
        var f = new CoachFixture();
        await f.Connection.OpenAsync();
        f.Db = f.NewContext();
        await f.Db.Database.EnsureCreatedAsync();
        for (int id = 1; id <= 7; id++)
        {
            f.Db.member.Add(new Member { id = id, real_name = id == 2 ? "张教练" : id == 6 ? "李教练" : id >= 3 && id <= 5 ? "学员" + (char)('A' + id - 3) : "成员" + id, following_wechat = 0 });
            f.Db.memberSocialAccount.Add(new MemberSocialAccount { member_id = id, type = "wechat_mini_openid", num = "mini-" + id, valid = 1 });
            f.Db.memberSocialAccount.Add(new MemberSocialAccount { member_id = id, type = "cell", num = "1380000" + (1000 + id), valid = 1 });
            f.Db.miniSession.Add(new MiniSession { session_key = "session-" + id, session_type = "wechat_mini_openid", member_id = id,
                wechat_openid = "mini-" + id, valid = 1, expire_date = DateTime.Now.AddHours(2) });
        }
        // 没有会员的新用户会话（还没授权手机号）
        f.Db.miniSession.Add(new MiniSession { session_key = "session-new", session_type = "wechat_mini_openid", member_id = null,
            wechat_openid = "mini-new", valid = 1, expire_date = DateTime.Now.AddHours(2) });
        f.Db.staff.Add(new Staff { id = 9, name = "管理员", title_level = 300, valid = 1 });
        f.Db.staff.Add(new Staff { id = 10, name = "店员", title_level = 100, valid = 1 });
        f.Db.socialAccountForJob.Add(new SocialAccountForJob { id = 9, member_id = Admin, wechat_mini_openid = "mini-1" });
        f.Db.socialAccountForJob.Add(new SocialAccountForJob { id = 10, member_id = Clerk, wechat_mini_openid = "mini-7" });
        f.Db.staffSocialAccount.Add(new StaffSocialAccount { staff_id = 9, social_account_id = 9, valid = 1, start_date = DateTime.Now.AddDays(-2) });
        f.Db.staffSocialAccount.Add(new StaffSocialAccount { staff_id = 10, social_account_id = 10, valid = 1, start_date = DateTime.Now.AddDays(-2) });
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 12, name = "免费打蜡券", memo = "说明", type = "养护券", miniapp_recept_path = "", sharable = 1, valid = 1 });
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 16, name = "老顾客券", memo = "说明", type = "养护券", miniapp_recept_path = "", sharable = 1, valid = 1 });
        // 非 12/16、且 sharable=0：普通顾客不能转赠，教练券可以
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 99, name = "教练专属券", memo = "说明", type = "养护券", miniapp_recept_path = "", sharable = 0, valid = 1 });
        f.Db.shop.Add(new Shop { id = 1, name = "万龙服务中心", code = "wl", care = 1 });
        f.Db.shop.Add(new Shop { id = 2, name = "崇礼旗舰店", code = "cl", care = 1 });
        f.Db.shop.Add(new Shop { id = 3, name = "南山", code = "ns", care = 1 });
        f.Db.category.Add(new Category { id = 20, code = "0203", name = "养护服务", biz_type = "养护", valid = 1 });
        int pid = 200;
        void Product(int shop, string name, double price) =>
            f.Db.product.Add(new Product { id = pid++, shop_id = shop, name = name, category_code = "0203", category_id = 20, sale_price = price, valid = 1, on_shelves = 1 });
        Product(1, "单项", 120); Product(1, "双项", 168); Product(1, "单项加急", 180); Product(1, "双项加急", 228);
        Product(2, "单项", 138); Product(2, "双项", 188); // 崇礼没有加急商品：加急单回退非加急价
        Product(3, "单项", 100); Product(3, "双项", 150);
        var coach = new Coach { member_id = CoachMember, display_name = "张教练", created_by_staff_id = 9 };
        var coach2 = new Coach { member_id = CoachMember2, display_name = "李教练", created_by_staff_id = 9 };
        f.Db.coach.Add(coach); f.Db.coach.Add(coach2);
        await f.Db.SaveChangesAsync();
        f.CoachId = coach.id; f.Coach2Id = coach2.id;
        if (withPricing) await f.Admin_().SetPricingAsync(f.CoachId, Standard());
        f.Db.ChangeTracker.Clear();
        return f;
    }

    /// <summary>标准价格：万龙 单8折/双8折；崇礼 单一口价68/双一口价128；租赁9折。</summary>
    public static PricingInput Standard() => new(new()
    {
        new("万龙服务中心", "单项", "rate", 0.8), new("万龙服务中心", "双项", "rate", 0.8),
        new("崇礼旗舰店", "单项", "fixed", 68), new("崇礼旗舰店", "双项", "fixed", 128),
    }, 0.9);

    public ApplicationDBContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDBContext>()
        .UseSqlite(Connection.ConnectionString).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);

    public CoachAdminService Admin_() => new(NewContext(), Config);
    public CoachCardService Cards() => new(NewContext(), Config);
    public CoachPricingService Pricing() => new(NewContext());
    public CoachQueryService Query() => new(NewContext());

    /// <summary>建一张待领取的卡并让 memberId 领取，返回已领取的卡。</summary>
    public async Task<CoachCard> IssueAndClaim(int memberId, int? coachId = null)
    {
        var svc = Cards();
        CoachCard card = await svc.CreateCardAsync(coachId ?? CoachId, DateTime.Now);
        var r = await Cards().ClaimAsync(card.token, memberId, DateTime.Now);
        Assert.Equal(ShareCardState.ClaimedByMe, r.state);
        return r.card!;
    }

    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
}
