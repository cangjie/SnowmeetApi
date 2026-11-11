using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Tests;

/// <summary>
/// 「养护首单」落库：每个会员第一次下「有效」养护订单的那一单，记在 member 表的两个可空列上
/// （care_first_order_id 订单 id、care_first_order_date 开单时间）。
/// 列表的「新」标识、以后的报表都只读这一份，不再各自从订单表现算。
/// 这两列只通过 MemberCareFirstOrder（与 Member 共用 member 表的另一个映射）读写，Member 实体本身不带它们：
/// 会员的整行保存不会覆盖它们，会员的任何查询也不依赖它们是否已建。
/// 关键性质：
///   · 记录不依赖订单表一直保留旧单——旧单被归档 / 删除后，别的订单不会被误标成「新」；
///   · 首单后来作废（订单作废 / 变测试单 / 装备全删）时，「新」顺延给该会员的下一张有效订单；
///   · 缺记录的会员在第一次被读到时自动补上，所以不依赖一次性回填是否做全。
/// </summary>
public class CareFirstOrderTests
{
    private static readonly DateTime D = new(2026, 11, 1, 10, 0, 0);

    // ───────────── 纯规则：存下来的记录 vs 现算出的最早有效单，该怎么处理 ─────────────

    [Fact]
    public void Resolve_NoRecord_InsertsEarliestValidOrder_OrDoesNothing()
    {
        var cand = new FirstOrderPoint(20, D);
        Assert.Equal(new FirstOrderDecision(FirstOrderAction.Upsert, 20, D), CareFirstOrderRules.Resolve(null, StoredOrderState.Missing, cand));
        Assert.Equal(FirstOrderAction.Keep, CareFirstOrderRules.Resolve(null, StoredOrderState.Missing, null).Action);
    }

    [Fact]
    public void Resolve_RecordStillValid_IsKept_UnlessAnEarlierValidOrderAppeared()
    {
        var stored = new FirstOrderPoint(20, D);
        Assert.Equal(FirstOrderAction.Keep, CareFirstOrderRules.Resolve(stored, StoredOrderState.Valid, new FirstOrderPoint(20, D)).Action);
        Assert.Equal(FirstOrderAction.Keep, CareFirstOrderRules.Resolve(stored, StoredOrderState.Valid, new FirstOrderPoint(30, D.AddDays(1))).Action);
        // 后来补出来一张更早的有效单（时间更早，或时间相同 id 更小）→ 换成它
        Assert.Equal(new FirstOrderDecision(FirstOrderAction.Upsert, 10, D.AddHours(-1)),
            CareFirstOrderRules.Resolve(stored, StoredOrderState.Valid, new FirstOrderPoint(10, D.AddHours(-1))));
        Assert.Equal(new FirstOrderDecision(FirstOrderAction.Upsert, 19, D),
            CareFirstOrderRules.Resolve(stored, StoredOrderState.Valid, new FirstOrderPoint(19, D)));
    }

    [Fact]
    public void Resolve_RecordedOrderRowIsGone_KeepsTheRecord_SoHistoryIsNotRewritten()
    {
        var stored = new FirstOrderPoint(20, D);
        // 旧单被归档 / 删除：现算只能看到更晚的订单，但记录是「过去发生过」的事实，不能被改写
        Assert.Equal(FirstOrderAction.Keep, CareFirstOrderRules.Resolve(stored, StoredOrderState.Missing, new FirstOrderPoint(30, D.AddDays(3))).Action);
        Assert.Equal(FirstOrderAction.Keep, CareFirstOrderRules.Resolve(stored, StoredOrderState.Missing, null).Action);
        // 但如果表里还有比记录更早的有效单，说明记录本身错了，换成更早的
        Assert.Equal(FirstOrderAction.Upsert, CareFirstOrderRules.Resolve(stored, StoredOrderState.Missing, new FirstOrderPoint(10, D.AddDays(-2))).Action);
    }

