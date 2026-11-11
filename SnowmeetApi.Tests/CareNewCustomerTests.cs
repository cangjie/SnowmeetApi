using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护订单列表的「新」标识（取代原来的「会 / 散」）：该顾客第一次下「有效」的养护订单，这一单标「新」。
/// 有效养护订单 = order.valid=1、不是测试单、至少有一件有效装备（没装备的是「临时订单」，装备被删的也不算）。
/// 顾客按会员 id 认；第一单按开单时间（biz_date）取最早，时间相同取 id 小的；
/// 判断要看这个顾客的全部历史订单，不能只看列表里查出来的那一页 / 那个日期范围（读 member 表上记着的养护首单，见 CareFirstOrderTests）。
/// </summary>
public class CareNewCustomerTests
{
    private static Order CareOrder(int id, int? memberId, DateTime bizDate, int valid = 1, int isTest = 0, int validCares = 1)
    {
        var o = new Order { id = id, type = "养护", member_id = memberId, biz_date = bizDate, valid = valid, is_test = isTest };
        for (int i = 0; i < validCares; i++) o.cares.Add(new Care { id = id * 10 + i, equipment = "双板", valid = 1 });
        return o;
    }

    [Fact]
    public void ValidCareOrder_NeedsValidFlagNotTestAndAtLeastOneValidCare()
    {
        var day = new DateTime(2026, 11, 1);
        Assert.True(CareNewCustomerRules.IsValidCareOrder(CareOrder(1, 3, day)));
        Assert.False(CareNewCustomerRules.IsValidCareOrder(CareOrder(2, 3, day, valid: 0)));
        Assert.False(CareNewCustomerRules.IsValidCareOrder(CareOrder(3, 3, day, isTest: 1)));
        Assert.False(CareNewCustomerRules.IsValidCareOrder(CareOrder(4, 3, day, validCares: 0)));          // 临时订单：没有装备
        var cancelledOnly = CareOrder(5, 3, day, validCares: 0);
        cancelledOnly.cares.Add(new Care { id = 50, equipment = "双板", valid = 0 });                      // 装备被删
        Assert.False(CareNewCustomerRules.IsValidCareOrder(cancelledOnly));
        var rent = CareOrder(6, 3, day); rent.type = "租赁";
        Assert.False(CareNewCustomerRules.IsValidCareOrder(rent));
    }

    [Fact]
    public void Mark_FlagsOnlyTheFirstValidOrder_AndNeverOrdersWithoutMemberOrInvalidOnes()
    {
        var d = new DateTime(2026, 11, 1);
        var first = new Dictionary<int, int> { [3] = 20, [4] = 40 };
        var page = new List<Order>
        {
            CareOrder(20, 3, d),                          // 3 号的第一单 → 新
            CareOrder(30, 3, d.AddDays(1)),               // 3 号的第二单
            CareOrder(40, 4, d),                          // 4 号的第一单 → 新
            CareOrder(41, null, d),                       // 没有会员：认不出顾客，不标
            CareOrder(42, 4, d, validCares: 0),           // 自己不是有效订单，即使 id 在字典里也不标
        };
        CareNewCustomerRules.Mark(page, first);
        Assert.Equal(new[] { 20, 40 }, page.Where(o => o.isNewCareCustomer).Select(o => o.id));
    }

    [Fact]
    public async Task GetOrdersByStaffPaged_Care_MarksTheFirstValidOrderOfEachCustomer_UsingFullHistory()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        void Add(int id, int? member, int daysAgo, int isTest = 0, int validCares = 1, int valid = 1)
        {
            var o = new Order
            {
                id = id, type = "养护", shop = "万龙服务中心", valid = valid, is_test = isTest, member_id = member,
                biz_date = now.AddDays(-daysAgo), create_date = now.AddDays(-daysAgo), pay_option = "普通"
            };
            for (int i = 0; i < validCares; i++) o.cares.Add(new Care { equipment = "双板", valid = 1, biz_type = "", create_date = now.AddDays(-daysAgo) });
            if (validCares == 0 && id == 8010) o.cares.Add(new Care { equipment = "双板", valid = 0, biz_type = "", create_date = now.AddDays(-daysAgo) });
            f.Db.order.Add(o);
        }
        Add(8001, CoachFixture.StudentA, 10);                  // A 的第一单 → 新
        Add(8002, CoachFixture.StudentA, 1);                   // A 的第二单
        Add(8003, CoachFixture.StudentB, 5, isTest: 1);        // B 的测试单：不算有效订单
        Add(8004, CoachFixture.StudentB, 2);                   // B 第一次下有效订单 → 新
        Add(8005, CoachFixture.StudentC, 4, validCares: 0);    // C 的临时订单（没装备）
        Add(8006, CoachFixture.StudentC, 3);                   // C 第一次下有效订单 → 新
        Add(8007, null, 1);                                    // 没有会员
        Add(8008, CoachFixture.CoachMember2, 100);             // 100 天前，不在查询的日期范围内
        Add(8009, CoachFixture.CoachMember2, 1);               // 在范围内，但不是第一单（看全部历史）
        Add(8010, CoachFixture.CoachMember, 6, validCares: 0); // 装备被删的订单
        Add(8011, CoachFixture.CoachMember, 2);                // 第一次下有效订单 → 新
        await f.Db.SaveChangesAsync();

        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, now.AddDays(-30).Date, now.Date, "session-7", null, pageIndex: 1, pageSize: 50);

        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        var byId = api.data!.items.ToDictionary(o => o.id);
        Assert.False(byId.ContainsKey(8008));    // 确认 8008 确实不在这页里，下面才是「看全部历史」的有效证明
        Assert.Equal(new[] { 8001, 8004, 8006, 8011 }, byId.Values.Where(o => o.isNewCareCustomer).Select(o => o.id).OrderBy(i => i));
    }
}
