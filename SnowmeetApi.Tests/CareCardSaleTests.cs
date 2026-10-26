using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services;
using PayIdentity = SnowmeetApi.Controllers.Order.PaymentIdentityController;

namespace SnowmeetApi.Tests;

public class CareCardSaleTests
{
    [Fact]
    public async Task PreviewDoesNotGrant_ThenPaidRetailGrantsAndUsesExactlyOnce()
    {
        await using var f = await Fixture.Create(2);
        var request = f.Request();
        var preview = await f.Service.PreviewAsync(request);
        Assert.Equal(600m, preview.amount);
        Assert.Equal(2, preview.items.Single().punches);
        Assert.Equal(8, preview.items.Single().remaining);
        Assert.Empty(await f.Db.punchCard.ToListAsync());
        Assert.Empty(await f.Db.careTask.ToListAsync());
        await f.Service.SaveDraftAsync(request);
        await f.Service.SaveDraftAsync(request); // 重复试算确认、草稿覆盖不能残留关联或重复发卡。
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        Assert.Equal(retailId, await f.Service.SubmitAsync(f.OrderId, 28));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompletePaidAsync(retailId, null));
        await f.Pay(retailId);
        Assert.True(await f.Service.CompletePaidAsync(retailId, f.PaymentId));
        int tasks = await f.Db.careTask.CountAsync();
        Assert.True(await f.Service.CompletePaidAsync(retailId, f.PaymentId));
        Assert.Equal(tasks, await f.Db.careTask.CountAsync());
        var card = await f.Db.punchCard.SingleAsync();
        Assert.Equal(2, card.punches);
        Assert.Equal(8, card.remaining);
        Assert.Equal(10, card.member_id); // 即使支付记录意图是self，也不能转给代付者。
        Assert.Null(card.equip_brand);
        Assert.Equal(2, await f.Db.punchCardUsed.CountAsync(u => u.valid));
        Assert.Equal(f.OrderId, retailId);
        Assert.Single(await f.Db.order.ToListAsync());
        Assert.All(await f.Db.care.ToListAsync(), c => Assert.Equal(retailId, c.order_id));
        Assert.Equal(retailId, (await f.Db.retail.SingleAsync()).order_id);
        Assert.Equal(600, (await f.Db.order.FindAsync(retailId))!.total_amount);
        Assert.Equal(1, (await f.Db.order.FindAsync(f.OrderId))!.dealed);
    }

    [Fact]
    public async Task CancellingOneEquipmentReturnsOnlyItsUse_AndCannotReapplyOrRefund()
    {
        await using var f = await Fixture.Create(2);
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId);
        await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        var care = await f.Db.care.OrderBy(c => c.id).FirstAsync();
        int finish = await f.Db.careTask.Where(t => t.care_id == care.id && t.task_name == "发板").Select(t => t.id).SingleAsync();
        var controller = new CareController(f.Db, f.Config, f.Http);
        await controller.SetTaskStatus(finish, "已完成", "测试取消", "staff-session", isCancel: true, cancelReason: "取消本件");
        await controller.SetTaskStatus(finish, "已完成", "测试取消", "staff-session", isCancel: true, cancelReason: "重试");
        Assert.Equal(1, (await f.Db.punchCard.SingleAsync()).punches);
        Assert.Single(await f.Db.punchCardUsed.Where(u => u.valid).ToListAsync());
        await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        await controller.EffectCareOrder(f.OrderId);
        Assert.Equal(1, (await f.Db.punchCard.SingleAsync()).punches);
        var refund = await new OrderController(f.Db, f.Config, f.Http).RefundCore((await f.Db.order.FindAsync(retailId))!, null, new());
        Assert.Equal(1, refund.code);
        Assert.Contains("不允许退款", refund.message);
    }

    [Theory]
    [InlineData("机打蜡季卡", true)]
    [InlineData("双项养护季卡", false)]
    public async Task OnlyWaxSeasonBindsEquipment(string name, bool binds)
    {
        await using var f = await Fixture.Create(1, name, true);
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        var card = await f.Db.punchCard.SingleAsync();
        Assert.Null(card.total);
        Assert.Equal(binds ? "测试品牌" : null, card.equip_brand);
        Assert.Equal(0, card.punches);
    }

    [Fact]
    public async Task OldClientPaymentAndZeroWriteoffContractsRemain_OnlyLinkedCareIsBlocked()
    {
        await using var f = await Fixture.Create(1);
        var old = new SnowmeetApi.Models.Order { type = "零售", shop = "测试店", member_id = 10, total_amount = 35, paying_amount = 35, valid = 1, code = "OLD_LS_01" };
        f.Db.order.Add(old); await f.Db.SaveChangesAsync();
        var payment = new OrderPayment { order_id = old.id, amount = 35, pay_method = "微信支付", status = "待支付" };
        f.Db.orderPayment.Add(payment); await f.Db.SaveChangesAsync();
        var controller = new OrderController(f.Db, f.Config, f.Http);
        var response = await controller.GetOrderFromPaymentByCustomer(payment.id, "customer-session");
        var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, envelope.code);
        Assert.Equal(35, envelope.data!.payments!.Single().amount);
        Assert.Null(await CareCardSaleService.PaymentErrorAsync(f.Db, old, 35, "微信支付"));
        Assert.False(await CareCardSaleService.ContainsCardSaleAsync(f.Db, old.id));
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        var blocked = await controller.WriteoffCareOrder(f.OrderId, "staff-session");
        var blockedEnvelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(blocked.Result).Value);
        Assert.Equal(1, blockedEnvelope.code);
        Assert.Contains("购卡", blockedEnvelope.message);
        var retail = (await f.Db.order.FindAsync(retailId))!;
        Assert.Contains("一次付清", (await CareCardSaleService.PaymentErrorAsync(f.Db, retail, 1, "微信支付"))!);
    }

    [Fact]
    public async Task ProductChangesRequireNewPreview_AndRepairChargesAreAdded()
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        var product = await f.Db.product.SingleAsync(); product.sale_price = 700; await f.Db.SaveChangesAsync();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.OrderId, 28));
        Assert.Contains("重新试算", e.Message);
        Assert.Empty(await f.Db.punchCard.ToListAsync());
        var care = await f.Db.care.SingleAsync(); care.repair_charge = 50; await f.Db.SaveChangesAsync();
        var revised = await f.Service.PreviewAsync(f.Request());
        Assert.Equal(50m, revised.careAmount); Assert.Equal(750m, revised.amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    public async Task FailedBusinessTransactionDoesNotLeakAssetsWhenCallbackSavesLogs_AndCanRetry(double repairFee)
    {
        await using var f = await Fixture.Create(1);
        var care = await f.Db.care.SingleAsync(); care.repair_charge = repairFee;
        if (repairFee > 0) { care.need_repair = 1; care.repair_memo = "维修附加项目"; }
        await f.Db.SaveChangesAsync();
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId, 600 + repairFee);
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_care_tasks BEFORE INSERT ON care_task BEGIN SELECT RAISE(ABORT, 'injected task failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => f.Service.CompletePaidAsync(retailId, f.PaymentId));
        f.Db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order", "test", retailId, "回调记录失败", null, null, null, "injected", ""));
        await f.Db.SaveChangesAsync();
        Assert.Empty(await f.Db.punchCard.ToListAsync());
        Assert.Empty(await f.Db.punchCardUsed.ToListAsync());
        Assert.Empty(await f.Db.careTask.ToListAsync());
        Assert.Equal("支付成功", (await f.Db.orderPayment.SingleAsync()).status);
        await f.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_care_tasks;");
        await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Single(await f.Db.punchCard.ToListAsync());
        Assert.Equal(1, (await f.Db.punchCard.SingleAsync()).punches);
    }

    [Fact]
    public async Task EmptySelectionClearsDraftAndDoesNotRequireANewMemberOrEquipment()
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        var order = (await f.Db.order.FindAsync(f.OrderId))!;
        order.member_id = null;
        await f.Db.SaveChangesAsync();
        await f.Service.SaveDraftAsync(new CareCardSaleRequest { orderId = f.OrderId });
        Assert.Empty(await f.Db.retail.ToListAsync());
        Assert.False(await CareCardSaleService.HasDraftAsync(f.Db, f.OrderId));
    }

    [Fact]
    public async Task OldClientCareDraftStillAcceptsOriginalPayloadWithoutCardSelections()
    {
        await using var f = await Fixture.Create(1);
        var controller = new CareController(f.Db, f.Config, f.Http);
        var response = await controller.SaveCareRecept(new SnowmeetApi.Models.Order
        {
            type = "养护", shop = "测试店", member_id = 10,
            cares = new() { new Care { equipment = "双板", brand = "测试品牌", scale = "170", need_wax = 1 } }
        }, "staff-session");
        var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, envelope.code);
        Assert.Equal(0, envelope.data!.valid);
        Assert.Single(envelope.data.cares);
        Assert.False(await f.Db.order.AnyAsync(o => o.order_source == CareCardSaleService.OrderSource));
    }

    [Fact]
    public async Task WaxSeasonCanExplicitlyPreviewDefaultProjectForEquipmentWithoutService()
    {
        await using var f = await Fixture.Create(1, "机打蜡季卡", true);
        var care = await f.Db.care.SingleAsync(); care.free_wax = 0; await f.Db.SaveChangesAsync();
        var preview = await f.Service.PreviewAsync(f.Request());
        Assert.Equal("机打蜡", preview.items.Single().serviceLabel);
        Assert.Equal(0, care.free_wax); // 试算不修改草稿项目或实际扣次。
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        Assert.Equal(1, care.free_wax);
        await f.Pay(retailId); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Equal(1, (await f.Db.punchCardUsed.SingleAsync()).punch_count);
    }

    [Fact]
    public async Task NoNewConfigurationDoesNotEnableNewEntry()
    {
        await using var f = await Fixture.Create(1);
        var controller = new CareCardSaleController(f.Db, new ConfigurationBuilder().Build(), f.Http);
        var response = await controller.Preview(f.Request(), "staff-session");
        var envelope = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(response).Value);
        Assert.Equal(1, envelope.code);
        Assert.Contains("尚未启用", envelope.message);
    }

    [Fact]
    public async Task PendingPlanSurvivesNewDbContext_ThenIsClearedAndHistoryUsesOriginalRecords()
    {
        await using var f = await Fixture.Create(2);
        Assert.DoesNotContain(f.Db.Model.GetEntityTypes(), t => (t.GetTableName() ?? "").StartsWith("care_card_sale"));
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        Assert.NotEmpty((await f.Db.retail.SingleAsync()).memo);
        var customerView = await new OrderController(f.Db, f.Config, f.Http).GetOrder(retailId);
        Assert.Equal("", customerView.retails.Single().memo);
        Assert.NotEmpty((await f.Db.retail.AsNoTracking().SingleAsync()).memo);
        await f.Pay(retailId);
        // 不依赖内存缓存或原请求的跟踪状态；模拟重启后的支付通知。
        await using (var restarted = f.NewContext())
            await new CareCardSaleService(restarted, f.Config, f.Http).CompletePaidAsync(retailId, f.PaymentId);
        f.Db.ChangeTracker.Clear();
        var row = await f.Db.retail.SingleAsync();
        Assert.Equal("", row.memo);
        Assert.NotNull(row.punch_card_id);
        Assert.Equal(row.id, (await f.Db.punchCard.SingleAsync()).source_retail_id);
        var history = JObject.FromObject(await f.Service.GetDraftAsync(f.OrderId));
        Assert.Equal("completed", (string?)history["state"]);
        Assert.Equal(2, (int?)history["preview"]?["items"]?[0]?["punches"]);
        Assert.True(await f.Service.CompletePaidAsync(retailId, f.PaymentId));
        Assert.Single(await f.Db.punchCard.ToListAsync());
        Assert.Equal(2, await f.Db.punchCardUsed.CountAsync());
    }

    [Fact]
    public async Task ExistingSelfServiceRetailStillGrantsOnceWithoutCarePurchaseMetadata()
    {
        await using var f = await Fixture.Create(1);
        var order = new SnowmeetApi.Models.Order { type = "零售", shop = "测试店", member_id = 10,
            code = "OLD_LS_CARD", total_amount = 600, paying_amount = 600 };
        f.Db.order.Add(order); await f.Db.SaveChangesAsync();
        f.Db.retail.Add(new Retail { order_id = order.id, product_id = 101, sale_price = 600, deal_price = 600,
            order_type = "卡类销售", retail_type = "养护卡" });
        await f.Db.SaveChangesAsync(); await f.Pay(order.id);
        var payment = (await f.Db.orderPayment.FindAsync(f.PaymentId))!;
        payment.member_id = 10; await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var controller = new OrderController(f.Db, f.Config, f.Http);
        await controller.DealSuccessPaidOrder(order.id, f.PaymentId);
        f.Db.ChangeTracker.Clear();
        await controller.DealSuccessPaidOrder(order.id, f.PaymentId);
        var card = await f.Db.punchCard.SingleAsync();
        Assert.Equal(10, card.member_id);
        Assert.Equal(0, card.punches);
        Assert.Equal(card.id, (await f.Db.retail.SingleAsync()).punch_card_id);
        Assert.Empty(await f.Db.punchCardUsed.ToListAsync());
        Assert.Null(await CareCardSaleService.GetInfoAsync(f.Db, order));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LegacyPlacementChecksWholeCardBalance_AndSufficientCardStillWritesOff(int count)
    {
        await using var f = await Fixture.Create(count);
        await PrepareLegacyCard(f, false, 9);
        await using var db = f.NewContext();
        var controller = new OrderController(db, f.Config, f.Http);
        var placed = await controller.PlaceCareOrder(f.OrderId, "staff-session");
        var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(placed.Result).Value);
        if (count == 2)
        {
            Assert.Equal(1, envelope.code); Assert.Contains("次数不足", envelope.message);
            Assert.Equal(0, (await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).valid);
            Assert.Empty(await db.careTask.ToListAsync()); Assert.Empty(await db.punchCardUsed.ToListAsync());
            return;
        }
        Assert.Equal(0, envelope.code);
        var result = await controller.WriteoffCareOrder(f.OrderId, "staff-session");
        var completed = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(0, completed.code); Assert.Equal(1, completed.data!.dealed);
        await controller.WriteoffCareOrder(f.OrderId, "staff-session");
        Assert.Single(await db.punchCardUsed.ToListAsync());
        Assert.Equal(10, (await db.punchCard.AsNoTracking().SingleAsync()).punches);
    }

    [Fact]
    public async Task LegacyAlreadyPlacedOrderWithInsufficientCardReturnsBusinessError_ThenCanRetry()
    {
        await using var f = await Fixture.Create(2);
        await PrepareLegacyCard(f, true, 9);
        await using var db = f.NewContext();
        var controller = new OrderController(db, f.Config, f.Http);
        var result = await controller.WriteoffCareOrder(f.OrderId, "staff-session");
        var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, envelope.code); Assert.Contains("次数不足", envelope.message);
        var order = await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId);
        Assert.Equal(0, order.dealed); Assert.Equal(0d, order.paying_amount);
        Assert.Empty(await db.careTask.ToListAsync()); Assert.Empty(await db.punchCardUsed.ToListAsync());
        var card = await db.punchCard.AsTracking().SingleAsync(); card.punches = 8; await db.SaveChangesAsync();
        var retry = await controller.WriteoffCareOrder(f.OrderId, "staff-session");
        var success = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(retry.Result).Value);
        Assert.Equal(0, success.code); Assert.Equal(1, success.data!.dealed);
        Assert.Equal(2, await db.punchCardUsed.CountAsync());
        Assert.Equal(10, (await db.punchCard.AsNoTracking().SingleAsync()).punches);
    }

    [Fact]
    public async Task LegacyEffectRollbackDoesNotLeakWorkWhenCallerSavesErrorLog()
    {
        await using var f = await Fixture.Create(2);
        await PrepareLegacyCard(f, true, 0);
        await using var db = f.NewContext();
        await db.Database.ExecuteSqlRawAsync(FailCareTaskSql);
        await Assert.ThrowsAsync<DbUpdateException>(() => new CareController(db, f.Config, f.Http).EffectCareOrder(f.OrderId));
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order", "test", f.OrderId, "旧回调失败日志", null, null, null, null, "test"));
        await db.SaveChangesAsync();
        Assert.Empty(await db.careTask.ToListAsync()); Assert.Empty(await db.punchCardUsed.ToListAsync());
        Assert.All(await db.care.ToListAsync(), c => Assert.Null(c.task_flow_code));
        Assert.Equal(0, (await db.punchCard.AsNoTracking().SingleAsync()).punches);
    }

    [Fact]
    public async Task LegacyZeroWriteoffCommitsTasksAndCompletionTogether_AndRetriesAfterDatabaseFailure()
    {
        await using var f = await Fixture.Create(1);
        await PrepareLegacyCard(f, true, 0);
        await using var db = f.NewContext();
        await db.Database.ExecuteSqlRawAsync(FailCareTaskSql);
        var controller = new OrderController(db, f.Config, f.Http);
        await Assert.ThrowsAsync<DbUpdateException>(() => controller.WriteoffCareOrder(f.OrderId, "staff-session"));
        Assert.Equal(0, (await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).dealed);
        Assert.Empty(await db.careTask.ToListAsync()); Assert.Empty(await db.punchCardUsed.ToListAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_legacy_care_tasks;");
        var retry = await controller.WriteoffCareOrder(f.OrderId, "staff-session");
        var success = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(retry.Result).Value);
        Assert.Equal(0, success.code); Assert.Equal(1, success.data!.dealed);
        Assert.Single(await db.punchCardUsed.ToListAsync());
    }

    [Fact]
    public async Task LegacyPaidCareCallbackRollsBackBusinessOnly_AndCanRetryWithoutChargingAgain()
    {
        await using var f = await Fixture.Create(1);
        await PrepareLegacyCard(f, true, 0);
        var order = (await f.Db.order.FindAsync(f.OrderId))!; order.total_amount = 100; order.paying_amount = 100;
        var payment = new OrderPayment { order_id = f.OrderId, amount = 100, member_id = 10, pay_method = "微信支付", status = "支付成功" };
        f.Db.orderPayment.Add(payment); await f.Db.SaveChangesAsync();
        await using var db = f.NewContext();
        await db.Database.ExecuteSqlRawAsync(FailCareTaskSql);
        var controller = new OrderController(db, f.Config, f.Http);
        await Assert.ThrowsAsync<DbUpdateException>(() => controller.DealSuccessPaidOrder(f.OrderId, payment.id));
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order", "test", f.OrderId, "旧支付失败日志", null, null, null, null, "test"));
        await db.SaveChangesAsync();
        Assert.Equal(0, (await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).dealed);
        Assert.Empty(await db.careTask.ToListAsync()); Assert.Empty(await db.punchCardUsed.ToListAsync());
        Assert.Equal("支付成功", (await db.orderPayment.SingleAsync()).status);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_legacy_care_tasks;");
        await controller.DealSuccessPaidOrder(f.OrderId, payment.id);
        Assert.Equal(1, (await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).dealed);
        Assert.Single(await db.punchCardUsed.ToListAsync()); Assert.Single(await db.orderPayment.ToListAsync());
    }

    private const string FailCareTaskSql = "CREATE TRIGGER fail_legacy_care_tasks BEFORE INSERT ON care_task WHEN NEW.task_name = '发板' BEGIN SELECT RAISE(ABORT, 'injected legacy task failure'); END;";

    [Fact]
    public async Task PendingCardPurchaseCannotChangeEquipmentBeforePayment()
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        await f.Service.SubmitAsync(f.OrderId, 28);
        var care = await f.Db.care.AsNoTracking().SingleAsync();
        care.equipment = "单板";
        var result = await new CareController(f.Db, f.Config, f.Http).UpdateCareByStaff(care, "修改装备", "staff-session");
        var envelope = Assert.IsType<ApiResult<Care?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, envelope.code);
        Assert.Equal("双板", (await f.Db.care.AsNoTracking().SingleAsync()).equipment);
    }

    [Fact]
    public async Task CardSelectionStaysDraft_DefaultsAllEquipment_AndNewEquipmentUsesDefaults()
    {
        await using var f = await Fixture.Create(2);
        foreach (var care in await f.Db.care.ToListAsync()) { care.need_edge = 1; care.need_wax = 0; }
        await f.Db.SaveChangesAsync();
        var trial = await f.Service.PreviewAsync(f.Request());
        Assert.All(trial.projects, p => { Assert.Equal(1, p.need_edge); Assert.Equal(1, p.need_wax); });
        Assert.All(await f.Db.care.ToListAsync(), c => Assert.Equal(0, c.need_wax)); // 试算不改项目。
        await f.Service.SaveDraftAsync(f.Request());
        Assert.All(await f.Db.care.ToListAsync(), c => { Assert.Equal(1, c.need_edge); Assert.Equal(1, c.need_wax); });
        Assert.All(await f.Db.order.ToListAsync(), o => { Assert.Equal(0, o.valid); Assert.Equal(1, o.recepting); });
        Assert.Empty(await f.Db.careTask.ToListAsync()); Assert.Empty(await f.Db.punchCard.ToListAsync());
        // 同一张双项卡允许店员后续只做其中一项，新增装备仍默认双项。
        (await f.Db.care.OrderBy(c => c.id).FirstAsync()).need_wax = 0;
        f.Db.care.Add(new Care { order_id = f.OrderId, equipment = "双板", brand = "新增", scale = "160", need_edge = 1 });
        await f.Db.SaveChangesAsync();
        var saved = await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal(0, saved.projects.First().need_wax);
        Assert.Equal(1, saved.projects.Last().need_wax);
        Assert.Equal(3, saved.items.Single().punches);
    }

    [Fact]
    public async Task SingleCardCanBeSelectedBeforeProjects_ButAllEquipmentMustChooseServicesBeforeSubmit()
    {
        await using var f = await Fixture.Create(2, "单项养护10次卡");
        (await f.Db.product.SingleAsync()).care_project_count = 1;
        foreach (var care in await f.Db.care.ToListAsync()) care.need_edge = care.need_wax = 0;
        await f.Db.SaveChangesAsync();
        var draft = await f.Service.SaveDraftAsync(f.Request());
        Assert.False(draft.canCheckout); Assert.Equal(2, draft.errors.Count);
        Assert.All(await f.Db.care.ToListAsync(), c => { Assert.Equal(0, c.need_edge); Assert.Equal(0, c.need_wax); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.OrderId, 28));
        var cares = await f.Db.care.OrderBy(c => c.id).ToListAsync();
        cares[0].need_edge = 1; await f.Db.SaveChangesAsync();
        Assert.False((await f.Service.SaveDraftAsync(f.Request())).canCheckout);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.OrderId, 28));
        cares[1].need_wax = 1; await f.Db.SaveChangesAsync();
        Assert.True((await f.Service.SaveDraftAsync(f.Request())).canCheckout);
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Equal(2, (await f.Db.punchCard.SingleAsync()).punches);
        Assert.Equal(1, cares[0].need_edge); Assert.Equal(0, cares[0].need_wax);
        Assert.Equal(0, cares[1].need_edge); Assert.Equal(1, cares[1].need_wax);
    }

    [Fact]
    public async Task MachineSeasonRequiresExactlyOneEquipment_AndOverridesPreviousProjects()
    {
        await using var f = await Fixture.Create(2, "机打蜡季卡", true);
        var request = f.Request(); request.selections[0].quantity = 2;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveDraftAsync(request));
        Assert.Contains("一套装备", error.Message); Assert.Single(await f.Db.order.ToListAsync());
        f.Db.care.Remove((await f.Db.care.OrderBy(c => c.id).ToListAsync()).Last());
        var care = await f.Db.care.FirstAsync(); care.need_edge = care.need_wax = 1; care.free_wax = 0;
        await f.Db.SaveChangesAsync();
        var preview = await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal("machine", preview.serviceMode);
        Assert.Equal(0, care.need_edge); Assert.Equal(0, care.need_wax); Assert.Equal(1, care.free_wax);
    }

    [Fact]
    public async Task EachOrderMayBuyOnlyOneProductType_ButCanBuyMultipleCardsOfThatType()
    {
        await using var f = await Fixture.Create(2);
        var request = f.Request(); request.selections.Add(new CareCardSelection { productId = 102 });
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveDraftAsync(request));
        Assert.Contains("一种卡", error.Message);
        request.selections.RemoveAt(1); request.selections[0].quantity = 2;
        var result = await f.Service.SaveDraftAsync(request);
        Assert.Equal(1200m, result.amount); Assert.Equal(2, result.items.Count);
        Assert.Equal(2, result.items.Sum(i => i.punches));
    }

    [Fact]
    public async Task OneCentCardAndOneCentRepairCollectTwoCents_OnlyOneCardIsGrantedAndUsed()
    {
        await using var f = await Fixture.Create(1, "测试单项次卡");
        var product = await f.Db.product.SingleAsync(); product.sale_price = 0.01; product.care_project_count = 1;
        var care = await f.Db.care.SingleAsync(); care.need_wax = 0;
        care.need_repair = 1; care.repair_memo = "修磨刃"; care.repair_charge = 0.01;
        await f.Db.SaveChangesAsync();
        var trial = await f.Service.SaveDraftAsync(f.Request());
        Assert.True(trial.canCheckout); Assert.Equal(0.01m, trial.cardAmount);
        Assert.Equal(0.01m, trial.careAmount); Assert.Equal(0.02m, trial.amount);
        Assert.Equal(1, trial.items.Single().punches);
        Assert.Equal(0, trial.projects.Single().common_charge);
        Assert.Equal(0.01, care.repair_charge);
        var row = await f.Db.retail.SingleAsync();
        Assert.Equal(f.OrderId, row.order_id); Assert.Equal(0.01, row.deal_price);
        Assert.Equal(0.01, care.repair_charge);
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        var retail = (await f.Db.order.FindAsync(retailId))!;
        Assert.Equal(0.02, retail.total_amount); Assert.Equal(0.02, retail.paying_amount);
        Assert.Equal(f.OrderId, retailId);
        Assert.Single(await f.Db.order.ToListAsync());
        Assert.Contains("一次付清", (await CareCardSaleService.PaymentErrorAsync(f.Db, retail, 0.01, "微信支付"))!);
        await f.Pay(retailId, 0.01);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompletePaidAsync(retailId, f.PaymentId));
        Assert.Empty(await f.Db.punchCard.ToListAsync()); Assert.Empty(await f.Db.careTask.ToListAsync());
        (await f.Db.orderPayment.FindAsync(f.PaymentId))!.amount = 0.02; await f.Db.SaveChangesAsync();
        await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Single(await f.Db.orderPayment.ToListAsync());
        Assert.Equal(1, (await f.Db.punchCard.SingleAsync()).punches);
        Assert.Single(await f.Db.punchCardUsed.ToListAsync());
        Assert.Single(await f.Db.careTask.Where(t => t.task_name == "维修").ToListAsync());
        Assert.NotNull((await f.Db.retail.SingleAsync()).punch_card_id);
        var history = JObject.FromObject(await f.Service.GetDraftAsync(f.OrderId));
        Assert.Equal(0.02m, (decimal?)history["preview"]?["amount"]);
        Assert.Equal(0.01m, (decimal?)history["preview"]?["careAmount"]);
        Assert.Single((JArray)history["preview"]!["items"]!);
        var info = await CareCardSaleService.GetInfoAsync(f.Db, retail);
        Assert.Equal(0.01m, info!.careAmount); Assert.Equal(0.01m, info.cardAmount);
        int finish = await f.Db.careTask.Where(t => t.care_id == care.id && t.task_name == "发板").Select(t => t.id).SingleAsync();
        await new CareController(f.Db, f.Config, f.Http).SetTaskStatus(finish, "已完成", "测试取消", "staff-session", isCancel: true, cancelReason: "取消本件");
        Assert.Equal(0, (await f.Db.punchCard.SingleAsync()).punches);
        Assert.Equal(0.01, (await f.Db.retail.SingleAsync()).deal_price);
        Assert.Equal(0.02, (await f.Db.order.SingleAsync()).total_amount);
    }

    [Theory]
    [InlineData(0, 0.02, 0.03)]
    [InlineData(1, 0.05, 0.06)]
    public async Task SingleCardAllowsBothProjects_CollectsPackageDifferenceAndUrgentCharge(int urgent, double extra, double total)
    {
        await using var f = await Fixture.Create(1, "单项养护次卡");
        var card = await f.Db.product.SingleAsync(); card.sale_price = 0.01; card.care_project_count = 1;
        var care = await f.Db.care.SingleAsync(); care.urgent = urgent;
        await AddCarePrices(f);
        var preview = await f.Service.SaveDraftAsync(f.Request());
        Assert.True(preview.canCheckout);
        Assert.Equal(1, care.need_edge); Assert.Equal(1, care.need_wax);
        Assert.Equal(extra, care.common_charge); Assert.Equal((decimal)extra, preview.careAmount);
        Assert.Equal((decimal)total, preview.amount);
        Assert.Equal(1, preview.items.Single().punches);
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId, total); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Equal(1, (await f.Db.punchCard.SingleAsync()).punches);
        Assert.Single(await f.Db.careTask.Where(t => t.task_name == "修刃").ToListAsync());
        Assert.Single(await f.Db.careTask.Where(t => t.task_name == "热蜡").ToListAsync());
    }

    [Fact]
    public async Task RepairPriceChangedAfterTrialRequiresReconfirmation_AndDiscountIsPreserved()
    {
        await using var f = await Fixture.Create(1);
        var care = await f.Db.care.SingleAsync(); care.need_repair = 1; care.repair_charge = 10; care.discount = 2;
        await f.Db.SaveChangesAsync();
        var trial = await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal(8m, trial.careAmount); Assert.Equal(2, care.discount);
        care.repair_charge = 12; await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SubmitAsync(f.OrderId, 28));
        Assert.Contains("重新试算", error.Message);
        var revised = await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal(10m, revised.careAmount);
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId, 610); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Equal(2, care.discount); Assert.Equal(12, care.repair_charge);
    }

    [Fact]
    public async Task MissingUpgradePriceDoesNotTurnAdditionalProjectIntoFreeService()
    {
        await using var f = await Fixture.Create(1, "单项养护次卡");
        (await f.Db.product.SingleAsync()).care_project_count = 1; await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.PreviewAsync(f.Request()));
        Assert.Contains("未配置养护价格", error.Message);
        Assert.Empty(await f.Db.retail.ToListAsync());
    }

    [Fact]
    public async Task MachineSeasonAllowsAdditionalProjectAndCollectsExistingUpgradePrice()
    {
        await using var f = await Fixture.Create(1, "机打蜡季卡", true);
        (await f.Db.product.SingleAsync()).sale_price = 0.01;
        await AddCarePrices(f);
        await f.Service.SaveDraftAsync(f.Request());
        var care = await f.Db.care.SingleAsync(); care.need_edge = 1; await f.Db.SaveChangesAsync();
        var preview = await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal(1, care.free_wax); Assert.Equal(1, care.need_edge);
        Assert.Equal(0.03m, preview.careAmount); Assert.Equal(0.04m, preview.amount);
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(retailId, 0.04); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Single(await f.Db.punchCardUsed.ToListAsync());
        Assert.Equal("测试品牌", (await f.Db.punchCard.SingleAsync()).equip_brand);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task SubmitSetsPickupDateUsingActualOrderDayAndUrgency(int urgent, int days)
    {
        await using var f = await Fixture.Create(1);
        var order = (await f.Db.order.FindAsync(f.OrderId))!;
        order.biz_date = DateTime.Today.AddDays(-2);
        var care = await f.Db.care.SingleAsync(); care.urgent = urgent;
        if (urgent == 1) await AddCarePrices(f); // 加急补差使用现有门店价格。
        await f.Db.SaveChangesAsync();
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        Assert.Equal(DateTime.Today, order.biz_date.Date);
        Assert.Equal(order.biz_date.Date.AddDays(days), care.member_pick_date);
        await f.Pay(retailId, (await f.Db.order.FindAsync(retailId))!.total_amount);
        await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Equal(order.biz_date.Date.AddDays(days), care.member_pick_date);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingPendingOrderFillsMissingPickupDate_AndKeepsAnExplicitPickupDate(bool explicitDate)
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        int retailId = await f.Service.SubmitAsync(f.OrderId, 28);
        var order = (await f.Db.order.FindAsync(f.OrderId))!; order.biz_date = DateTime.Today.AddDays(-1);
        var care = await f.Db.care.SingleAsync();
        care.member_pick_date = explicitDate ? DateTime.Today.AddDays(5) : null;
        await f.Db.SaveChangesAsync();
        await f.Pay(retailId); await f.Service.CompletePaidAsync(retailId, f.PaymentId);
        Assert.Equal(explicitDate ? DateTime.Today.AddDays(5) : DateTime.Today, care.member_pick_date);
        Assert.Single(await f.Db.punchCardUsed.ToListAsync());
    }

    [Fact]
    public async Task SingleOrderCallbackLoadsBothDetailsAndRetailSearchFindsCardSale()
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        int id = await f.Service.SubmitAsync(f.OrderId, 28);
        await using var db = f.NewContext();
        var controller = new OrderController(db, f.Config, f.Http);
        var detail = await controller.GetOrder(id);
        Assert.Single(detail.cares); Assert.Single(detail.retails);
        Assert.Equal("", detail.retails.Single().memo);
        Assert.Equal(id, detail.careCardSale!.careOrderId);
        Assert.Equal(id, detail.careCardSale.retailOrderId);
        Assert.Equal(600m, detail.careCardSale.cardAmount);
        Assert.Single(await controller.GetRetailOrders(id));
        Assert.Single(await controller.GetCommonOrders(id, null, null, null, "零售", null, null));
        Assert.Single(await controller.GetCommonOrders(id, null, null, null, "养护", null, null));
        Assert.Single(await controller.GetCommonOrders(id, null, null, null, null, null, null));
        await f.Pay(id);
        await controller.DealSuccessPaidOrder(id, f.PaymentId);
        await controller.DealSuccessPaidOrder(id, f.PaymentId);
        Assert.Single(await db.order.ToListAsync());
        Assert.Single(await db.punchCard.ToListAsync());
        Assert.Single(await db.punchCardUsed.ToListAsync());
    }

    [Fact]
    public async Task OriginalCareDraftPayloadPreservesSavedCardAndCanRemoveItWithoutAnotherOrder()
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        await using (var db = f.NewContext())
        {
            var care = await db.care.SingleAsync();
            var posted = new SnowmeetApi.Models.Order { id = f.OrderId, type = "养护", shop = "测试店",
                member_id = 10, staff_id = 28, valid = 0, recepting = 1, cares = new() { care } };
            var response = await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
            var result = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(response.Result).Value);
            Assert.Equal(0, result.code);
            Assert.Equal(CareCardSaleService.SingleOrderSource, (await db.order.AsNoTracking().SingleAsync()).order_source);
            Assert.Single(await db.retail.ToListAsync());
            Assert.NotEmpty((await db.retail.SingleAsync()).memo);
            await new CareCardSaleService(db, f.Config, f.Http).SaveDraftAsync(new() { orderId = f.OrderId });
            Assert.Empty(await db.retail.ToListAsync());
            Assert.Null((await db.order.AsNoTracking().SingleAsync()).order_source);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.01)]
    public async Task ExistingTwoOrderPurchasesStillCompleteAndRetainTheirOriginalPayments(double careFee)
    {
        await using var f = await Fixture.Create(1);
        var care = await f.Db.care.SingleAsync(); care.repair_charge = careFee;
        await f.Db.SaveChangesAsync(); await f.Service.SaveDraftAsync(f.Request());
        var original = (await f.Db.order.FindAsync(f.OrderId))!;
        original.order_source = null; original.source_order_no = null; original.sub_type = "";
        original.total_amount = 0; original.paying_amount = 0;
        var legacy = new SnowmeetApi.Models.Order { type = "零售", sub_type = "养护购卡", shop = "测试店",
            member_id = 10, staff_id = 28, valid = 0, recepting = 1, wechat_unverified = true,
            order_source = CareCardSaleService.OrderSource, source_order_no = f.OrderId.ToString(),
            total_amount = 600 + careFee, paying_amount = 600 + careFee };
        f.Db.order.Add(legacy); await f.Db.SaveChangesAsync();
        (await f.Db.retail.SingleAsync()).order_id = legacy.id;
        if (careFee > 0) f.Db.retail.Add(new Retail { order_id = legacy.id, deal_price = careFee,
            retail_type = CareCardSaleService.CareChargeRetailType, order_type = "养护补费" });
        await f.Db.SaveChangesAsync();
        int id = await f.Service.SubmitAsync(f.OrderId, 28);
        Assert.Equal(legacy.id, id); Assert.NotEqual(f.OrderId, id);
        Assert.NotNull(await CareCardSaleService.PaymentErrorAsync(f.Db, original, 0, "微信支付"));
        await f.Pay(id, 600 + careFee);
        await using (var db = f.NewContext())
        {
            var controller = new OrderController(db, f.Config, f.Http);
            await controller.DealSuccessPaidOrder(id, f.PaymentId);
            await controller.DealSuccessPaidOrder(id, f.PaymentId);
            var info = await CareCardSaleService.GetInfoAsync(db, await db.order.FirstAsync(o => o.id == id));
            Assert.Equal((decimal)careFee, info!.careAmount);
            Assert.Equal(f.OrderId, info.careOrderId); Assert.Equal(id, info.retailOrderId);
            Assert.Equal(2, await db.order.CountAsync()); Assert.Single(await db.punchCardUsed.ToListAsync());
            Assert.Equal(id, (await db.orderPayment.SingleAsync()).order_id);
        }
    }

    [Fact]
    public async Task SingleOrderDraftCannotBePaidOrSubmittedThroughLegacyEntry_AndLateSaveCannotOverwriteSubmission()
    {
        await using var f = await Fixture.Create(1);
        await f.Service.SaveDraftAsync(f.Request());
        await using (var db = f.NewContext())
        {
            var order = await db.order.SingleAsync();
            Assert.Contains("先提交", (await CareCardSaleService.PaymentErrorAsync(db, order, 600, "微信支付"))!);
            var result = await new OrderController(db, f.Config, f.Http).PlaceOrder(order, "staff-session");
            var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.Equal(1, envelope.code);
        }
        await f.Service.SubmitAsync(f.OrderId, 28);
        await using (var db = f.NewContext())
        {
            var stale = new SnowmeetApi.Models.Order { id = f.OrderId, type = "养护", shop = "测试店", valid = 0, recepting = 1 };
            var result = await new CareController(db, f.Config, f.Http).SaveCareRecept(stale, "staff-session");
            var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
            Assert.Equal(1, envelope.code);
            var saved = await db.order.SingleAsync();
            Assert.Equal(1, saved.valid); Assert.Equal(0, saved.recepting); Assert.Equal(600, saved.total_amount);
            Assert.Single(await db.care.ToListAsync()); Assert.Single(await db.retail.ToListAsync());
        }
    }

    [Fact]
    public async Task ChangedSingleOrderCareFeeCannotIssueCardUntilOriginalAmountIsRestored()
    {
        await using var f = await Fixture.Create(1);
        var care = await f.Db.care.SingleAsync(); care.repair_charge = 0.01;
        await f.Db.SaveChangesAsync(); await f.Service.SaveDraftAsync(f.Request());
        await f.Service.SubmitAsync(f.OrderId, 28); await f.Pay(f.OrderId, 600.01);
        care.repair_charge = 0.02; await f.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CompletePaidAsync(f.OrderId, f.PaymentId));
        Assert.Contains("费用已变化", error.Message);
        Assert.Empty(await f.Db.punchCard.ToListAsync()); Assert.Empty(await f.Db.punchCardUsed.ToListAsync());
        Assert.Empty(await f.Db.careTask.ToListAsync());
        care = await f.Db.care.SingleAsync(); care.repair_charge = 0.01; f.Db.Update(care); await f.Db.SaveChangesAsync();
        await f.Service.CompletePaidAsync(f.OrderId, f.PaymentId);
        Assert.Single(await f.Db.punchCard.ToListAsync()); Assert.Single(await f.Db.punchCardUsed.ToListAsync());
    }

    [Theory]
    [InlineData(0.05, 0, 0.02)]
    [InlineData(0.06, 0.06, 0.01)]
    public async Task SwitchingFromOldCustomerTicketToCardRemovesCouponDiscountButKeepsLargerManualDiscount(
        double previousDiscount, double expectedDiscount, double amount)
    {
        await using var f = await Fixture.Create(1);
        await AddCarePrices(f);
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 16, biz_type = "养护", type = "养护券",
            name = "老顾客优惠券", memo = "", miniapp_recept_path = "" });
        f.Db.ticket.Add(new Ticket { code = "OLD-CUSTOMER", name = "老顾客优惠券", template_id = 16,
            member_id = 10, open_id = "customer-openid", valid = 1, is_active = 1 });
        var product = await f.Db.product.FirstAsync(p => p.id == 101); product.sale_price = 0.01;
        var care = await f.Db.care.SingleAsync(); care.ticket_code = "OLD-CUSTOMER";
        care.discount = previousDiscount; care.repair_charge = 0.01;
        await f.Db.SaveChangesAsync();
        var preview = await f.Service.PreviewAsync(f.Request());
        Assert.Equal(previousDiscount, care.discount); Assert.Equal("OLD-CUSTOMER", care.ticket_code);
        Assert.Equal((decimal)amount, preview.amount);
        Assert.Equal(expectedDiscount, preview.projects.Single().discount);
        await f.Service.SaveDraftAsync(f.Request());
        Assert.Null(care.ticket_code); Assert.Equal(expectedDiscount, care.discount);
        Assert.Equal((decimal)amount, (await f.Service.SaveDraftAsync(f.Request())).amount);
        int id = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(id, amount); await f.Service.CompletePaidAsync(id, f.PaymentId);
        Assert.Single(await f.Db.order.ToListAsync()); Assert.Single(await f.Db.punchCardUsed.ToListAsync());
        Assert.Equal(0, (await f.Db.ticket.SingleAsync()).used);
    }

    [Fact]
    public async Task SingleOrderSetsAndClearsBothSourceFieldsUnderProductionPairConstraint()
    {
        await using var f = await Fixture.Create(1);
        await Assert.ThrowsAsync<SqliteException>(() => f.Db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"order\" SET order_source={CareCardSaleService.SingleOrderSource} WHERE id={f.OrderId}"));
        await f.Service.SaveDraftAsync(f.Request());
        await f.Service.SaveDraftAsync(f.Request());
        var order = await f.Db.order.SingleAsync();
        Assert.Equal(CareCardSaleService.SingleOrderSource, order.order_source);
        Assert.Equal(f.OrderId.ToString(), order.source_order_no);
        Assert.Single(await f.Db.retail.ToListAsync());
        await f.Service.SaveDraftAsync(new() { orderId = f.OrderId });
        Assert.Null(order.order_source); Assert.Null(order.source_order_no);
        Assert.Empty(await f.Db.retail.ToListAsync());
        await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal(f.OrderId, await f.Service.SubmitAsync(f.OrderId, 28));
        Assert.Single(await f.Db.order.ToListAsync());
    }

    [Fact]
    public async Task SavingNewCardClearsEveryOldTicketAndMemberCard_OnlyNewCardIsUsedAfterPayment()
    {
        await using var f = await Fixture.Create(2);
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 12, biz_type = "养护", type = "养护券",
            name = "老券", memo = "", miniapp_recept_path = "" });
        f.Db.ticket.Add(new Ticket { code = "PREVIOUS", name = "老券", template_id = 12,
            member_id = 10, open_id = "customer-openid", valid = 1, is_active = 1 });
        f.Db.punchCard.Add(new PunchCard { id = 501, member_id = 10, biz_type = "养护", card_name = "旧会员卡",
            total = 10, punches = 0, care_project_count = 2 });
        var cares = await f.Db.care.OrderBy(c => c.id).ToListAsync();
        cares[0].ticket_code = "PREVIOUS"; cares[0].ticket_discount = 30;
        cares[1].use_card = true; cares[1].card_id = 501; cares[1].card_name = "旧会员卡";
        await f.Db.SaveChangesAsync();
        await f.Service.SaveDraftAsync(f.Request());
        Assert.All(await f.Db.care.ToListAsync(), c =>
        {
            Assert.Null(c.ticket_code); Assert.Equal(0, c.ticket_discount);
            Assert.False(c.use_card); Assert.Null(c.card_id); Assert.Null(c.card_name);
        });
        int id = await f.Service.SubmitAsync(f.OrderId, 28);
        await f.Pay(id); await f.Service.CompletePaidAsync(id, f.PaymentId);
        Assert.Single(await f.Db.order.ToListAsync()); Assert.Single(await f.Db.retail.ToListAsync());
        Assert.Equal(0, (await f.Db.punchCard.FirstAsync(c => c.id == 501)).punches);
        Assert.Equal(0, (await f.Db.ticket.SingleAsync()).used);
        Assert.All(await f.Db.punchCardUsed.ToListAsync(), u => Assert.NotEqual(501, u.card_id));
        var newCardId = (await f.Db.retail.SingleAsync()).punch_card_id;
        Assert.All(await f.Db.care.ToListAsync(), c => Assert.Equal(newCardId, c.card_id));
    }

    [Fact]
    public async Task OneCentSingleCardAndWanlongDoubleServiceShowSixtyYuanPackageDifference_AndRecalculateAfterProjectChanges()
    {
        await using var f = await Fixture.Create(1, "测试");
        await AddCarePrices(f);
        var card = await f.Db.product.FirstAsync(p => p.id == 101); card.sale_price = 0.01; card.care_project_count = 1;
        (await f.Db.product.FirstAsync(p => p.id == 201)).sale_price = 170;
        (await f.Db.product.FirstAsync(p => p.id == 202)).sale_price = 230;
        await f.Db.SaveChangesAsync();
        var doubleService = await f.Service.SaveDraftAsync(f.Request());
        Assert.Equal(60.01m, doubleService.amount);
        Assert.Equal(230m, doubleService.projects.Single().serviceAmount);
        Assert.Equal(170m, doubleService.projects.Single().cardCoveredAmount);
        Assert.Equal(60, doubleService.projects.Single().common_charge);
        var care = await f.Db.care.SingleAsync(); care.need_wax = 0; care.need_unwax = 0;
        await f.Db.SaveChangesAsync();
        Assert.Equal(0.01m, (await f.Service.SaveDraftAsync(f.Request())).amount);
        care.need_wax = 1; care.need_unwax = 1; care.repair_charge = 0.01;
        await f.Db.SaveChangesAsync();
        Assert.Equal(60.02m, (await f.Service.SaveDraftAsync(f.Request())).amount);
        care.discount = 10; await f.Db.SaveChangesAsync();
        Assert.Equal(50.02m, (await f.Service.SaveDraftAsync(f.Request())).amount);
    }

    [Theory]
    [InlineData("coupon")]
    [InlineData("card")]
    public async Task AuthorizedPhoneReceptionVerifiesCareOrder_AndProxyPaymentUsesOriginalMembersBenefits(string benefit)
    {
        await using var f = await Fixture.Create(1);
        await AddCarePrices(f);
        if (benefit == "card") await PrepareLegacyCard(f, false, 0);
        else
        {
            f.Db.ticketTemplate.Add(new TicketTemplate { id = 16, biz_type = "养护", type = "养护券",
                name = "老顾客优惠券", memo = "", miniapp_recept_path = "" });
            f.Db.ticket.Add(new Ticket { code = "OWNER-COUPON", name = "老顾客优惠券", template_id = 16,
                member_id = 10, open_id = "owner-openid", valid = 1, is_active = 1 });
            (await f.Db.care.SingleAsync()).ticket_code = "OWNER-COUPON";
        }
        var care = await f.Db.care.SingleAsync(); care.need_repair = 1; care.repair_charge = 0.01;
        f.Db.member.Add(new Member { id = 20, real_name = "代付人", gender = "男" });
        f.Db.miniSession.Add(new MiniSession { session_key = "payer-session", session_type = "wechat_mini_openid",
            member_id = 20, wechat_openid = "payer-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
        await PreparePhoneReception(f, true);
        await using var db = f.NewContext();
        var posted = await db.order.Include(o => o.cares).FirstAsync(o => o.id == f.OrderId);
        posted.wechat_unverified = false;
        var saved = await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
        var savedEnvelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(saved.Result).Value);
        Assert.Equal(0, savedEnvelope.code); Assert.True(savedEnvelope.data!.wechat_unverified);
        db.ChangeTracker.Clear(); // 每个接口调用对应独立 HTTP 请求的跟踪范围。
        var orders = new OrderController(db, f.Config, f.Http);
        var placed = await orders.PlaceCareOrder(f.OrderId, "staff-session");
        var placedEnvelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(placed.Result).Value);
        Assert.Equal(0, placedEnvelope.code); Assert.True(placedEnvelope.data!.wechat_unverified);
        Assert.Equal(0.01, placedEnvelope.data.total_amount, 2);
        db.ChangeTracker.Clear();
        var payment = new OrderPayment { order_id = f.OrderId, amount = 0.01, pay_method = "微信支付",
            status = "待支付", valid = 1 };
        db.orderPayment.Add(payment); await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var identity = new PayIdentity(db, f.Config);
        var checkedPayment = await identity.CheckPayerIdentity(payment.id, "wechat", "", "payer-session");
        var checkedEnvelope = Assert.IsType<ApiResult<PayIdentity.CheckPayerIdentityResult>>(Assert.IsType<OkObjectResult>(checkedPayment.Result).Value);
        Assert.Equal("choose_identity", checkedEnvelope.data.status);
        var confirmed = await identity.ConfirmPayIdentity(new() { paymentId = payment.id, payerType = "wechat",
            action = "choose", choice = "self", scannerId = "" }, "payer-session");
        var confirmedEnvelope = Assert.IsType<ApiResult<PayIdentity.CheckPayerIdentityResult>>(Assert.IsType<OkObjectResult>(confirmed.Result).Value);
        Assert.Equal(0, confirmedEnvelope.code); Assert.Equal("direct", confirmedEnvelope.data.status);
        db.ChangeTracker.Clear();
        payment = await db.orderPayment.AsTracking().FirstAsync(p => p.id == payment.id);
        Assert.True(payment.is_proxy_pay); Assert.Equal(20, payment.member_id);
        payment.status = "支付成功"; await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await orders.DealSuccessPaidOrder(f.OrderId, payment.id);
        var completed = await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId);
        Assert.Equal(10, completed.member_id); Assert.True(completed.wechat_unverified); Assert.Equal(1, completed.dealed);
        if (benefit == "coupon")
        {
            var used = await db.ticket.SingleAsync(); Assert.Equal(1, used.used); Assert.Equal(10, used.member_id);
        }
        else
        {
            var used = await db.punchCard.SingleAsync(); Assert.Equal(1, used.punches); Assert.Equal(10, used.member_id);
        }
    }

    [Fact]
    public async Task AuthorizedPhoneReceptionAllowsDepositWriteoffFromOriginalMemberWithoutWechatReverification()
    {
        await using var f = await Fixture.Create(1); await AddCarePrices(f);
        f.Db.depositAccount.Add(new DepositAccount { id = 1, member_id = 10, type = "服务储值", sub_type = "",
            deposit_no = "OWNER-DEPOSIT", income_amount = 10, consume_amount = 0, create_date = DateTime.Now });
        await PreparePhoneReception(f, true);
        await using var db = f.NewContext();
        var posted = await db.order.Include(o => o.cares).FirstAsync(o => o.id == f.OrderId);
        await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
        db.ChangeTracker.Clear();
        var orders = new OrderController(db, f.Config, f.Http);
        var placed = await orders.PlaceCareOrder(f.OrderId, "staff-session", useDeposit: true);
        var placedEnvelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(placed.Result).Value);
        Assert.Equal(0, placedEnvelope.code); Assert.True(placedEnvelope.data!.wechat_unverified);
        db.ChangeTracker.Clear();
        var writeoff = await orders.WriteoffCareOrder(f.OrderId, "staff-session");
        var envelope = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(writeoff.Result).Value);
        Assert.Equal(0, envelope.code); Assert.Equal(1, envelope.data!.dealed); Assert.Equal(10, envelope.data.member_id);
        Assert.Equal(0.05, (await db.depositAccount.AsNoTracking().SingleAsync()).consume_amount, 2);
        Assert.All(await db.depositBalance.ToListAsync(), b => Assert.Equal(10, b.member_id));
    }

    [Fact]
    public async Task PhoneReceptionCannotTrustPostedFlagBeforeApproval_AndChangingMemberClearsSavedVerification()
    {
        await using var f = await Fixture.Create(1); await PreparePhoneReception(f, false);
        await using (var db = f.NewContext())
        {
            var posted = await db.order.Include(o => o.cares).FirstAsync(o => o.id == f.OrderId);
            posted.wechat_unverified = true;
            await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
            Assert.False((await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).wechat_unverified);
            var checkin = await db.scanQrCode.AsTracking().SingleAsync(); checkin.authed = 1; await db.SaveChangesAsync();
        }
        await using (var db = f.NewContext())
        {
            var posted = await db.order.Include(o => o.cares).FirstAsync(o => o.id == f.OrderId);
            await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
            Assert.True((await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).wechat_unverified);
            var checkin = await db.scanQrCode.AsTracking().SingleAsync(); checkin.authed = 0; await db.SaveChangesAsync();
        }
        await using (var db = f.NewContext())
        {
            var posted = await db.order.Include(o => o.cares).FirstAsync(o => o.id == f.OrderId);
            await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
            Assert.True((await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).wechat_unverified);
        }
        await using (var db = f.NewContext())
        {
            var posted = await db.order.Include(o => o.cares).FirstAsync(o => o.id == f.OrderId);
            posted.member_id = 20;
            await new CareController(db, f.Config, f.Http).SaveCareRecept(posted, "staff-session");
            Assert.False((await db.order.AsNoTracking().FirstAsync(o => o.id == f.OrderId)).wechat_unverified);
        }
    }

    [Fact]
    public async Task CancelNowReturns17Only() => await AssertSummerCancellation("now", 17, 18);

    [Fact]
    public async Task CancelLaterReturns18Only() => await AssertSummerCancellation("later", 18, 17);

    private static async Task AssertSummerCancellation(string mode, int consumed, int other)
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, mode);
        AddCancellationTicket(f, "CONSUMED", consumed, careId);
        AddCancellationTicket(f, "OTHER", other, careId, used: 0);
        await f.Db.SaveChangesAsync();
        await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "CONSUMED", careId, f.OrderId);
        Assert.Equal(0, (await read.ticket.SingleAsync(t => t.code == "OTHER")).used);
        Assert.Single(await read.coreDataModLog.Where(l => l.table_name == "ticket").ToListAsync());
        Assert.Empty(await read.ticketLog.ToListAsync());
    }

    [Fact]
    public async Task CancelReturnsSelectedOrdinaryTicket()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, selected: "SELECTED");
        AddCancellationTicket(f, "SELECTED", 12, careId, sourceOrder: 991);
        f.Db.discount.Add(new Discount { order_id = f.OrderId, biz_type = "养护", biz_id = careId,
            ticket_code = "SELECTED", amount = 30, valid = 1 });
        await f.Db.SaveChangesAsync();
        await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "SELECTED", careId, f.OrderId);
        var order = await read.order.SingleAsync();
        Assert.Equal(330, order.total_amount); Assert.Equal(1, order.dealed);
        Assert.Equal(330, (await read.orderPayment.SingleAsync()).amount);
        Assert.Equal("支付成功", (await read.orderPayment.SingleAsync()).status);
        Assert.Equal(30, (await read.discount.SingleAsync()).amount);
        Assert.Equal(991, (await read.ticket.SingleAsync()).order_id);
    }

    [Theory]
    [InlineData("now", 17)]
    [InlineData("later", 18)]
    public async Task CancelReturnsSelectedExistingSummerTicket(string mode, int template)
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, mode, "SELECTED");
        AddCancellationTicket(f, "SELECTED", template, careId, sourceOrder: 991);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "SELECTED", careId, f.OrderId);
    }

    [Fact]
    public async Task CancelDoesNotReturnOtherEquipmentTicket()
    {
        await using var f = await Fixture.Create(2);
        var (careId, taskId) = await PrepareCouponCancellation(f, "now", "REUSED");
        int otherCare = await f.Db.care.Where(c => c.id != careId).Select(c => c.id).SingleAsync();
        AddCancellationTicket(f, "OWN", 17, careId);
        AddCancellationTicket(f, "REUSED", 17, otherCare);
        AddCancellationTicket(f, "OTHER-EQUIPMENT", 18, otherCare);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "OWN", careId, f.OrderId);
        Assert.All(await read.ticket.Where(t => t.code != "OWN").ToListAsync(), t => Assert.Equal(1, t.used));
    }

    [Fact]
    public async Task CancelDoesNotReturnCompletionGift()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, "now");
        AddCancellationTicket(f, "OWN", 17, careId);
        AddCancellationTicket(f, "COMPLETION-GIFT", 16, careId);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "OWN", careId, f.OrderId);
        Assert.Equal(1, (await read.ticket.SingleAsync(t => t.code == "COMPLETION-GIFT")).used);
    }

    [Fact]
    public async Task RepeatedCancelDoesNotWriteAgain()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, selected: "OWN", withCard: true);
        AddCancellationTicket(f, "OWN", 12, careId);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        var before = await CancellationSnapshot(read, careId, taskId);
        await CancelInNewContext(f, taskId, "另一个取消原因");
        Assert.Equal(before, await CancellationSnapshot(read, careId, taskId));
    }

    [Fact]
    public async Task RepeatedCancelAfterLegacyUseKeepsNewUse()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, selected: "OWN");
        AddCancellationTicket(f, "OWN", 12, careId);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using (var use = f.NewContext())
        {
            var ticket = await use.ticket.AsTracking().SingleAsync();
            Assert.Equal(0, ticket.used); // 先证明首次取消已退券。
            ticket.used = 1; ticket.used_time = new DateTime(2026, 10, 25, 18, 0, 0);
            await use.SaveChangesAsync(); // 模拟旧接口：不改 biz_id。
        }
        await using var read = f.NewContext();
        var before = await CancellationSnapshot(read, careId, taskId);
        await CancelInNewContext(f, taskId, "旧取消请求重试");
        Assert.Equal(before, await CancellationSnapshot(read, careId, taskId));
        Assert.Equal(1, (await read.ticket.SingleAsync()).used);
    }

    [Theory]
    [InlineData("now", 17, 18)]
    [InlineData("later", 18, 17)]
    public async Task CancelDoesNotReturnOppositeSummerTicketUsedElsewhere(string mode, int consumed, int other)
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, mode);
        AddCancellationTicket(f, "OWN", consumed, careId);
        AddCancellationTicket(f, "OTHER-USED", other, careId);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "OWN", careId, f.OrderId);
        var otherTicket = await read.ticket.SingleAsync(t => t.code == "OTHER-USED");
        Assert.Equal(1, otherTicket.used); Assert.Equal(CancellationUseTime, otherTicket.used_time);
    }

    [Fact]
    public async Task NormalFinishDoesNotReturnTickets()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, selected: "OWN", withCard: true);
        AddCancellationTicket(f, "OWN", 12, careId);
        await f.Db.SaveChangesAsync();
        await using (var db = f.NewContext())
            await new CareController(db, f.Config, f.Http).SetTaskStatus(taskId, "已完成", "正常发板", "staff-session");
        await using var read = f.NewContext();
        Assert.Equal(1, (await read.ticket.SingleAsync(t => t.code == "OWN")).used);
        Assert.Equal(1, (await read.punchCard.SingleAsync()).punches);
        Assert.True((await read.punchCardUsed.SingleAsync()).valid);
        Assert.False((await read.care.SingleAsync()).is_cancel);
        Assert.Empty(await read.coreDataModLog.Where(l => l.table_name == "ticket").ToListAsync());
    }

    [Fact]
    public async Task CancelPersistsWithNoTrackingContext()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, selected: "OWN", withCard: true);
        AddCancellationTicket(f, "OWN", 12, careId);
        await f.Db.SaveChangesAsync(); await CancelInNewContext(f, taskId);
        await using var read = f.NewContext();
        await AssertReturnedCoupon(read, "OWN", careId, f.OrderId);
        Assert.True((await read.care.SingleAsync()).is_cancel);
        Assert.Equal("测试取消原因", (await read.care.SingleAsync()).cancel_reason);
        Assert.Equal("已完成", (await read.careTask.SingleAsync()).status);
        Assert.Equal(0, (await read.punchCard.SingleAsync()).punches);
        Assert.False((await read.punchCardUsed.SingleAsync()).valid);
        Assert.Single(await read.coreDataModLog.Where(l => l.scene == "取消养护返次").ToListAsync());
    }

    [Fact]
    public async Task CancelSaveFailureRollsBackAllChanges()
    {
        await using var f = await Fixture.Create(1);
        var (careId, taskId) = await PrepareCouponCancellation(f, selected: "OWN", withCard: true);
        AddCancellationTicket(f, "OWN", 12, careId); await f.Db.SaveChangesAsync();
        await using var db = f.NewContext();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_cancel_audit BEFORE INSERT ON core_data_mod_log WHEN NEW.table_name = 'care' AND NEW.field_name = 'is_cancel' BEGIN SELECT RAISE(ABORT, 'injected cancellation audit failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => new CareController(db, f.Config, f.Http)
            .SetTaskStatus(taskId, "已完成", "取消养护", "staff-session", isCancel: true, cancelReason: "测试取消原因"));
        await using var read = f.NewContext();
        var original = await CancellationSnapshot(read, careId, taskId);
        Assert.False((await read.care.SingleAsync()).is_cancel);
        Assert.Equal(1, (await read.ticket.SingleAsync()).used);
        Assert.Equal(1, (await read.punchCard.SingleAsync()).punches);
        Assert.True((await read.punchCardUsed.SingleAsync()).valid);
        Assert.Equal("未开始", (await read.careTask.SingleAsync()).status);
        Assert.Empty(await read.coreDataModLog.ToListAsync());
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_cancel_audit;");
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order", "error", f.OrderId, "错误日志", null, null, null, null, "取消失败"));
        await db.SaveChangesAsync();
        Assert.Equal(original, await CancellationSnapshot(read, careId, taskId));
        await CancelInNewContext(f, taskId);
        await AssertReturnedCoupon(read, "OWN", careId, f.OrderId);
        Assert.Equal(0, (await read.punchCard.SingleAsync()).punches);
        Assert.Single(await read.coreDataModLog.Where(l => l.table_name == "ticket").ToListAsync());
    }

    private static readonly DateTime CancellationUseTime = new(2026, 10, 25, 12, 0, 0);

    private static async Task<(int CareId, int TaskId)> PrepareCouponCancellation(Fixture f,
        string? mode = null, string? selected = null, bool withCard = false)
    {
        var order = (await f.Db.order.FindAsync(f.OrderId))!;
        order.valid = 1; order.dealed = 1; order.recepting = 0; order.total_amount = 330;
        order.code = "TEST_YH_261025_00001";
        var care = await f.Db.care.OrderBy(c => c.id).FirstAsync();
        care.valid = 1; care.task_flow_code = "TEST-261025-001";
        care.biz_type = mode == null ? "雪季养护" : "非雪季养护";
        care.summer = mode; care.ticket_code = selected;
        var task = new CareTask { care_id = care.id, task_name = "发板", sort = 60 };
        f.Db.careTask.Add(task);
        f.Db.orderPayment.Add(new OrderPayment { order_id = order.id, amount = 330, pay_method = "微信支付", status = "支付成功", member_id = 10, valid = 1 });
        if (withCard)
        {
            f.Db.punchCard.Add(new PunchCard { id = 501, member_id = 10, biz_type = "养护", card_name = "测试次卡", total = 10, punches = 1 });
            f.Db.punchCardUsed.Add(new PunchCardUsed { card_id = 501, order_id = order.id, biz_type = "养护", biz_id = care.id, punch_count = 1 });
        }
        await f.Db.SaveChangesAsync();
        return (care.id, task.id);
    }

    private static void AddCancellationTicket(Fixture f, string code, int template, int careId, int used = 1, int? sourceOrder = null)
        => f.Db.ticket.Add(new Ticket { code = code, name = "测试券", open_id = "customer-openid", member_id = 10,
            template_id = template, valid = 1, is_active = 1, used = used, used_time = used == 1 ? CancellationUseTime : null,
            biz_type = "养护", biz_id = careId, order_id = sourceOrder ?? f.OrderId,
            start_date = new DateTime(2026, 10, 1), expire_date = new DateTime(2027, 1, 1) });

    private static async Task CancelInNewContext(Fixture f, int taskId, string reason = "测试取消原因")
    {
        await using var db = f.NewContext();
        var response = await new CareController(db, f.Config, f.Http)
            .SetTaskStatus(taskId, "已完成", "取消养护", "staff-session", isCancel: true, cancelReason: reason);
        var result = Assert.IsType<ApiResult<Care>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code); Assert.True(result.data!.is_cancel);
    }

    private static async Task AssertReturnedCoupon(ApplicationDBContext db, string code, int careId, int orderId)
    {
        var ticket = await db.ticket.SingleAsync(t => t.code == code);
        Assert.Equal(0, ticket.used); Assert.Null(ticket.used_time);
        Assert.Equal(new DateTime(2027, 1, 1), ticket.expire_date); Assert.Equal(1, ticket.valid); Assert.Equal(1, ticket.is_active);
        Assert.Equal(10, ticket.member_id); Assert.Equal(careId, ticket.biz_id);
        var log = Assert.Single(await db.coreDataModLog.Where(l => l.table_name == "ticket" && l.manual_memo!.Contains(code)).ToListAsync());
        Assert.Equal("used", log.field_name); Assert.Equal(0, log.key_value);
        Assert.Equal("1", log.prev_value); Assert.Equal("0", log.current_value);
        Assert.Equal(10, log.member_id); Assert.Equal(28, log.staff_id);
        Assert.Contains("care=" + careId, log.manual_memo); Assert.Contains("order=" + orderId, log.manual_memo);
        Assert.Contains("2026-10-25 12:00:00", log.manual_memo); Assert.Contains("测试取消原因", log.manual_memo);
    }

    private static async Task<string> CancellationSnapshot(ApplicationDBContext db, int careId, int taskId)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            care = await db.care.Where(c => c.id == careId).Select(c => new { c.is_cancel, c.cancel_reason, c.update_date }).SingleAsync(),
            task = await db.careTask.Where(t => t.id == taskId).Select(t => new { t.status, t.memo, t.update_date, t.end_time, t.staff_id }).SingleAsync(),
            tickets = await db.ticket.OrderBy(t => t.code).Select(t => new { t.code, t.used, t.used_time }).ToListAsync(),
            cards = await db.punchCard.OrderBy(c => c.id).Select(c => new { c.id, c.punches, c.update_date }).ToListAsync(),
            usages = await db.punchCardUsed.OrderBy(u => u.id).Select(u => new { u.id, u.valid, u.update_date }).ToListAsync(),
            logs = await db.coreDataModLog.Where(l => l.field_name != "error").OrderBy(l => l.id)
                .Select(l => new { l.id, l.table_name, l.field_name, l.prev_value, l.current_value, l.manual_memo }).ToListAsync()
        });

    [Theory]
    [InlineData(1, "测试", 1, 0, 0, 0)]
    [InlineData(1, "测试", 0, 1, 0, 0)]
    [InlineData(1, "测试", 1, 1, 0, 60)]
    [InlineData(1, "测试", 1, 0, 1, 50)]
    [InlineData(1, "测试", 1, 1, 1, 110)]
    [InlineData(2, "测试", 1, 1, 0, 0)]
    [InlineData(2, "测试", 1, 1, 1, 50)]
    [InlineData(null, "历史双项次卡", 1, 1, 0, 0)]
    [InlineData(null, "历史单项次卡", 1, 1, 0, 60)]
    [InlineData(1, "名称含双项但实际单项", 1, 1, 0, 60)]
    public async Task ExistingCardPreviewChargesOnlyServicesOutsideItsCoverage(int? projectCount, string name,
        int edge, int wax, int urgent, double expected)
    {
        await using var f = await Fixture.Create(1);
        await PrepareExistingCareCard(f, projectCount, name);
        var care = await f.Db.care.AsNoTracking().SingleAsync();
        care.need_edge = edge; care.need_wax = care.need_unwax = wax; care.urgent = urgent;
        var response = await new CareController(f.Db, f.Config, f.Http).CalcCareCharge(new()
            { shop = "测试店", memberId = 10, care = care }, "staff-session");
        var result = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(0, result.code);
        var data = JObject.FromObject(result.data);
        Assert.Equal(expected, data.Value<double>("commonCharge"));
        Assert.Equal(expected, data["care"]!.Value<double>("common_charge"));
        Assert.Equal(0, data.Value<double>("ticketDiscount"));
        Assert.Equal(0, (await f.Db.punchCard.SingleAsync()).punches);
        Assert.Empty(await f.Db.punchCardUsed.ToListAsync());
    }

    [Fact]
    public async Task ExistingSingleCardCheckoutRecalculatesUpgradeThenPaymentUsesOnePunchExactlyOnce()
    {
        await using var f = await Fixture.Create(1);
        await PrepareExistingCareCard(f);
        // 客户端保存的 0 元费用不能绕过正式下单重新计价。
        var care = await f.Db.care.SingleAsync(); care.common_charge = 0; await f.Db.SaveChangesAsync();
        await using var db = f.NewContext();
        var orders = new OrderController(db, f.Config, f.Http);
        var placed = await orders.PlaceCareOrder(f.OrderId, "staff-session");
        var result = Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(placed.Result).Value);
        Assert.Equal(0, result.code); Assert.Equal(60, result.data!.total_amount); Assert.Equal(60, result.data.paying_amount);
        Assert.Equal(60, (await db.care.AsNoTracking().SingleAsync()).common_charge);
        Assert.Empty(await db.punchCardUsed.ToListAsync()); Assert.Empty(await db.careTask.ToListAsync());
        await f.Pay(f.OrderId, 60);
        (await f.Db.orderPayment.FindAsync(f.PaymentId))!.member_id = 10; await f.Db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await orders.DealSuccessPaidOrder(f.OrderId, f.PaymentId);
        db.ChangeTracker.Clear();
        await orders.DealSuccessPaidOrder(f.OrderId, f.PaymentId);
        Assert.Equal(1, (await db.punchCard.AsNoTracking().SingleAsync()).punches);
        Assert.Equal(1, (await db.punchCardUsed.AsNoTracking().SingleAsync(u => u.valid)).punch_count);
        Assert.Equal(1, (await db.order.AsNoTracking().SingleAsync()).dealed);
        Assert.NotEmpty(await db.careTask.ToListAsync());
    }

    [Fact]
    public async Task ExistingCardWithUnpaidUpgradeCannotUseZeroAmountWriteoff()
    {
        await using var f = await Fixture.Create(1);
        await PrepareExistingCareCard(f);
        var order = (await f.Db.order.FindAsync(f.OrderId))!;
        order.valid = 1; order.recepting = 0; order.code = "TEST_YH_261026_00001";
        order.total_amount = 60; order.paying_amount = 60;
        (await f.Db.care.SingleAsync()).valid = 1; await f.Db.SaveChangesAsync();
        await using var db = f.NewContext();
        var response = await new OrderController(db, f.Config, f.Http).WriteoffCareOrder(f.OrderId, "staff-session");
        var result = Assert.IsType<ApiResult<SnowmeetApi.Models.Order?>>(Assert.IsType<OkObjectResult>(response.Result).Value);
        Assert.Equal(1, result.code); Assert.Contains("支付", result.message);
        Assert.Equal(0, (await db.punchCard.AsNoTracking().SingleAsync()).punches);
        Assert.Equal(0, (await db.order.AsNoTracking().SingleAsync()).dealed);
        Assert.Empty(await db.punchCardUsed.ToListAsync()); Assert.Empty(await db.careTask.ToListAsync());
    }

    [Theory]
    [InlineData(201)]
    [InlineData(202)]
    public async Task ExistingSingleCardMissingUpgradePricesRejectsPreviewAndPlacement(int missingProductId)
    {
        await using var f = await Fixture.Create(1);
        await PrepareExistingCareCard(f);
        f.Db.product.Remove(await f.Db.product.FindAsync(missingProductId) ?? throw new InvalidOperationException());
        await f.Db.SaveChangesAsync();
        await using var db = f.NewContext();
        var care = await db.care.SingleAsync();
        var preview = await new CareController(db, f.Config, f.Http).CalcCareCharge(new()
            { shop = "测试店", memberId = 10, care = care }, "staff-session");
        var previewResult = Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(preview.Result).Value);
        Assert.Equal(1, previewResult.code); Assert.Contains("价格", previewResult.message);
        db.ChangeTracker.Clear();
        var placed = await new OrderController(db, f.Config, f.Http).PlaceCareOrder(f.OrderId, "staff-session");
        var placedResult = Assert.IsType<ApiResult<SnowmeetApi.Models.Order>>(Assert.IsType<OkObjectResult>(placed.Result).Value);
        Assert.Equal(1, placedResult.code); Assert.Contains("价格", placedResult.message);
        Assert.Equal(0, (await db.order.AsNoTracking().SingleAsync()).valid);
        Assert.Empty(await db.punchCardUsed.ToListAsync()); Assert.Empty(await db.careTask.ToListAsync());
    }

    [Fact]
    public async Task ExistingMachineSeasonKeepsItsConfiguredUpgradePrice()
    {
        await using var f = await Fixture.Create(1, "机打蜡季卡", true);
        await PrepareExistingCareCard(f, 1, "机打蜡季卡");
        (await f.Db.punchCard.SingleAsync()).total = null;
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 12, biz_type = "养护", type = "养护券",
            name = "免费打蜡", memo = "", miniapp_recept_path = "" });
        f.Db.productTicketTemplate.Add(new ProductTicketTemplate { ticket_template_id = 12, product_id = 201, fixed_price = 50, valid = true });
        await f.Db.SaveChangesAsync();
        var helper = new CareController(f.Db, f.Config, f.Http);
        var card = await f.Db.punchCard.SingleAsync();
        var care = await f.Db.care.SingleAsync();
        Assert.Equal(0, (await helper.CalcCharge("测试店", care, null, card)).commonCharge);
        care.free_wax = 0; care.need_wax = care.need_unwax = 1;
        Assert.Equal(50, (await helper.CalcCharge("测试店", care, null, card)).commonCharge);
    }

    private static async Task PrepareExistingCareCard(Fixture f, int? projectCount = 1, string name = "测试")
    {
        await AddCarePrices(f); await PrepareLegacyCard(f, false, 0);
        var card = await f.Db.punchCard.SingleAsync(); card.care_project_count = projectCount; card.card_name = name;
        (await f.Db.product.FindAsync(201))!.sale_price = 170;
        (await f.Db.product.FindAsync(202))!.sale_price = 230;
        (await f.Db.product.FindAsync(203))!.sale_price = 280;
        f.Db.product.Add(new Product { id = 204, name = "单项加急", shop_id = 1,
            category_code = CareProductRules.CareCategoryCode, sale_price = 220, valid = 1 });
        await f.Db.SaveChangesAsync();
    }

    private static async Task PreparePhoneReception(Fixture f, bool authorized)
    {
        var order = (await f.Db.order.FindAsync(f.OrderId))!; order.wechat_unverified = false;
        (await f.Db.staff.FirstAsync(s => s.id == 28)).title_level = 100;
        f.Db.scanQrCode.Add(new ScanQrCode { id = 700, code = ReceptCheckinRules.BuildCode("abcdefghij", 700),
            platform = "wechat_mini", scene = "店铺接待", purpose = "顾客扫码进小程序", staff_id = 28,
            scaner_member_id = 10, scaned = 0, cell = "13800138000", authed = authorized ? 1 : 0,
            create_date = DateTime.Now, update_date = DateTime.Now });
        await f.Db.SaveChangesAsync();
    }

    private static async Task AddCarePrices(Fixture f)
    {
        f.Db.shop.Add(new Shop { id = 1, name = "测试店", code = "TEST", beacon_mac = "", beacon_uuid = "" });
        f.Db.category.Add(new Category { id = 11, code = CareProductRules.CareCategoryCode, name = "养护服务", biz_type = "养护", valid = 1 });
        f.Db.product.AddRange(
            new Product { id = 201, name = "单项", shop_id = 1, category_code = CareProductRules.CareCategoryCode, sale_price = 0.03, valid = 1 },
            new Product { id = 202, name = "双项", shop_id = 1, category_code = CareProductRules.CareCategoryCode, sale_price = 0.05, valid = 1 },
            new Product { id = 203, name = "双项加急", shop_id = 1, category_code = CareProductRules.CareCategoryCode, sale_price = 0.08, valid = 1 });
        await f.Db.SaveChangesAsync();
    }

    private static async Task PrepareLegacyCard(Fixture f, bool placed, int punches)
    {
        var order = (await f.Db.order.FindAsync(f.OrderId))!;
        order.valid = placed ? 1 : 0; order.recepting = placed ? 0 : 1; order.paying_amount = 0;
        order.code = placed ? "TEST_YH_261024_00001" : null;
        f.Db.punchCard.Add(new PunchCard { id = 501, member_id = 10, biz_type = "养护", card_name = "双项养护10次卡",
            total = 10, punches = punches, care_project_count = 2 });
        foreach (var care in await f.Db.care.ToListAsync()) { care.valid = placed ? 1 : 0; care.use_card = true; care.card_id = 501; }
        await f.Db.SaveChangesAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ApplicationDBContext Db { get; }
        public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["CareCardSale:Enabled"] = "true", ["Settings:AppId"] = "local-test-app" }).Build();
        public IHttpContextAccessor Http { get; } = new TestHttpContextAccessor();
        public CareCardSaleService Service => new(Db, Config, Http);
        public ApplicationDBContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
        public int OrderId { get; private set; }
        public int PaymentId { get; private set; }
        private Fixture(SqliteConnection connection, ApplicationDBContext db) { this.connection = connection; Db = db; }
        public CareCardSaleRequest Request() => new() { orderId = OrderId, selections = new() { new() { productId = 101, quantity = 1 } } };

        public static async Task<Fixture> Create(int count, string name = "双项养护10次卡", bool season = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var db = new ApplicationDBContext(new DbContextOptionsBuilder<ApplicationDBContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            // SQLite EnsureCreated 不带生产现有 CHECK，显式复现其成对字段要求。
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER CK_order_source_pair_insert BEFORE INSERT ON "order"
                WHEN (NEW.order_source IS NULL) <> (NEW.source_order_no IS NULL)
                BEGIN SELECT RAISE(ABORT, 'CK_order_source_pair'); END;
                CREATE TRIGGER CK_order_source_pair_update BEFORE UPDATE ON "order"
                WHEN (NEW.order_source IS NULL) <> (NEW.source_order_no IS NULL)
                BEGIN SELECT RAISE(ABORT, 'CK_order_source_pair'); END;
                """);
            var f = new Fixture(connection, db);
            db.member.Add(new Member { id = 10, real_name = "顾客", gender = "男" });
            db.Set<Staff>().Add(new Staff { id = 28, title_level = 300, valid = 1 });
            db.Set<SocialAccountForJob>().Add(new SocialAccountForJob { id = 1, member_id = 10, wechat_mini_openid = "staff-openid" });
            db.Set<StaffSocialAccount>().Add(new StaffSocialAccount { staff_id = 28, social_account_id = 1, valid = 1 });
            db.miniSession.Add(new MiniSession { session_key = "staff-session", session_type = "wechat_mini_openid", member_id = 10,
                wechat_openid = "staff-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.miniSession.Add(new MiniSession { session_key = "customer-session", session_type = "wechat_mini_openid", member_id = 10,
                wechat_openid = "customer-openid", valid = 1, expire_date = DateTime.Now.AddHours(1) });
            db.category.Add(new Category { id = 10, code = season ? "0202" : "0201", name = season ? "季卡" : "次卡", biz_type = "养护", valid = 1 });
            db.product.Add(new Product { id = 101, name = name, category_code = season ? "0202" : "0201", sale_price = 600,
                punch_total = season ? null : 10, care_project_count = 2, on_shelves = 1, valid = 1 });
            var order = new SnowmeetApi.Models.Order { type = "养护", shop = "测试店", member_id = 10, staff_id = 28, valid = 0, recepting = 1,
                wechat_unverified = true, name = "顾客", gender = "男" };
            db.order.Add(order); await db.SaveChangesAsync(); f.OrderId = order.id;
            for (int n = 0; n < count; n++) db.care.Add(new Care { order_id = order.id, equipment = "双板", brand = "测试品牌", scale = "170",
                need_edge = name.Contains("机打蜡") ? 0 : 1, need_wax = name.Contains("机打蜡") ? 0 : 1,
                free_wax = name.Contains("机打蜡") ? 1 : 0, biz_type = "雪季养护", valid = 0 });
            await db.SaveChangesAsync(); return f;
        }

        public async Task Pay(int retailId, double amount = 600)
        {
            var payment = new OrderPayment { order_id = retailId, amount = amount, pay_method = "微信支付", status = "支付成功", member_id = 999, is_proxy_pay = false };
            Db.orderPayment.Add(payment); await Db.SaveChangesAsync(); PaymentId = payment.id;
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }

    // Keep the request available after the asynchronous fixture factory returns.
    private sealed class TestHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }
}
