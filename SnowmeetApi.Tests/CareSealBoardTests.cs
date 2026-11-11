using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Controllers;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Tests;

/// <summary>
/// 养护「封板」（用户 2026-11-09 提出）：
/// 开单时「刮蜡」后面多一个「封板」选项；有热蜡时刮蜡和封板必选其一（默认刮蜡）。
/// 封板不是养护工序（不生成任务、不计费），只是 care.seal_board = 1 这个标志；
/// 取板（「发板」任务完成）后，给封板装备的顾客发一张开板刮蜡券（ticket_template 20）。
/// </summary>
public class CareSealBoardTests
{
    private sealed class Http : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = new DefaultHttpContext(); }
    private static CareController Ctl(CoachFixture f) => new(f.NewContext(), f.Config, new Http());
    // 联动规则是纯函数，不碰数据库
    private static CareController Pure() => new(null!, new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new Http());

    private static Care C(int wax = 1, int unwax = 1, int seal = 0, int edge = 0, int freeWax = 0, string? summer = null) =>
        new() { equipment = "双板", need_edge = edge, need_wax = wax, need_unwax = unwax, seal_board = seal, free_wax = freeWax,
            summer = summer, biz_type = summer == null ? "雪季养护" : "非雪季养护" };

    private static (int wax, int unwax, int seal) S(Care c) => (c.need_wax, c.need_unwax, c.seal_board);

    // ---------- 服务项联动（ApplyServiceLinkage）：热蜡 / 刮蜡 / 封板 ----------

    [Fact]
    public void TurnOnWax_DefaultsToScrape_NotSeal()
    {
        var c = C(wax: 1, unwax: 0, seal: 0);
        Pure().ApplyServiceLinkage(c, "need_wax");
        Assert.Equal((1, 1, 0), S(c));
    }

    [Fact]
    public void TurnOnWax_AfterSealWasChosenEarlier_GoesBackToScrapeDefault()
    {
        // 先封板、再关热蜡、再开热蜡：每次重新开热蜡都回到默认刮蜡
        var c = C(wax: 1, unwax: 0, seal: 1);
        Pure().ApplyServiceLinkage(c, "need_wax");
        Assert.Equal((1, 1, 0), S(c));
    }

    [Fact]
    public void TurnOffWax_ClearsScrapeAndSeal()
    {
        var sealed_ = C(wax: 0, unwax: 0, seal: 1);
        Pure().ApplyServiceLinkage(sealed_, "need_wax");
        Assert.Equal((0, 0, 0), S(sealed_));
        var scraped = C(wax: 0, unwax: 1, seal: 0);
        Pure().ApplyServiceLinkage(scraped, "need_wax");
        Assert.Equal((0, 0, 0), S(scraped));
    }

    [Fact]
    public void TapSeal_WithWax_SwitchesScrapeToSeal()
    {
        var c = C(wax: 1, unwax: 1, seal: 1);   // 界面先把 seal_board 置 1，再发联动请求
        Pure().ApplyServiceLinkage(c, "seal_board");
        Assert.Equal((1, 0, 1), S(c));
    }

    [Fact]
    public void TapScrape_WhileSealed_SwitchesBackToScrape()
    {
        var c = C(wax: 1, unwax: 1, seal: 1);   // 界面先把 need_unwax 置 1
        Pure().ApplyServiceLinkage(c, "need_unwax");
        Assert.Equal((1, 1, 0), S(c));
    }

    [Fact]
    public void ScrapeTurnedOffWithWax_BecomesSeal_SoOneOfTheTwoIsAlwaysChosen()
    {
        var c = C(wax: 1, unwax: 0, seal: 0);
        Pure().ApplyServiceLinkage(c, "need_unwax");
        Assert.Equal((1, 0, 1), S(c));
    }

    [Fact]
    public void Seal_WithoutWax_IsRejected()
    {
        var c = C(wax: 0, unwax: 0, seal: 1);
        Pure().ApplyServiceLinkage(c, "seal_board");
        Assert.Equal((0, 0, 0), S(c));
    }

    [Fact]
    public void ScrapeOnly_WithoutWax_StaysScrapeOnly_NoSeal()
    {
        // 没有热蜡：刮蜡可以单独选（万龙单刮蜡一口价那条），也不会被翻成封板
        var on = C(wax: 0, unwax: 1, seal: 0);
        Pure().ApplyServiceLinkage(on, "need_unwax");
        Assert.Equal((0, 1, 0), S(on));
        var off = C(wax: 0, unwax: 0, seal: 0);
        Pure().ApplyServiceLinkage(off, "need_unwax");
        Assert.Equal((0, 0, 0), S(off));
    }

    [Fact]
    public void MachineWax_ClearsWaxScrapeAndSeal()
    {
        var c = C(wax: 1, unwax: 0, seal: 1, freeWax: 1);
        Pure().ApplyServiceLinkage(c, "free_wax");
        Assert.Equal((0, 0, 0), S(c));
        Assert.Equal(1, c.free_wax);
    }

    [Theory]
    [InlineData("now")]
    [InlineData("later")]
    public void SummerCare_NeverSeals(string mode)
    {
        var c = C(wax: 1, unwax: 0, seal: 1, summer: mode);
        Pure().ApplyServiceLinkage(c, "summer");
        Assert.Equal(0, c.seal_board);
        if (mode == "later") Assert.Equal((1, 1, 0), S(c));   // 先双项：修刃+热蜡+刮蜡
    }

    [Fact]
    public void DeriveDefaultServices_ClearsSeal()
    {
        var c = C(wax: 1, unwax: 0, seal: 1);
        Pure().ApplyDefaultServices(c, null!, null!);
        Assert.Equal((0, 0, 0), S(c));
    }

    // ---------- 收敛到合法组合（Normalize）：开单页预览和下单落库共用 ----------

    [Theory]
    //   输入 wax, unwax, seal, freeWax, summer → 期望 wax, unwax, seal
    [InlineData(1, 1, 0, 0, null, 1, 1, 0)]     // 热蜡+刮蜡：原样
    [InlineData(1, 0, 1, 0, null, 1, 0, 1)]     // 热蜡+封板：原样
    [InlineData(1, 1, 1, 0, null, 1, 0, 1)]     // 两个都勾：以封板为准（封板就是不刮）
    [InlineData(1, 0, 0, 0, null, 1, 1, 0)]     // 热蜡但都没选：默认刮蜡
    [InlineData(0, 0, 1, 0, null, 0, 0, 0)]     // 没热蜡不能封板
    [InlineData(0, 1, 0, 0, null, 0, 1, 0)]     // 只刮蜡：不动
    [InlineData(0, 0, 0, 0, null, 0, 0, 0)]     // 啥也没选：不动
    [InlineData(1, 0, 1, 1, null, 1, 0, 0)]     // 机打蜡：不能封板，其他项不动
    [InlineData(1, 1, 1, 0, "later", 1, 1, 0)]  // 非雪季：不能封板，其他项不动
    [InlineData(1, 0, 1, 0, "now", 1, 0, 0)]
    public void Normalize_ReducesToValidCombination(int wax, int unwax, int seal, int freeWax, string? summer, int eWax, int eUnwax, int eSeal)
    {
        var c = C(wax, unwax, seal, freeWax: freeWax, summer: summer);
        CareSealRules.Normalize(c);
        Assert.Equal((eWax, eUnwax, eSeal), S(c));
    }

    [Fact]
    public void ShouldGrantTicket_OnlyForSealedAndNotCancelled()
    {
        Assert.True(CareSealRules.ShouldGrantTicket(new Care { seal_board = 1 }));
        Assert.False(CareSealRules.ShouldGrantTicket(new Care { seal_board = 0 }));
        Assert.False(CareSealRules.ShouldGrantTicket(new Care { seal_board = 1, is_cancel = true }));
        Assert.False(CareSealRules.ShouldGrantTicket(null!));
    }

    // ---------- 开单页实时计价：响应里的 care 以服务端收敛结果为准，封板不改价 ----------

    private static async Task<System.Text.Json.JsonElement> Preview(CoachFixture f, Care care, string? changed, string shop = "崇礼旗舰店")
    {
        var res = await Ctl(f).CalcCareCharge(new CareController.CalcCareChargeRequest { shop = shop, memberId = CoachFixture.StudentA, care = care, changedField = changed }, "session-7");
        var env = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(res.Result).Value);
        Assert.Equal(0, env.code);
        return System.Text.Json.JsonSerializer.SerializeToElement(env.data);
    }

    [Fact]
    public async Task Preview_TapSeal_ReturnsSealedCare_AndPriceIsUnchanged()
    {
        await using var f = await CoachFixture.Create();
        var scraped = await Preview(f, C(wax: 1, unwax: 1), changed: "need_wax");
        var sealed_ = await Preview(f, C(wax: 1, unwax: 1, seal: 1), changed: "seal_board");
        var care = sealed_.GetProperty("care");
        Assert.Equal((1, 0, 1), (care.GetProperty("need_wax").GetInt32(), care.GetProperty("need_unwax").GetInt32(), care.GetProperty("seal_board").GetInt32()));
        Assert.Equal(scraped.GetProperty("commonCharge").GetDouble(), sealed_.GetProperty("commonCharge").GetDouble());
        Assert.True(sealed_.GetProperty("commonCharge").GetDouble() > 0);   // 热蜡是计费项，封板只是不刮，价格不变
    }

    [Fact]
    public async Task Preview_WithoutChangedField_StillNormalizes()
    {
        await using var f = await CoachFixture.Create();
        // 草稿找回时的纯重新计价：没有热蜡的封板被清掉；热蜡但两项都没选的补上默认刮蜡
        var noWax = (await Preview(f, C(wax: 0, unwax: 0, seal: 1), changed: null)).GetProperty("care");
        Assert.Equal(0, noWax.GetProperty("seal_board").GetInt32());
        var legacy = (await Preview(f, C(wax: 1, unwax: 0, seal: 0), changed: null)).GetProperty("care");
        Assert.Equal((1, 1, 0), (legacy.GetProperty("need_wax").GetInt32(), legacy.GetProperty("need_unwax").GetInt32(), legacy.GetProperty("seal_board").GetInt32()));
    }

    // ---------- 下单落库：seal_board 由服务端收敛，不采信界面带来的不合法组合 ----------

    private static async Task<(int orderId, int careId)> DraftCare(CoachFixture f, int wax, int unwax, int seal, string? summer = null, int? memberId = CoachFixture.StudentA)
    {
        var db = f.NewContext();
        var order = new SnowmeetApi.Models.Order { type = "养护", shop = "崇礼旗舰店", member_id = memberId, valid = 0, recepting = 1, staff_id = 9, code = "YH_20261109_" + Guid.NewGuid().ToString("N")[..6] };
        db.order.Add(order); await db.SaveChangesAsync();
        var care = new Care { order_id = order.id, equipment = "双板", brand = "品牌", scale = "170", need_edge = 0, need_wax = wax, need_unwax = unwax,
            seal_board = seal, summer = summer, biz_type = summer == null ? "雪季养护" : "非雪季养护", valid = 0 };
        db.care.Add(care); await db.SaveChangesAsync();
        return (order.id, care.id);
    }

    private static async Task<ApiResult<SnowmeetApi.Models.Order>> Place(CoachFixture f, int orderId)
    {
        var res = await new OrderController(f.NewContext(), f.Config, new Http()).PlaceCareOrder(orderId, "session-1");
        return Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(res.Result).Value);
    }

    [Theory]
    [InlineData(1, 0, 1, null, 1, 0, 1)]    // 热蜡+封板：落库就是封板，没有刮蜡
    [InlineData(1, 1, 0, null, 1, 1, 0)]    // 热蜡+刮蜡：不封板
    [InlineData(1, 1, 1, null, 1, 0, 1)]    // 都勾了（界面不该发生）：以封板为准
    [InlineData(0, 0, 1, null, 0, 0, 0)]    // 没热蜡的封板：清掉，之后不会白发券
    public async Task PlaceOrder_PersistsNormalizedSeal(int wax, int unwax, int seal, string? summer, int eWax, int eUnwax, int eSeal)
    {
        await using var f = await CoachFixture.Create();
        var (orderId, careId) = await DraftCare(f, wax, unwax, seal, summer);
        var r = await Place(f, orderId);
        // 没有任何服务项的草稿下单是否通过不是这里要测的；只看落库的服务项
        _ = r;
        var care = await f.Db.care.AsNoTracking().SingleAsync(c => c.id == careId);
        Assert.Equal((eWax, eUnwax, eSeal), (care.need_wax, care.need_unwax, care.seal_board));
    }

    // ---------- 取板（发板完成）后发开板刮蜡券 ----------

    private const int SealTemplate = CareSealRules.SealTicketTemplateId;

    private static async Task AddSealTemplate(CoachFixture f)
    {
        f.Db.ticketTemplate.Add(new TicketTemplate { id = SealTemplate, name = "开板刮蜡券", memo = "封板后，免费刮蜡", type = "养护券", biz_type = "养护",
            miniapp_recept_path = "", sharable = 0, valid = 1 });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
    }

    /// <summary>已生效、等着取板的一件装备：发板任务未开始。返回 (orderId, careId, 发板任务 id)。</summary>
    private static async Task<(int orderId, int careId, int finishTaskId)> PendingPickup(CoachFixture f, int seal, int? memberId = CoachFixture.StudentA, int? orderId = null)
    {
        var db = f.NewContext();
        if (orderId == null)
        {
            var order = new SnowmeetApi.Models.Order { type = "养护", shop = "崇礼旗舰店", member_id = memberId, valid = 1, staff_id = 9, code = "YH_P_" + Guid.NewGuid().ToString("N")[..6] };
            db.order.Add(order); await db.SaveChangesAsync(); orderId = order.id;
        }
        var care = new Care { order_id = orderId, equipment = "双板", brand = "品牌", scale = "170", need_wax = 1, need_unwax = seal == 1 ? 0 : 1,
            seal_board = seal, biz_type = "雪季养护", valid = 1, task_flow_code = "WF_TEST" };
        db.care.Add(care); await db.SaveChangesAsync();
        var finish = new CareTask { care_id = care.id, task_name = "发板", sort = 60, status = "未开始", valid = 1 };
        db.careTask.Add(finish); await db.SaveChangesAsync();
        return (orderId.Value, care.id, finish.id);
    }

    private static async Task Pickup(CoachFixture f, int taskId, bool cancel = false) =>
        await Ctl(f).SetTaskStatus(taskId, "已完成", "扫码取板", "session-7", isCancel: cancel, cancelReason: cancel ? "测试取消" : null);

    private static Task<List<Ticket>> Tickets(CoachFixture f, int templateId) =>
        f.NewContext().ticket.Where(t => t.template_id == templateId).ToListAsync();

    [Fact]
    public async Task PickupOfSealedCare_IssuesOneOpenBoardScrapeTicket_ToTheCustomer()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (orderId, careId, task) = await PendingPickup(f, seal: 1);
        await Pickup(f, task);
        var t = Assert.Single(await Tickets(f, SealTemplate));
        Assert.Equal(CoachFixture.StudentA, t.member_id);
        Assert.Equal("开板刮蜡券", t.name);
        Assert.Equal(1, t.valid);
        Assert.Equal(1, t.is_active);
        Assert.Equal(0, t.used);
        Assert.Equal(("养护", careId), (t.biz_type, t.biz_id));
        Assert.Equal(CareSealRules.GrantMemo(careId), t.create_memo);
        Assert.Equal(10, t.staff_id);                       // 取板操作的店员
        Assert.True(t.expire_date > new DateTime(2100, 1, 1));   // 模板没配有效期 = 永久有效
        Assert.Contains("免费刮蜡", t.memo);
        // 养护完成赠送（老顾客券 16）照旧发，互不影响
        Assert.Single(await Tickets(f, 16));
        Assert.Equal("已完成", (await f.NewContext().careTask.SingleAsync(x => x.id == task)).status);
    }

    [Fact]
    public async Task PickupOfCareWithoutSeal_IssuesNoOpenBoardTicket()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (_, _, task) = await PendingPickup(f, seal: 0);
        await Pickup(f, task);
        Assert.Empty(await Tickets(f, SealTemplate));
        Assert.Single(await Tickets(f, 16));
    }

    [Fact]
    public async Task PickupRepeated_StillOnlyOneTicketPerCare()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (_, _, task) = await PendingPickup(f, seal: 1);
        await Pickup(f, task);
        await Pickup(f, task);      // 重试 / 验证码重放
        Assert.Single(await Tickets(f, SealTemplate));
    }

    [Fact]
    public async Task TwoSealedCaresInOneOrder_GetOneTicketEach()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (orderId, careA, taskA) = await PendingPickup(f, seal: 1);
        var (_, careB, taskB) = await PendingPickup(f, seal: 1, orderId: orderId);
        await Pickup(f, taskA);
        await Pickup(f, taskB);
        var tickets = await Tickets(f, SealTemplate);
        Assert.Equal(2, tickets.Count);
        Assert.Equal(new[] { careA, careB }.OrderBy(x => x), tickets.Select(t => t.biz_id!.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task CancelledPickup_IssuesNoOpenBoardTicket()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (_, careId, task) = await PendingPickup(f, seal: 1);
        await Pickup(f, task, cancel: true);
        Assert.Empty(await Tickets(f, SealTemplate));
        Assert.True((await f.NewContext().care.SingleAsync(c => c.id == careId)).is_cancel);
    }

    [Fact]
    public async Task OrderWithoutMember_PickupStillCompletes_NoTicket()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (_, _, task) = await PendingPickup(f, seal: 1, memberId: null);
        await Pickup(f, task);
        Assert.Empty(await Tickets(f, SealTemplate));
        Assert.Equal("已完成", (await f.NewContext().careTask.SingleAsync(x => x.id == task)).status);
    }

    [Fact]
    public async Task MissingTemplate_PickupStillCompletes_AndTheOtherGiftIsStillIssued()
    {
        await using var f = await CoachFixture.Create();     // 故意不建模板 20
        var (_, _, task) = await PendingPickup(f, seal: 1);
        await Pickup(f, task);
        Assert.Empty(await Tickets(f, SealTemplate));
        Assert.Equal("已完成", (await f.NewContext().careTask.SingleAsync(x => x.id == task)).status);
        Assert.Single(await Tickets(f, 16));
    }

    [Fact]
    public async Task OnlyTheFinishTaskTriggersTheTicket_OtherStepsDoNot()
    {
        await using var f = await CoachFixture.Create();
        await AddSealTemplate(f);
        var (_, careId, finish) = await PendingPickup(f, seal: 1);
        var db = f.NewContext();
        var wax = new CareTask { care_id = careId, task_name = "热蜡", sort = 40, status = "未开始", valid = 1 };
        db.careTask.Add(wax); await db.SaveChangesAsync();
        await Ctl(f).SetTaskStatus(wax.id, "已完成", "热蜡完成", "session-7");
        Assert.Empty(await Tickets(f, SealTemplate));      // 热蜡做完不发券，取板才发
    }
}
