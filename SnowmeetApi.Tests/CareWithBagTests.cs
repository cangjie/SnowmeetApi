using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护「含包」：care.with_bag（bit NULL，含雪包）和早就有的 care.with_pole（含雪杖）并列。
/// 含包、含杖二选一（小程序开单、详情页保证）；后端只负责把这两个标记存下来、列表照常带出，不改任何别的行为。
/// 加列脚本 2026-11-14_care_with_bag.sql 要和实体对得上：先建列、再部署读写它的后端。
/// </summary>
public class CareWithBagTests
{
    private const string Wanlong = "万龙服务中心";
    private static readonly DateTime Now = DateTime.Now;

    private static async Task<int> AddOrder(CoachFixture f, params Care[] cares)
    {
        var o = new Order { type = "养护", shop = Wanlong, valid = 1, is_test = 0, member_id = CoachFixture.StudentA, biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
        foreach (var c in cares) { c.valid = 1; c.create_date = Now.AddHours(-1); o.cares.Add(c); }
        f.Db.order.Add(o);
        await f.Db.SaveChangesAsync();
        return o.id;
    }

    private static Care Plain(string equipment, bool? bag, bool? pole) =>
        new Care { equipment = equipment, need_edge = 1, need_wax = 1, biz_type = "雪季养护", common_charge = 230, with_bag = bag, with_pole = pole };

    [Fact]
    public async Task BagAndPoleFlagsAreStoredAndComeBackInTheCareOrderList()
    {
        await using var f = await CoachFixture.Create();
        int orderId = await AddOrder(f,
            Plain("双板", true, null),     // 含包
            Plain("双板", null, true),     // 含杖
            Plain("单板", true, null),     // 单板含包
            Plain("双板", null, null));    // 都没带（历史数据都是这样：NULL）
        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, Now.AddDays(-30).Date, Now.Date, "session-7", null, pageIndex: 1, pageSize: 50);
        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        var cares = api.data!.items.Single(o => o.id == orderId).cares.OrderBy(c => c.id).ToList();
        Assert.Equal(new (bool?, bool?)[] { (true, null), (null, true), (true, null), (null, null) },
            cares.Select(c => (c.with_bag, c.with_pole)).ToArray());
    }

    [Fact]
    public async Task UpdatingTheFlagsOnAStoredCareSticks()
    {
        await using var f = await CoachFixture.Create();
        int orderId = await AddOrder(f, Plain("双板", null, null));
        var stored = f.NewContext().care.Single(c => c.order_id == orderId);
        Assert.Null(stored.with_bag);
        // 改成含包、再改成含杖（二选一由小程序保证：改一个时另一个同时清掉）
        using (var db = f.NewContext())
        {
            var c = db.care.Single(x => x.order_id == orderId);
            c.with_bag = true; c.with_pole = false;
            db.care.Update(c);
            await db.SaveChangesAsync();
        }
        var afterBag = f.NewContext().care.Single(c => c.order_id == orderId);
        Assert.Equal((true, false), (afterBag.with_bag, afterBag.with_pole));
        using (var db = f.NewContext())
        {
            var c = db.care.Single(x => x.order_id == orderId);
            c.with_bag = false; c.with_pole = true;
            db.care.Update(c);
            await db.SaveChangesAsync();
        }
        var afterPole = f.NewContext().care.Single(c => c.order_id == orderId);
        Assert.Equal((false, true), (afterPole.with_bag, afterPole.with_pole));
    }

    [Fact]
    public void SchemaScript_AddsTheNullableBitColumnTheModelMaps()
    {
        string? script = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); script == null && dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "snowmeet_ai_doc", "sql", "2026-11-14_care_with_bag.sql");
            if (File.Exists(candidate)) script = candidate;
        }
        Assert.True(script != null, "需要相邻 snowmeet_ai_doc 仓库里的 sql/2026-11-14_care_with_bag.sql");
        string sql = File.ReadAllText(script!);

        var property = typeof(Care).GetProperty("with_bag")!;
        Assert.Equal(typeof(bool?), property.PropertyType);          // 可空 bool ↔ bit NULL，和 with_pole 一致
        Assert.Equal(typeof(bool?), typeof(Care).GetProperty("with_pole")!.PropertyType);
        Assert.Matches(@"ALTER\s+TABLE\s+dbo\.care\s+ADD\s+with_bag\s+bit\s+NULL", sql);
        Assert.Contains("COL_LENGTH(N'dbo.care', N'with_bag')", sql);  // 脚本可重复执行
        Assert.DoesNotMatch(@"(?i)\bUPDATE\b|\bDELETE\b|\bDROP\b", sql);  // 只加列，不动任何已有数据
    }
}
