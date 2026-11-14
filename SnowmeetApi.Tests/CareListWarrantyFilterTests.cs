using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护订单列表筛选里的「质保」：选了以后列出所有订单图标带「质」字的订单（Order.haveWarranty，任一件有效装备 warranty = true），
/// 对应接口参数 haveWarranty=true。不选（null）时不按质保筛；质保与招待互不影响（既质保又招待的订单两边都能查到）。
/// 只读，不改任何订单。
/// </summary>
public class CareListWarrantyFilterTests
{
    private const string Wanlong = "万龙服务中心";
    private static readonly DateTime Now = DateTime.Now;

    private static async Task<int> AddOrder(CoachFixture f, params Care[] cares)
    {
        var o = new Order { type = "养护", shop = Wanlong, valid = 1, is_test = 0, member_id = CoachFixture.StudentA, biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
        foreach (var c in cares)
        {
            c.valid = 1; c.create_date = Now.AddHours(-1);
            if (string.IsNullOrEmpty(c.equipment)) c.equipment = "双板";
            o.cares.Add(c);
        }
        f.Db.order.Add(o);
        await f.Db.SaveChangesAsync();
        return o.id;
    }

    private static Care Plain(Action<Care>? tweak = null)
    {
        var c = new Care { equipment = "双板", need_edge = 1, need_wax = 1, biz_type = "雪季养护", common_charge = 230 };
        tweak?.Invoke(c);
        return c;
    }

    private static async Task<List<SnowmeetApi.Models.Order>> List(CoachFixture f, bool? haveWarranty, bool? isEntertain = null)
    {
        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, Now.AddDays(-30).Date, Now.Date, "session-7", null,
            isEntertain: isEntertain, haveWarranty: haveWarranty, pageIndex: 1, pageSize: 50);
        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items;
    }

    [Fact]
    public async Task HaveWarrantyTrue_ListsOnlyOrdersWithAWarrantyBoard()
    {
        await using var f = await CoachFixture.Create();
        int plain = await AddOrder(f, Plain());
        int warrantyOnly = await AddOrder(f, Plain(c => { c.warranty = true; c.common_charge = 0; }));
        int mixed = await AddOrder(f, Plain(), Plain(c => { c.warranty = true; c.common_charge = 0; }));   // 多件装备里有一件质保，订单图标也带「质」
        int entertain = await AddOrder(f, Plain(c => { c.entertain = true; c.common_charge = 0; }));

        var items = await List(f, haveWarranty: true);

        Assert.Equal(new[] { warrantyOnly, mixed }.OrderBy(i => i), items.Select(o => o.id).OrderBy(i => i));
        Assert.All(items, o => Assert.True(o.haveWarranty));   // 列出来的订单图标上确实有「质」
        Assert.DoesNotContain(items, o => o.id == plain || o.id == entertain);
    }

    [Fact]
    public async Task HaveWarrantyNull_DoesNotFilterByWarranty()
    {
        await using var f = await CoachFixture.Create();
        int plain = await AddOrder(f, Plain());
        int warranty = await AddOrder(f, Plain(c => { c.warranty = true; c.common_charge = 0; }));

        var items = await List(f, haveWarranty: null);

        Assert.Equal(new[] { plain, warranty }.OrderBy(i => i), items.Select(o => o.id).OrderBy(i => i));
    }

    [Fact]
    public async Task WarrantyAndEntertain_AreIndependent()
    {
        await using var f = await CoachFixture.Create();
        int both = await AddOrder(f, Plain(c => { c.warranty = true; c.entertain = true; c.common_charge = 0; }));
        int entertainOnly = await AddOrder(f, Plain(c => { c.entertain = true; c.common_charge = 0; }));

        Assert.Equal(new[] { both }, (await List(f, haveWarranty: true)).Select(o => o.id).ToArray());
        Assert.Equal(new[] { both, entertainOnly }.OrderBy(i => i), (await List(f, haveWarranty: null, isEntertain: true)).Select(o => o.id).OrderBy(i => i));
    }

    [Fact]
    public async Task WarrantyBoardThatWasRemoved_DoesNotMakeTheOrderAWarrantyOrder()
    {
        await using var f = await CoachFixture.Create();
        var warrantyBoard = Plain(c => { c.warranty = true; c.common_charge = 0; });
        int orderId = await AddOrder(f, Plain(), warrantyBoard);
        Assert.Contains(await List(f, haveWarranty: true), o => o.id == orderId);   // 作废之前它是质保订单

        // 作废那件质保装备（valid = 0）：订单图标上没有「质」，也就不该在质保列表里
        warrantyBoard.valid = 0;
        await f.Db.SaveChangesAsync();
        Assert.Equal(0, f.NewContext().care.Single(c => c.id == warrantyBoard.id).valid);   // 确认真的存进去了

        Assert.DoesNotContain(await List(f, haveWarranty: true), o => o.id == orderId);
    }
}
