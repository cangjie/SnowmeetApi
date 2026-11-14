using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

/// <summary>
/// 门店筛选支持逗号分隔的多个店名（2026-11-14，小程序「区域=万龙 + 全部门店」联合查询万龙体验中心和万龙服务中心）。
/// 兼容约定：旧客户端发单个店名或不带 shop，结果必须和改之前完全一样；多个店名只是「或」。
/// 覆盖 7 个查询接口：订单三种列表（GetCommonOrders，含跨店）、养护未取 / 未完成、租赁未归还、养护工作量（统计 / 明细 / 快照）。
/// </summary>
public class ShopFilterTests
{
    private const string WlService = "万龙服务中心";
    private const string WlExperience = "万龙体验中心";
    private const string Flagship = "崇礼旗舰店";
    private const string Nanshan = "南山";
    private static readonly string[] AllShops = { WlService, WlExperience, Flagship, Nanshan };
    private static readonly DateTime Now = DateTime.Now;
    private static HttpContextAccessor Http() => new();

    // ───────────── 解析 ─────────────

    [Fact]
    public void Parse_NullMeansAllShops() => Assert.Null(ShopFilter.Parse(null));

    [Fact]
    public void Parse_SingleNameIsAOneElementListLikeTheOldExactMatch()
    {
        Assert.Equal(new[] { WlService }, ShopFilter.Parse(WlService)!);
        Assert.Equal(new[] { WlService }, ShopFilter.Parse("  " + WlService + " ")!);
    }

    [Fact]
    public void Parse_EmptyStringStaysAnExactMatchOfNothing_NotAllShops()
    {
        // 旧谓词 o.shop.Trim().Equals(shop.Trim())：shop = "" 时一家都匹配不上；保持，不悄悄变成「全部」
        Assert.Equal(new[] { "" }, ShopFilter.Parse("")!);
    }

    [Fact]
    public void Parse_SplitsOnAsciiAndFullWidthCommasTrimsAndDeduplicates()
    {
        Assert.Equal(new[] { WlExperience, WlService }, ShopFilter.Parse(WlExperience + ", " + WlService + "，" + WlExperience)!);
        Assert.Equal(new[] { WlService }, ShopFilter.Parse(WlService + ",,")!);
    }

    // ───────────── 订单列表（GetCommonOrders 经 GetOrdersByStaffPaged） ─────────────

