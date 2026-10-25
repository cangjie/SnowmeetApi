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
        original.order_source = null; original.sub_type = ""; original.total_amount = 0; original.paying_amount = 0;
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
