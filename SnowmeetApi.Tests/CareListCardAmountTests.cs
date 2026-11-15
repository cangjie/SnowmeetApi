using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护订单列表汇总行的「卡类零售」：养护购卡单（order_source = care_card_single）已卖出的卡款，
/// 来自它名下有效零售明细里不是「养护附加费用」的成交价；买卡的钱不在任何装备的金额里，
/// 单列一项才能和「共计收款」对上。只算已生效的单；普通养护单和草稿都是 null。只读，不改订单。
/// </summary>
public class CareListCardAmountTests
{
    private const string Wanlong = "万龙服务中心";
    private static readonly DateTime Now = DateTime.Now;

    private static async Task<int> AddOrder(CoachFixture f, bool cardSale, int dealed = 1, params Retail[] retails)
    {
        var o = new Order
        {
            type = "养护", shop = Wanlong, valid = 1, is_test = 0, member_id = CoachFixture.StudentA,
            biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通", dealed = dealed,
            sub_type = cardSale ? "养护购卡" : "", order_source = cardSale ? CareCardSaleService.SingleOrderSource : ""
        };
        o.cares.Add(new Care { equipment = "双板", need_edge = 1, need_wax = 1, biz_type = "雪季养护", common_charge = cardSale ? 0 : 230, valid = 1, create_date = Now.AddHours(-1) });
        foreach (var r in retails) o.retails.Add(r);
        f.Db.order.Add(o);
        await f.Db.SaveChangesAsync();
        return o.id;
    }

    private static Retail Card(double price, int valid = 1) => new Retail { retail_type = "养护卡", deal_price = price, valid = valid };
    private static Retail Extra(double price) => new Retail { retail_type = CareCardSaleService.CareChargeRetailType, deal_price = price, valid = 1 };

    private static async Task<List<Order>> List(CoachFixture f)
    {
        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, Now.AddDays(-30).Date, Now.Date, "session-7", null,
            pageIndex: 1, pageSize: 50);
        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items;
    }

    [Fact]
    public async Task CardSaleOrder_CarriesTheCardMoney_ButNotTheCareExtraCharge()
    {
        await using var f = await CoachFixture.Create();
        int sale = await AddOrder(f, true, 1, Card(1800), Extra(230));   // 买卡 1800，另有养护附加费用 230（属于养护，不算卡款）

        var items = await List(f);

        Assert.Equal(1800, items.Single(o => o.id == sale).careCardAmount);
    }

    [Fact]
    public async Task SeveralCardRows_AreAdded_AndInvalidRowsAreIgnored()
    {
        await using var f = await CoachFixture.Create();
        int sale = await AddOrder(f, true, 1, Card(1200), Card(1000), Card(500, valid: 0));

        var items = await List(f);

        Assert.Equal(2200, items.Single(o => o.id == sale).careCardAmount);
    }

    [Fact]
    public async Task PlainCareOrder_AndUndealtDraft_HaveNoCardAmount()
    {
        await using var f = await CoachFixture.Create();
        int plain = await AddOrder(f, false);
        int draft = await AddOrder(f, true, 0, Card(1800));   // 还没生效的购卡草稿：不算卖出
        int noRows = await AddOrder(f, true, 1);               // 购卡单但名下没有有效零售明细

        var items = await List(f);

        Assert.Null(items.Single(o => o.id == plain).careCardAmount);
        Assert.Null(items.Single(o => o.id == draft).careCardAmount);
        Assert.Null(items.Single(o => o.id == noRows).careCardAmount);
    }

    [Fact]
    public async Task MixedPage_OnlyCardSalesCarryAnAmount()
    {
        await using var f = await CoachFixture.Create();
        int plain = await AddOrder(f, false);
        int sale1 = await AddOrder(f, true, 1, Card(1800));
        int sale2 = await AddOrder(f, true, 1, Card(1200));

        var items = await List(f);

        Assert.Equal(new double?[] { null, 1800, 1200 }, new[] { plain, sale1, sale2 }.Select(id => items.Single(o => o.id == id).careCardAmount).ToArray());
        Assert.Equal(3000, items.Sum(o => o.careCardAmount ?? 0));
    }
}