    [Fact]
    public void Resolve_RecordedOrderNoLongerValid_MovesToTheNextValidOrder_OrIsDeleted()
    {
        var stored = new FirstOrderPoint(20, D);
        Assert.Equal(new FirstOrderDecision(FirstOrderAction.Upsert, 30, D.AddDays(1)),
            CareFirstOrderRules.Resolve(stored, StoredOrderState.Invalid, new FirstOrderPoint(30, D.AddDays(1))));
        Assert.Equal(FirstOrderAction.Delete, CareFirstOrderRules.Resolve(stored, StoredOrderState.Invalid, null).Action);
    }

    // ───────────── 服务：读 / 补 / 校正（SQLite 内存库） ─────────────

    private static void AddOrder(CoachFixture f, int id, int? member, DateTime biz, int valid = 1, int isTest = 0, int validCares = 1, string type = "养护")
    {
        var o = new Order { id = id, type = type, shop = "万龙服务中心", valid = valid, is_test = isTest, member_id = member, biz_date = biz, create_date = biz, pay_option = "普通" };
        for (int i = 0; i < validCares; i++) o.cares.Add(new Care { equipment = "双板", valid = 1, biz_type = "", create_date = biz });
        f.Db.order.Add(o);
    }

    private static async Task RemoveOrderRow(CoachFixture f, int id)
    {
        await f.NewContext().care.Where(c => c.order_id == id).ExecuteDeleteAsync();
        await f.NewContext().order.Where(o => o.id == id).ExecuteDeleteAsync();
    }

    // 现在记着首单的会员（两列都空的不算）
    private static async Task<List<MemberCareFirstOrder>> Stored(CoachFixture f) =>
        await f.NewContext().memberCareFirstOrder.AsNoTracking().Where(r => r.order_id != null).OrderBy(r => r.member_id).ToListAsync();