    private static async Task SeedOrders(CoachFixture f)
    {
        foreach (string shop in AllShops)
        {
            foreach (string type in new[] { "养护", "租赁", "零售" })
            {
                var o = new Order { type = type, shop = shop, valid = 1, is_test = 0, member_id = CoachFixture.StudentA,
                    biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
                if (type == "养护")
                    o.cares.Add(new Care { equipment = "双板", valid = 1, create_date = Now.AddHours(-1), need_edge = 1, biz_type = "雪季养护", common_charge = 100 });
                f.Db.order.Add(o);
            }
        }
        await f.Db.SaveChangesAsync();
    }

    private static async Task<List<string>> OrderShops(CoachFixture f, string type, string? shop, bool crossShopOnly = false)
    {
        var ctl = new OrderController(f.NewContext(), f.Config, Http());
        var result = await ctl.GetOrdersByStaffPaged(null, shop, type, null, Now.AddDays(-30).Date, Now.Date, "session-7", null,
            pageIndex: 1, pageSize: 50, crossShopOnly: crossShopOnly);
        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items.Select(o => o.shop.Trim()).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private static List<string> Sorted(params string[] shops) => shops.OrderBy(s => s, StringComparer.Ordinal).ToList();

    [Theory]
    [InlineData("养护")]
    [InlineData("租赁")]
    [InlineData("零售")]
    public async Task Orders_NoShopOrOneShopBehaveAsBefore_SeveralShopsAreAnOr(string type)
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedOrders(f);

        Assert.Equal(Sorted(AllShops), await OrderShops(f, type, null));                                       // 不带 = 全部
        Assert.Equal(Sorted(WlService), await OrderShops(f, type, WlService));                                 // 单店：和以前一样
        Assert.Equal(Sorted(Flagship), await OrderShops(f, type, Flagship));
        Assert.Equal(Sorted(WlExperience, WlService), await OrderShops(f, type, WlExperience + "," + WlService));   // 万龙两家联合
        Assert.Equal(Sorted(WlExperience, WlService), await OrderShops(f, type, WlService + "，" + WlExperience));   // 全角逗号（控制器的 UrlDecode 会把空格变成 +，所以这里不带空格）
        Assert.Equal(Sorted(WlService, Nanshan), await OrderShops(f, type, WlService + "," + Nanshan));
        Assert.Empty(await OrderShops(f, type, "不存在的店"));
        Assert.Empty(await OrderShops(f, type, "不存在的店,另一个不存在的店"));
    }

    [Fact]
    public async Task CrossShopRentOrders_MatchWhenEitherTheOrderShopOrThePickupShopIsInTheList()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var order = new Order { type = "租赁", shop = Flagship, valid = 1, is_test = 0, member_id = CoachFixture.StudentA,
            biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
        var rental = new Rental { valid = 1, create_date = Now.AddHours(-1) };
        rental.rentItems.Add(new RentItem { valid = 1, class_name = "雪鞋", pick_type = "异店租赁", pickup_shop = WlExperience, create_date = Now.AddHours(-1) });
        order.rentals.Add(rental);
        f.Db.order.Add(order);
        await f.Db.SaveChangesAsync();

        Assert.Single(await OrderShops(f, "租赁", null, crossShopOnly: true));                                    // 不带 shop：所有异店单
        Assert.Single(await OrderShops(f, "租赁", WlExperience, crossShopOnly: true));                             // 领取店匹配（单店，和以前一样）
        Assert.Single(await OrderShops(f, "租赁", Flagship, crossShopOnly: true));                                 // 开单店匹配
        Assert.Single(await OrderShops(f, "租赁", WlExperience + "," + WlService, crossShopOnly: true));          // 万龙联合：领取店在其中
        Assert.Empty(await OrderShops(f, "租赁", WlService + "," + Nanshan, crossShopOnly: true));                  // 两边都不在
    }

    // ───────────── 养护未取 / 养护未完成 ─────────────

    private static async Task SeedCareLists(CoachFixture f)
    {
        foreach (string shop in AllShops)
        {
            foreach (bool pending in new[] { false, true })      // false：只差发板（未取）；true：还有工序没做完（未完成）
            {
                var o = new Order { type = "养护", shop = shop, valid = 1, is_test = 0, member_id = CoachFixture.StudentA,
                    biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
                var care = new Care { equipment = "双板", valid = 1, create_date = Now.AddHours(-1), task_flow_code = "F", biz_type = "雪季养护" };
                care.tasks.Add(new CareTask { task_name = "安全检查", status = "已完成", sort = 10, create_date = Now.AddHours(-1) });
                care.tasks.Add(new CareTask { task_name = "修刃", status = pending ? null : "已完成", sort = 20, create_date = Now.AddHours(-1) });
                care.tasks.Add(new CareTask { task_name = "发板", sort = 60, create_date = Now.AddHours(-1) });
                o.cares.Add(care);
                f.Db.order.Add(o);
            }
        }
        await f.Db.SaveChangesAsync();
    }

    private static async Task<List<string>> UnpickedShops(CoachFixture f, string? shop)
    {
        var result = await new CareController(f.NewContext(), f.Config, Http())
            .GetUnpickedCareItemsByStaff(shop, null, null, null, "session-7", pageIndex: 1, pageSize: 50);
        var api = (ApiResult<CareController.PagedCareItemResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items.Select(c => c.order.shop.Trim()).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    private static async Task<List<string>> IncompleteShops(CoachFixture f, string? shop)
    {
        var result = await new CareController(f.NewContext(), f.Config, Http())
            .GetIncompleteCareItemsByStaff(shop, null, null, null, "session-7", pageIndex: 1, pageSize: 50);
        var api = (ApiResult<CareController.PagedCareProgressItemResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.items.Select(c => c.shop.Trim()).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task UnpickedCareList_NoShopOneShopAndSeveralShops()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedCareLists(f);
        Assert.Equal(Sorted(AllShops), await UnpickedShops(f, null));
        Assert.Equal(Sorted(WlService), await UnpickedShops(f, WlService));
        Assert.Equal(Sorted(WlExperience, WlService), await UnpickedShops(f, WlExperience + "," + WlService));
        Assert.Empty(await UnpickedShops(f, "不存在的店"));
    }

    [Fact]
    public async Task IncompleteCareList_NoShopOneShopAndSeveralShops()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedCareLists(f);
        Assert.Equal(Sorted(AllShops), await IncompleteShops(f, null));
        Assert.Equal(Sorted(WlService), await IncompleteShops(f, WlService));
        Assert.Equal(Sorted(WlExperience, WlService), await IncompleteShops(f, WlExperience + "," + WlService));
        Assert.Empty(await IncompleteShops(f, "不存在的店"));
    }

    // ───────────── 租赁未归还 ─────────────

    private static async Task SeedUnreturned(CoachFixture f)
    {
        foreach (string shop in AllShops)
        {
            var o = new Order { type = "租赁", shop = shop, valid = 1, is_test = 0, member_id = CoachFixture.StudentA,
                biz_date = Now.AddHours(-1), create_date = Now.AddHours(-1), pay_option = "普通" };
            var rental = new Rental { valid = 1, create_date = Now.AddHours(-1) };
            var item = new RentItem { valid = 1, name = "雪板 " + shop, class_name = "雪板", category_id = 31, create_date = Now.AddHours(-1) };
            item.logs.Add(new RentItemLog { status = "已发放", valid = 1, create_date = Now.AddMinutes(-30) });
            rental.rentItems.Add(item);
            o.rentals.Add(rental);
            f.Db.order.Add(o);
        }
        await f.Db.SaveChangesAsync();
    }

    private static async Task<List<string>> UnreturnedShops(CoachFixture f, string? shop)
    {
        var result = await new RentController(f.NewContext(), f.Config, Http()).GetUnReturnedRentItemsByStaff(shop, "session-7");
        var api = (ApiResult<List<CategoryRentItem>>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        return api.data!.SelectMany(c => c.items).Select(i => i.rental.order.shop.Trim()).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public async Task UnreturnedRentItems_NoShopOneShopAndSeveralShops()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        await SeedUnreturned(f);
        Assert.Equal(Sorted(AllShops), await UnreturnedShops(f, null));
        Assert.Equal(Sorted(WlService), await UnreturnedShops(f, WlService));
        Assert.Equal(Sorted(WlExperience, WlService), await UnreturnedShops(f, WlExperience + "," + WlService));
        Assert.Empty(await UnreturnedShops(f, "不存在的店"));
    }

    // ───────────── 养护工作量 ─────────────

    private static readonly DateTime Day = new(2026, 11, 10);
    private static DateTime At(int day, int hour) => new(2026, 11, day, hour, 0, 0);
    private static readonly DateTime NightNow = new(2026, 11, 10, 23, 30, 0);
    private static int nextOrder = 9100;

    private static void AddBoard(CoachFixture f, string shop, DateTime entered)
    {
        var order = new Order { id = ++nextOrder, type = "养护", shop = shop, valid = 1, is_test = 0, biz_date = entered, create_date = entered, pay_option = "普通" };
        var care = new Care { equipment = "双板", valid = 1, biz_type = "", create_date = entered, task_flow_code = "F" };
        care.tasks.Add(new CareTask { task_name = "安全检查", create_date = entered });
        care.tasks.Add(new CareTask { task_name = "发板", create_date = entered });
        order.cares.Add(care);
        f.Db.order.Add(order);
    }

    [Fact]
    public async Task Workload_SeveralShopsAreMergedLikeTheAllShopsViewButOnlyForThoseShops()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        AddBoard(f, WlService, At(10, 9)); AddBoard(f, WlService, At(10, 10));
        AddBoard(f, WlExperience, At(10, 9));
        AddBoard(f, Flagship, At(10, 9));
        await f.Db.SaveChangesAsync();
        var service = new CareWorkloadService(f.NewContext());

        var one = await service.ComputeAsync(Day, NightNow, WlService);                                    // 单店：和以前一样
        Assert.Equal((0, 2, 0, 2), (one.initialPending, one.added, one.completed, one.pending));
        Assert.Equal(WlService, one.shop);

        var both = await service.ComputeAsync(Day, NightNow, WlExperience + "," + WlService);
        Assert.Equal((0, 3, 0, 3), (both.initialPending, both.added, both.completed, both.pending));       // 不含崇礼那 1 块
        Assert.Equal(WlExperience + "," + WlService, both.shop);

        var all = await service.ComputeAsync(Day, NightNow);
        Assert.Equal(4, all.added);
        Assert.Null(all.shop);

        var items = await service.GetItemsAsync(Day, NightNow, WlExperience + "," + WlService, CareWorkloadRules.MetricAdded);
        Assert.Equal(3, items.total);
        Assert.All(items.items, i => Assert.NotEqual(Flagship, i.shop));
    }

    [Fact]
    public async Task Workload_SnapshotOfSeveralShopsMergesOnlyThoseRows()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        AddBoard(f, WlService, At(10, 9)); AddBoard(f, WlExperience, At(10, 9)); AddBoard(f, WlExperience, At(10, 10)); AddBoard(f, Flagship, At(10, 9));
        await f.Db.SaveChangesAsync();
        Assert.Equal(3, await new CareWorkloadService(f.NewContext()).SaveSnapshotAsync(Day, NightNow));   // 三家各一行

        var service = new CareWorkloadService(f.NewContext());
        var one = await service.GetSnapshotAsync(Day, WlExperience);
        Assert.Equal(2, one!.added);
        var both = await service.GetSnapshotAsync(Day, WlService + "," + WlExperience);
        Assert.Equal(3, both!.added);
        Assert.Equal("snapshot", both.source);
        Assert.Equal(WlService + "," + WlExperience, both.shop);
        var none = await service.GetSnapshotAsync(Day, Nanshan + "," + "别的店");                         // 那天存过但这几家没有行 = 全 0
        Assert.Equal(0, none!.added);
    }
}