    [Fact]
    public async Task GetFirstOrderIds_CreatesTheRecordLazily_ForAMemberWhoHasNone()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9001, CoachFixture.StudentA, D.AddDays(-10));
        AddOrder(f, 9002, CoachFixture.StudentA, D.AddDays(-1));
        await f.Db.SaveChangesAsync();
        Assert.Empty(await Stored(f));

        var first = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        Assert.Equal(9001, first[CoachFixture.StudentA]);
        var rows = await Stored(f);
        Assert.Single(rows);
        Assert.Equal((CoachFixture.StudentA, (int?)9001), (rows[0].member_id, rows[0].order_id));
        Assert.Equal((DateTime?)D.AddDays(-10), rows[0].biz_date);
    }

    [Fact]
    public async Task GetFirstOrderIds_IgnoresInvalidOrders_AndWritesNothingForAMemberWithoutAValidOne()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9010, CoachFixture.StudentB, D.AddDays(-5), isTest: 1);       // 测试单
        AddOrder(f, 9011, CoachFixture.StudentB, D.AddDays(-4), validCares: 0);   // 临时订单（没装备）
        AddOrder(f, 9012, CoachFixture.StudentB, D.AddDays(-3), valid: 0);        // 未生效 / 已作废
        AddOrder(f, 9013, CoachFixture.StudentB, D.AddDays(-2), type: "租赁");    // 不是养护
        await f.Db.SaveChangesAsync();

        var first = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentB });

        Assert.False(first.ContainsKey(CoachFixture.StudentB));
        Assert.Empty(await Stored(f));
    }

    [Fact]
    public async Task GetFirstOrderIds_CalledTwice_StillOneRow()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9020, CoachFixture.StudentA, D.AddDays(-3));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA, CoachFixture.StudentA });
        Assert.Single(await Stored(f));
    }

    [Fact]
    public async Task Record_SurvivesWhenTheFirstOrderRowIsRemovedFromTheOrderTable()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9030, CoachFixture.StudentA, D.AddDays(-10));   // A 的第一单
        AddOrder(f, 9031, CoachFixture.StudentA, D.AddDays(-1));
        await f.Db.SaveChangesAsync();
        Assert.Equal(9030, (await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA }))[CoachFixture.StudentA]);

        await RemoveOrderRow(f, 9030);   // 旧单被归档 / 删除

        var after = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });
        Assert.Equal(9030, after[CoachFixture.StudentA]);          // 记录还在：9031 不会被误标成「新」
        var rows = await Stored(f);
        Assert.Equal((int?)9030, rows.Single().order_id);
    }

    [Fact]
    public async Task WhenTheRecordedFirstOrderIsCancelled_NewMovesToTheNextValidOrder()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9040, CoachFixture.StudentA, D.AddDays(-10));
        AddOrder(f, 9041, CoachFixture.StudentA, D.AddDays(-5));
        AddOrder(f, 9042, CoachFixture.StudentA, D.AddDays(-1));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        await f.NewContext().order.Where(o => o.id == 9040).ExecuteUpdateAsync(s => s.SetProperty(o => o.valid, 0));   // 第一单作废

        var after = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });
        Assert.Equal(9041, after[CoachFixture.StudentA]);
        Assert.Equal((int?)9041, (await Stored(f)).Single().order_id);
    }

    [Fact]
    public async Task WhenTheRecordedOrderIsReassignedToAnotherMember_ItNoLongerCountsAsTheFirstOfTheOldOne()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9090, CoachFixture.StudentA, D.AddDays(-10));
        AddOrder(f, 9091, CoachFixture.StudentA, D.AddDays(-1));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        await f.NewContext().order.Where(o => o.id == 9090).ExecuteUpdateAsync(s => s.SetProperty(o => o.member_id, CoachFixture.StudentB));   // 订单改挂到别的会员

        var first = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA, CoachFixture.StudentB });
        Assert.Equal(9091, first[CoachFixture.StudentA]);   // A 的「新」顺延到仍属于 A 的下一张
        Assert.Equal(9090, first[CoachFixture.StudentB]);   // 9090 现在是 B 的第一单
    }

    [Fact]
    public async Task WhenTheOnlyValidOrderIsCancelled_TheRecordIsRemoved()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9050, CoachFixture.StudentA, D.AddDays(-2));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        await f.NewContext().order.Where(o => o.id == 9050).ExecuteUpdateAsync(s => s.SetProperty(o => o.is_test, 1));   // 改成测试单

        var after = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });
        Assert.False(after.ContainsKey(CoachFixture.StudentA));
        Assert.Empty(await Stored(f));
    }

    [Fact]
    public async Task Reconcile_ReplacesTheRecord_WhenAnEarlierValidOrderShowsUp()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9060, CoachFixture.StudentA, D.AddDays(-2));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        AddOrder(f, 9059, CoachFixture.StudentA, D.AddDays(-9));   // 补录了一张更早的有效单
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.ReconcileAsync(f.NewContext(), CoachFixture.StudentA);

        Assert.Equal((int?)9059, (await Stored(f)).Single().order_id);
    }

    [Fact]
    public async Task SameBizDate_TheLowerOrderIdIsTheFirstOrder()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9081, CoachFixture.StudentA, D);
        AddOrder(f, 9080, CoachFixture.StudentA, D);   // 开单时间相同，id 小的算第一单
        AddOrder(f, 9082, CoachFixture.StudentA, D.AddHours(-1).AddDays(1));
        await f.Db.SaveChangesAsync();
        var first = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });
        Assert.Equal(9080, first[CoachFixture.StudentA]);
    }

    [Fact]
    public async Task GetFirstOrderIds_OnlyAnswersForTheMembersAsked_AndHandlesSeveralAtOnce()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9070, CoachFixture.StudentA, D.AddDays(-4));
        AddOrder(f, 9071, CoachFixture.StudentB, D.AddDays(-3));
        AddOrder(f, 9072, CoachFixture.StudentB, D.AddDays(-2));
        AddOrder(f, 9073, CoachFixture.StudentC, D.AddDays(-1));
        await f.Db.SaveChangesAsync();

        var first = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA, CoachFixture.StudentB });

        Assert.Equal(new Dictionary<int, int> { [CoachFixture.StudentA] = 9070, [CoachFixture.StudentB] = 9071 }, first);
        Assert.Equal(2, (await Stored(f)).Count);   // C 没被问到，不写
        Assert.Empty(await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), Array.Empty<int>()));
    }

    // ───────────── 列表：「新」读的是存下来的记录 ─────────────

    [Fact]
    public async Task CareList_NewFlag_ReadsTheRecord_SoRemovingTheFirstOrderRowDoesNotMoveIt()
    {
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        AddOrder(f, 9101, CoachFixture.StudentA, now.AddDays(-10));   // A 的第一单
        AddOrder(f, 9102, CoachFixture.StudentA, now.AddDays(-1));
        await f.Db.SaveChangesAsync();

        async Task<int[]> NewIds()
        {
            var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
            var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, now.AddDays(-30).Date, now.Date, "session-7", null, pageIndex: 1, pageSize: 50);
            var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
            return api.data!.items.Where(o => o.isNewCareCustomer).Select(o => o.id).OrderBy(i => i).ToArray();
        }

        Assert.Equal(new[] { 9101 }, await NewIds());            // 第一次读，同时把记录补上
        await RemoveOrderRow(f, 9101);                           // 旧单被清掉
        Assert.Equal(Array.Empty<int>(), await NewIds());        // 9102 不会因此变成「新」
    }

    // ───────────── 订单生效的那一刻就记下首单（不等有人打开列表） ─────────────

    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }

    private static async Task<int> DraftCareOrder(CoachFixture f, int memberId)
    {
        var db = f.NewContext();
        var order = new Order { type = "养护", shop = "万龙服务中心", member_id = memberId, valid = 0, recepting = 1, staff_id = 9,
            code = "YH_20261110_" + Guid.NewGuid().ToString("N")[..6] };
        db.order.Add(order); await db.SaveChangesAsync();
        db.care.Add(new Care { order_id = order.id, equipment = "双板", brand = "品牌", scale = "170", need_wax = 1, need_unwax = 1, valid = 0, biz_type = "雪季养护" });
        await db.SaveChangesAsync();
        return order.id;
    }

    [Fact]
    public async Task PlacingACareOrder_RecordsTheFirstOrder_AndALaterOrderDoesNotMoveIt()
    {
        await using var f = await CoachFixture.Create();
        int first = await DraftCareOrder(f, CoachFixture.StudentA);
        int second = await DraftCareOrder(f, CoachFixture.StudentA);

        var r1 = await new OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(first, "session-1");
        Assert.Equal(0, ((ApiResult<Order>)((OkObjectResult)r1.Result!).Value!).code);
        var rows = await Stored(f);
        Assert.Equal((CoachFixture.StudentA, (int?)first), (rows.Single().member_id, rows.Single().order_id));   // 没有人打开过列表，记录也已经在了

        var r2 = await new OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(second, "session-1");
        Assert.Equal(0, ((ApiResult<Order>)((OkObjectResult)r2.Result!).Value!).code);
        Assert.Equal((int?)first, (await Stored(f)).Single().order_id);
    }

    [Fact]
    public async Task CareList_StillWorks_WhenTheFirstOrderColumnsAreMissing_JustWithoutTheNewFlag()
    {
        // 部署顺序搞反（代码先上、加列脚本还没跑）时，「新」只是暂时不显示：
        // 养护订单列表照常出，会员相关的查询也不受影响（Member 实体不带这两列）
        await using var f = await CoachFixture.Create();
        var now = DateTime.Now;
        AddOrder(f, 9201, CoachFixture.StudentA, now.AddDays(-2));
        await f.Db.SaveChangesAsync();
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE member DROP COLUMN care_first_order_id");
        await f.Db.Database.ExecuteSqlRawAsync("ALTER TABLE member DROP COLUMN care_first_order_date");

        var ctl = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor());
        var result = await ctl.GetOrdersByStaffPaged(null, null, "养护", null, now.AddDays(-30).Date, now.Date, "session-7", null, pageIndex: 1, pageSize: 50);

        var api = (ApiResult<OrderController.PagedOrderResult>)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(0, api.code);
        var order = Assert.Single(api.data!.items);
        Assert.Equal(9201, order.id);
        Assert.False(order.isNewCareCustomer);
        Assert.NotEmpty(await f.NewContext().member.ToListAsync());                                     // 会员查询不受影响
        Assert.Equal(CoachFixture.StudentA, (await f.NewContext().member.SingleAsync(m => m.id == CoachFixture.StudentA)).id);
    }

    // ───────────── 会员整行保存不会覆盖这两列 ─────────────

    [Fact]
    public async Task SavingAStaleMemberRow_DoesNotOverwriteTheFirstOrderColumns()
    {
        // 代码里有好几处 Entry(member).State = Modified 的整行保存（用的是之前读到的旧副本）。
        // 记首单之前读出来的副本存回去，不能把记下的首单抹掉
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9300, CoachFixture.StudentA, D.AddDays(-3));
        await f.Db.SaveChangesAsync();
        Member stale = await f.NewContext().member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentA);

        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });
        Assert.Equal((int?)9300, (await Stored(f)).Single().order_id);

        var db = f.NewContext();
        stale.real_name = "改过名字";
        db.member.Entry(stale).State = EntityState.Modified;
        await db.SaveChangesAsync();

        Assert.Equal("改过名字", (await f.NewContext().member.SingleAsync(m => m.id == CoachFixture.StudentA)).real_name);
        var rows = await Stored(f);
        Assert.Equal((CoachFixture.StudentA, (int?)9300, (DateTime?)D.AddDays(-3)), (rows.Single().member_id, rows.Single().order_id, rows.Single().biz_date));
    }

    [Fact]
    public async Task WritingTheFirstOrder_DoesNotTouchTheMembersOtherColumns()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9301, CoachFixture.StudentA, D.AddDays(-3));
        await f.Db.SaveChangesAsync();
        Member before = await f.NewContext().member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentA);

        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        Member after = await f.NewContext().member.AsNoTracking().SingleAsync(m => m.id == CoachFixture.StudentA);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after));   // update_date 等都没动
    }

    // ───────────── 合并会员：首单跟着走 ─────────────

    private static async Task Merge(CoachFixture f, int source, int target) =>
        await new MemberController(f.NewContext(), f.Config).MergeMember(source, target);

    private static async Task<MemberCareFirstOrder?> StoredOf(CoachFixture f, int memberId) =>
        (await Stored(f)).SingleOrDefault(r => r.member_id == memberId);

    [Fact]
    public async Task Merge_SourceHadTheEarlierFirstOrder_TargetTakesItOver_AndSourceIsCleared()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9400, CoachFixture.StudentA, D.AddDays(-20));   // 源会员的第一单（更早）
        AddOrder(f, 9401, CoachFixture.StudentB, D.AddDays(-5));    // 目标会员的第一单
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA, CoachFixture.StudentB });

        await Merge(f, CoachFixture.StudentA, CoachFixture.StudentB);

        Assert.Equal((int?)9400, (await StoredOf(f, CoachFixture.StudentB))!.order_id);
        Assert.Equal((DateTime?)D.AddDays(-20), (await StoredOf(f, CoachFixture.StudentB))!.biz_date);
        Assert.Null(await StoredOf(f, CoachFixture.StudentA));
    }

    [Fact]
    public async Task Merge_TargetAlreadyHadTheEarlierFirstOrder_KeepsIt_AndSourceIsCleared()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9410, CoachFixture.StudentA, D.AddDays(-5));
        AddOrder(f, 9411, CoachFixture.StudentB, D.AddDays(-20));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA, CoachFixture.StudentB });

        await Merge(f, CoachFixture.StudentA, CoachFixture.StudentB);

        Assert.Equal((int?)9411, (await StoredOf(f, CoachFixture.StudentB))!.order_id);
        Assert.Null(await StoredOf(f, CoachFixture.StudentA));
    }

    [Fact]
    public async Task Merge_TargetHadNoCareOrder_GetsTheSourcesFirstOrder()
    {
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9420, CoachFixture.StudentA, D.AddDays(-7));
        AddOrder(f, 9421, CoachFixture.StudentA, D.AddDays(-2));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA });

        await Merge(f, CoachFixture.StudentA, CoachFixture.StudentB);

        Assert.Equal((int?)9420, (await StoredOf(f, CoachFixture.StudentB))!.order_id);
        Assert.Null(await StoredOf(f, CoachFixture.StudentA));
        var first = await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentB });
        Assert.Equal(9420, first[CoachFixture.StudentB]);
    }

    [Fact]
    public async Task Merge_SourceFirstOrderRowAlreadyArchived_ItsRecordStillBecomesTheTargets()
    {
        // 源会员的第一单已被归档（订单表里没有了，只剩记录）；目标会员只有更晚的单。
        // 合并后这个人真正的第一单仍是那张已归档的，不能因为订单表里找不到就丢掉
        await using var f = await CoachFixture.Create();
        AddOrder(f, 9430, CoachFixture.StudentA, D.AddDays(-30));
        AddOrder(f, 9431, CoachFixture.StudentB, D.AddDays(-3));
        await f.Db.SaveChangesAsync();
        await CareFirstOrderService.GetFirstOrderIdsAsync(f.NewContext(), new[] { CoachFixture.StudentA, CoachFixture.StudentB });
        await RemoveOrderRow(f, 9430);

        await Merge(f, CoachFixture.StudentA, CoachFixture.StudentB);

        var target = (await StoredOf(f, CoachFixture.StudentB))!;
        Assert.Equal((9430, D.AddDays(-30)), (target.order_id, target.biz_date));
        Assert.Null(await StoredOf(f, CoachFixture.StudentA));
    }

    [Fact]
    public async Task Merge_NeitherHasACareOrder_LeavesBothEmpty()
    {
        await using var f = await CoachFixture.Create();
        await Merge(f, CoachFixture.StudentA, CoachFixture.StudentB);
        Assert.Empty(await Stored(f));
    }

    // ───────────── 加列脚本与模型一致 ─────────────

    [Fact]
    public void SchemaScript_AddsExactlyTheNullableColumnsTheModelMaps()
    {
        string? script = null;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); script == null && dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "snowmeet_ai_doc", "sql", "2026-11-10_member_care_first_order.sql");
            if (File.Exists(candidate)) script = candidate;
        }
        Assert.True(script != null, "需要相邻 snowmeet_ai_doc 仓库里的 sql/2026-11-10_member_care_first_order.sql");
        string sql = File.ReadAllText(script!);

        using var db = new CoachFixtureModelOnly().Context;
        var entity = db.Model.FindEntityType(typeof(MemberCareFirstOrder))!;
        Assert.Equal("member", entity.GetTableName());
        var memberEntity = db.Model.FindEntityType(typeof(Member))!;
        // 与 Member 共用 member 表：除了主键 id，其余列都是这次新加的
        var memberColumns = memberEntity.GetProperties().Select(p => p.GetColumnName()).ToHashSet();
        var modelNewColumns = entity.GetProperties().Select(p => p.GetColumnName()).Where(c => c != "id").OrderBy(c => c).ToList();
        Assert.Equal(new[] { "care_first_order_date", "care_first_order_id" }, modelNewColumns);
        Assert.All(modelNewColumns, c => Assert.DoesNotContain(c, memberColumns));   // Member 实体本身不映射它们
        Assert.All(entity.GetProperties().Where(p => p.GetColumnName() != "id"), p => Assert.True(p.IsNullable));

        var scriptColumns = System.Text.RegularExpressions.Regex.Matches(sql,
                @"ALTER\s+TABLE\s+dbo\.member\s+ADD\s+(?<col>\w+)\s+(?<type>\w+)\s+NULL", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Groups["col"].Value).OrderBy(c => c).ToList();
        Assert.Equal(modelNewColumns, scriptColumns);
        Assert.Contains("COL_LENGTH('dbo.member', 'care_first_order_id')", sql);      // 脚本可重复执行
        Assert.Contains("COL_LENGTH('dbo.member', 'care_first_order_date')", sql);
    }

    private sealed class CoachFixtureModelOnly
    {
        public ApplicationDBContext Context { get; } = new(new DbContextOptionsBuilder<ApplicationDBContext>()
            .UseSqlite("Data Source=model-only;Mode=Memory").Options);
    }
}
