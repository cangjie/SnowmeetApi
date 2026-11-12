using SnowmeetApi.Controllers.IntroducerShare;
using SnowmeetApi.Controllers;
using SnowmeetApi.Services.Share;
using SnowmeetApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace SnowmeetApi.Tests;

public class ShareWorkflowPermissionTests
{
    [Fact]
    public async Task Snow_ticket_share_created_before_staff_departure_must_not_be_submitted()
    {
        await using var db = await ShareFixture.NewDbAsync();
        db.staff.Add(new Staff { id = 28, name = "离职员工", valid = 0, title_level = 100 });
        db.order.Add(new Order { id = 1, code = "TICKET-1", type = "雪票", valid = 1 });
        db.orderShareRelation.Add(new OrderShareRelation { id = 201, staff_id = 28, type = "个人", name = "员工", valid = true });
        db.orderShare.Add(new OrderShare { id = 91, order_id = 1, relation_id = 201, amount = 1, valid = true });
        db.orderPayment.Add(new OrderPayment { id = 4, order_id = 1, valid = 1, status = "支付成功", pay_method = "现金" });
        db.paymentShare.Add(new PaymentShare { id = 901, payment_id = 4, share_id = 91, valid = true, amount = 1, out_trade_no = "TEST-901" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var original = await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 901);
        var result = await new OrderShareController(db, ShareFixture.Config, ShareFixture.Http).SharePayment(original);
        Assert.Null(result.submit_time);
        Assert.Null((await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == 901)).submit_time);
        var page = await new ShareListService(db).ListAsync(new ShareListQuery(null, null, null, null, null, null));
        var row = Assert.Single(page.items);
        Assert.Equal("已停止", row.channelState);
        Assert.Contains("离职", row.channelMessage);
    }

    [Fact]
    public async Task Store_manager_cannot_record_a_bank_transfer()
    {
        await using var db = await ShareFixture.NewDbAsync();
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var controller = new ShareListController(db, ShareFixture.Config, ShareFixture.Http);

        var response = await controller.CompleteManual("manager-s", new(new List<int> { 1 }, "MS-TEST"));

        Assert.Equal(3, response.code);
    }

    [Fact]
    public async Task New_automatic_distributor_share_waits_for_admin_approval()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);

        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, DateTime.Today, true);

        Assert.Equal("待审核", view.state);
    }

    [Fact]
    public async Task Cash_order_only_offers_bank_transfer()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("现金", 5000, 0, 400);
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var orderController = new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http);

        var data = JsonSerializer.SerializeToElement((await orderController.GetOrderShares("manager-s", 1)).data);

        Assert.True(data.GetProperty("shareMethodSupported").GetBoolean());
        Assert.False(data.GetProperty("channelShareAvailable").GetBoolean());
    }

    [Fact]
    public async Task Server_rejects_channel_sharing_for_a_payment_without_share_capability()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var payment = await db.orderPayment.AsNoTracking().SingleAsync(p => p.id == 4);
        payment.need_share = 0;
        db.orderPayment.Update(payment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var introducer = await ShareFixture.AddIntroducerAsync(db);
        var body = JsonSerializer.Deserialize<OrderIntroducerShareController.SetShareInput>(
            $"{{\"orderId\":1,\"introducerId\":{introducer.id},\"amount\":40,\"shareMethod\":\"manual\"}}")!;

        var result = await new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http).SetShare("manager-s", body);

        Assert.Equal(1, result.code);
        Assert.Empty(await db.orderShare.Where(s => s.relation_id == introducer.relation_id).ToListAsync());
    }

    [Fact]
    public async Task Manual_payment_channel_choice_is_persisted_separately_from_bank_transfer()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var body = JsonSerializer.Deserialize<OrderIntroducerShareController.SetShareInput>(
            $"{{\"orderId\":1,\"introducerId\":{introducer.id},\"amount\":40,\"shareMethod\":\"manual\"}}")!;

        var result = await new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http).SetShare("manager-s", body);

        Assert.Equal(0, result.code);
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.relation_id == introducer.relation_id && s.valid);
        Assert.Equal("manual", share.share_method);
        Assert.False(share.auto_share);
        Assert.Null(share.batch_id);
    }

    [Fact]
    public async Task Nightly_job_waits_for_approval_but_keeps_system_rental_share()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var introducer = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "系统收款方", valid = true });
        db.orderShare.Add(new OrderShare { id = 90, order_id = 1, relation_id = 4, amount = 10, valid = true, create_date = DateTime.Now });
        db.paymentShare.Add(new PaymentShare { id = 900, share_id = 90, payment_id = 4, amount = 10, valid = true, out_trade_no = "SYSTEM-900" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var created = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, introducer.id, 40, null, DateTime.Today, true);
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);

        Assert.Equal(new[] { 900 }, (await helper.GetDueWanlongShares(DateTime.Today.AddDays(-1))).Select(x => x.id).ToArray());
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == created.orderShareId);
        share.approved_by_staff_id = 303;
        share.approved_at = DateTime.Now;
        db.orderShare.Update(share);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Contains(await helper.GetDueWanlongShares(DateTime.Today.AddDays(-1)), s => s.share_id == share.id);
    }

    [Fact]
    public async Task Channel_manual_share_cannot_be_completed_as_a_bank_batch()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "manual");

        await Assert.ThrowsAsync<ShareException>(() => new ShareListService(db)
            .CompleteManualAsync(ShareFixture.Manager(300), new[] { view.orderShareId }, "MS-NO-MANUAL"));
        Assert.Null((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId)).batch_id);
    }

    [Fact]
    public async Task Only_admin_can_approve_and_editing_an_application_clears_approval()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var orderService = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);
        var view = await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, DateTime.Today, true);
        var service = new ShareListService(db);
        var approve = typeof(ShareListService).GetMethod("ApproveAutoAsync");
        Assert.NotNull(approve);
        Task Invoke(Staff op) => (Task)approve!.Invoke(service, new object[] { op, view.orderShareId })!;

        Assert.Equal(3, (await Assert.ThrowsAsync<ShareException>(() => Invoke(ShareFixture.Manager()))).Code);
        await Invoke(ShareFixture.Manager(300));
        var approved = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId);
        Assert.Equal(1, approved.approved_by_staff_id);
        Assert.NotNull(approved.approved_at);
        await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 45, null, DateTime.Today, true);
        Assert.Null((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId)).approved_at);
    }

    [Fact]
    public async Task Manual_channel_submission_is_admin_only_claimed_once_and_has_no_bank_batch()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "manual");
        int calls = 0;
        Func<PaymentShare, Task<PaymentShare>> dispatch = async row => {
            calls++;
            Assert.NotNull(row.submit_time);
            row.success = true;
            row.response_time = DateTime.Now;
            db.paymentShare.Update(row);
            await db.SaveChangesAsync();
            return row;
        };
        var type = typeof(ShareListService).Assembly.GetType("SnowmeetApi.Services.Share.ShareChannelSubmitService");
        Assert.NotNull(type);
        var service = Activator.CreateInstance(type!, db, dispatch)!;
        var submit = type!.GetMethod("SubmitManualAsync");
        Assert.NotNull(submit);
        Task Invoke(Staff op) => (Task)submit!.Invoke(service, new object[] { op, view.orderShareId })!;

        Assert.Equal(3, (await Assert.ThrowsAsync<ShareException>(() => Invoke(ShareFixture.Manager()))).Code);
        await Invoke(ShareFixture.Manager(300));
        await Assert.ThrowsAsync<ShareException>(() => Invoke(ShareFixture.Manager(300)));
        Assert.Equal(1, calls);
        Assert.Null((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId)).batch_id);
        Assert.True((await db.paymentShare.AsNoTracking().SingleAsync(s => s.share_id == view.orderShareId && s.valid)).success);
    }

    [Fact]
    public async Task Snow_ticket_system_one_yuan_share_is_selected_without_approval_but_distributor_rows_are_not()
    {
        await using var db = await ShareFixture.NewDbAsync();
        db.order.Add(new Order { id = 1, code = "SKI-1", type = "雪票", shop = "南山", valid = 1 });
        db.orderShareRelation.Add(new OrderShareRelation { id = 1, type = "个人", name = "系统", valid = true });
        db.orderShare.Add(new OrderShare { id = 1, order_id = 1, relation_id = 1, amount = 1, valid = true, create_date = DateTime.Now });
        db.orderShare.Add(new OrderShare { id = 2, order_id = 1, relation_id = 1, amount = 20, valid = true, share_method = "manual", create_date = DateTime.Now });
        db.paymentShare.Add(new PaymentShare { id = 1, share_id = 1, payment_id = 9, amount = 1, valid = true, out_trade_no = "SKI-1" });
        db.paymentShare.Add(new PaymentShare { id = 2, share_id = 2, payment_id = 9, amount = 20, valid = true, out_trade_no = "SKI-2" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var method = typeof(OrderShareController).GetMethod("GetSystemTicketSharesAsync");
        Assert.NotNull(method);
        var selected = await (Task<List<PaymentShare>>)method!.Invoke(helper, new object[] { 1 })!;
        Assert.Equal(new[] { 1 }, selected.Select(s => s.id).ToArray());
    }

    [Fact]
    public async Task Repeated_payment_callback_does_not_duplicate_modern_channel_records()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "manual");
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId);
        await new OrderShareController(db, ShareFixture.Config, ShareFixture.Http).CreatePaymentShare(share);
        Assert.Single(await db.paymentShare.AsNoTracking().Where(p => p.share_id == share.id && p.valid).ToListAsync());
    }

    [Fact]
    public async Task Bank_completed_share_survives_voiding_unsubmitted_applications()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("现金", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var orderService = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);
        var view = await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "bank");
        await new ShareListService(db).CompleteManualAsync(ShareFixture.Manager(300), new[] { view.orderShareId }, "MS-BANK-VOID");
        var bank = await db.orderShare.AsNoTracking().Include(s => s.paymentShares).SingleAsync(s => s.id == view.orderShareId);
        Assert.Equal(40, bank.successSharedAmount, 2);
        await orderService.VoidUnsubmittedIntroducerSharesAsync(1);
        Assert.True((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId)).valid);
    }

    [Fact]
    public async Task Partially_allocated_channel_share_is_not_shown_as_completed()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        db.orderShare.Add(new OrderShare { id = 90, order_id = 1, relation_id = intro.relation_id, amount = 40, share_method = "manual", valid = true, create_date = DateTime.Now });
        db.paymentShare.Add(new PaymentShare { id = 900, payment_id = 4, share_id = 90, amount = 10, valid = true, success = true,
            submit_time = DateTime.Now, response_time = DateTime.Now, out_trade_no = "PARTIAL-900" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var orderView = Assert.Single(await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http).ListAsync(1));
        var list = await new ShareListService(db).ListAsync(new ShareListQuery(null, null, null, null, null, null));
        Assert.NotEqual("已分账", orderView.state);
        Assert.Equal("未分", Assert.Single(list.items).status);
    }

    [Fact]
    public async Task Alipay_retail_order_can_choose_payment_channel()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        order.type = "零售";
        order.shop = "南山";
        db.order.Update(order);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True(await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http).ChannelShareAvailableAsync(order));
    }

    [Fact]
    public async Task Open_rental_wechat_payment_marked_shareable_can_choose_payment_channel()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        order.closed = 0;
        order.close_date = null;
        db.order.Update(order);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True(await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http).ChannelShareAvailableAsync(order));
    }

    [Fact]
    public async Task Nightly_job_does_not_submit_if_payment_loses_share_capability_after_approval()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, DateTime.Today, true, "auto");
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId);
        share.approved_at = DateTime.Now;
        share.approved_by_staff_id = 1;
        db.orderShare.Update(share);
        var payment = await db.orderPayment.AsNoTracking().SingleAsync(p => p.id == 4);
        payment.need_share = 0;
        db.orderPayment.Update(payment);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var due = await new OrderShareController(db, ShareFixture.Config, ShareFixture.Http).GetDueWanlongShares(DateTime.Today.AddDays(-1));
        Assert.DoesNotContain(due, p => p.share_id == share.id);
        var item = Assert.Single((await new ShareListService(db).ListAsync(new ShareListQuery(null, null, null, null, null, null))).items);
        Assert.Equal("无法提交", item.channelState);
        Assert.Contains("支付", item.channelMessage);
    }

    [Fact]
    public async Task Direct_automatic_dispatch_cannot_bypass_required_approval()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, DateTime.Today, true, "auto");
        var row = await db.paymentShare.AsNoTracking().SingleAsync(p => p.share_id == view.orderShareId && p.valid);
        await new OrderShareController(db, ShareFixture.Config, ShareFixture.Http).SharePayment(row);
        Assert.Null((await db.paymentShare.AsNoTracking().SingleAsync(p => p.id == row.id)).submit_time);
    }

    [Fact]
    public async Task Channel_failure_is_not_success_and_response_is_visible_without_bank_batch()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("支付宝", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db, aliType: "loginName", aliNum: "person@example.com", aliBound: true);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "manual");
        var service = new ShareChannelSubmitService(db, async row => {
            row.success = false;
            row.response_time = DateTime.Now;
            row.response_content = "渠道拒绝：收款方状态异常";
            db.paymentShare.Update(row);
            await db.SaveChangesAsync();
            return row;
        });
        await service.SubmitManualAsync(ShareFixture.Manager(300), view.orderShareId);
        var list = await new ShareListService(db).ListAsync(new ShareListQuery(null, null, null, null, null, null));
        var item = Assert.Single(list.items);
        Assert.Equal(("未分", "渠道失败", "渠道拒绝：收款方状态异常", false, (string?)null),
            (item.status, item.channelState, item.channelMessage, item.canSubmitManual, item.batch));
    }

    [Fact]
    public async Task Ticket_system_one_yuan_share_is_not_overwritten_by_a_distributor_application()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("现金", 5000, 0, 400);
        var order = await db.order.AsNoTracking().SingleAsync(o => o.id == 1);
        order.type = "雪票";
        db.order.Update(order);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        db.orderShare.Add(new OrderShare { id = 90, order_id = 1, relation_id = intro.relation_id, amount = 1, valid = true, create_date = DateTime.Now });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "bank");
        var shares = await db.orderShare.AsNoTracking().Where(s => s.order_id == 1 && s.valid).OrderBy(s => s.id).ToListAsync();
        Assert.Equal(2, shares.Count);
        Assert.Equal((1d, (string?)null), (shares.Single(s => s.id == 90).amount, shares.Single(s => s.id == 90).share_method));
        Assert.NotEqual(90, view.orderShareId);
        Assert.Single(await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http).ListAsync(1));
        var systemItem = (await new ShareListService(db).ListAsync(new ShareListQuery(null, null, null, null, null, null)))
            .items.Single(i => i.orderShareId == 90);
        Assert.Equal("自动", systemItem.mode);
        Assert.Null(systemItem.approvalState);
        Assert.False(systemItem.canApprove);
        await Assert.ThrowsAsync<ShareException>(() => new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .RemoveIntroducerShareAsync(ShareFixture.Manager(), 90));
    }

    [Fact]
    public async Task Removing_an_application_rolls_back_if_recalculating_other_shares_fails()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 1000, 500, 200);
        db.orderShareRelation.Add(new OrderShareRelation { id = 4, type = "公司", name = "系统收款方", valid = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var order = await new OrderController(db, ShareFixture.Config, ShareFixture.Http).GetOrder(1);
        var system = Assert.Single(await helper.CreateOrderShares(order));
        await helper.CreatePaymentShare(system);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var service = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);
        var view = await service.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 60, null);
        Assert.True((await db.paymentShare.AsNoTracking().SingleAsync(p => p.share_id == system.id && p.valid)).can_not_share);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_recalc BEFORE UPDATE ON payment_share WHEN OLD.can_not_share = 1 AND NEW.can_not_share = 0 BEGIN SELECT RAISE(ABORT, 'recalc failed'); END;");

        await Assert.ThrowsAnyAsync<Exception>(() => service.RemoveIntroducerShareAsync(ShareFixture.Manager(), view.orderShareId));
        db.ChangeTracker.Clear();
        Assert.True((await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId)).valid);
        Assert.All(await db.paymentShare.AsNoTracking().Where(p => p.share_id == view.orderShareId).ToListAsync(), p => Assert.True(p.valid));
    }

    [Fact]
    public async Task Find_order_by_complete_code_requires_manager_and_returns_the_matching_order()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("现金", 5000, 0, 400);
        await ShareFixture.AddStaffSessionAsync(db, 302, 200, "manager-s");
        await ShareFixture.AddStaffSessionAsync(db, 303, 100, "clerk-s");
        var controller = new OrderIntroducerShareController(db, ShareFixture.Config, ShareFixture.Http);
        Assert.Equal(3, (await controller.FindOrder("clerk-s", "WT_ZL_261031_00001")).code);
        Assert.Equal(1, (await controller.FindOrder("manager-s", "not-found")).code);
        var result = await controller.FindOrder("manager-s", " WT_ZL_261031_00001 ");
        Assert.Equal(0, result.code);
        Assert.Equal(1, JsonSerializer.SerializeToElement(result.data).GetProperty("orderId").GetInt32());
    }

    [Fact]
    public async Task Refund_after_approval_that_exceeds_wechat_balance_blocks_and_explains_automatic_submission()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, DateTime.Today, true, "auto");
        await new ShareListService(db).ApproveAutoAsync(ShareFixture.Manager(300), view.orderShareId);
        db.Set<OrderPaymentRefund>().Add(new OrderPaymentRefund { order_id = 1, payment_id = 4, amount = 4990, state = 1, refund_id = "TEST-REFUND" });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var due = await new OrderShareController(db, ShareFixture.Config, ShareFixture.Http).GetDueWanlongShares(DateTime.Today.AddDays(-1));
        Assert.DoesNotContain(due, p => p.share_id == view.orderShareId);
        var row = await db.paymentShare.AsNoTracking().SingleAsync(p => p.share_id == view.orderShareId && p.valid);
        var attempted = await new OrderShareController(db, ShareFixture.Config, ShareFixture.Http).SharePayment(row);
        Assert.Null(attempted.submit_time);
        var item = Assert.Single((await new ShareListService(db).ListAsync(new ShareListQuery(null, null, null, null, null, null))).items);
        Assert.Equal("无法提交", item.channelState);
        Assert.Contains("金额", item.channelMessage);
    }

    [Fact]
    public async Task Pending_channel_share_prevents_wechat_unfreeze_but_bank_and_definitive_failure_do_not()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var view = await new OrderShareService(db, ShareFixture.Config, ShareFixture.Http)
            .SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, DateTime.Today.AddDays(10), true, "auto");
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var method = typeof(OrderShareController).GetMethod("HasPendingDistributorChannelSharesAsync");
        Assert.NotNull(method);
        Task<bool> Pending() => (Task<bool>)method!.Invoke(helper, new object[] { 4 })!;

        Assert.True(await Pending()); // 包括尚未审核和计划日期未到的自动申请
        var share = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == view.orderShareId);
        share.share_method = "bank";
        db.orderShare.Update(share);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False(await Pending());
        share.share_method = "manual";
        db.orderShare.Update(share);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.True(await Pending());
        var row = await db.paymentShare.AsNoTracking().SingleAsync(p => p.share_id == share.id && p.valid);
        row.submit_time = DateTime.Now;
        row.response_time = DateTime.Now;
        row.success = false;
        db.paymentShare.Update(row);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False(await Pending());
    }

    [Fact]
    public async Task Bank_completion_allows_later_wechat_unfreeze_only_when_no_channel_share_is_pending()
    {
        await using var db = await ShareFixture.ClosedWanlongOrderAsync("微信支付", 5000, 0, 400);
        var intro = await ShareFixture.AddIntroducerAsync(db);
        var orderService = new OrderShareService(db, ShareFixture.Config, ShareFixture.Http);
        var bank = await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, intro.id, 40, null, null, false, "bank");
        await new ShareListService(db).CompleteManualAsync(ShareFixture.Manager(300), new[] { bank.orderShareId }, "MS-FREEZE");
        var batch = await db.shareBatch.AsNoTracking().SingleAsync(b => b.batch_no == "MS-FREEZE");
        batch.create_date = DateTime.Today.AddDays(-5);
        db.shareBatch.Update(batch);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var helper = new OrderShareController(db, ShareFixture.Config, ShareFixture.Http);
        var method = typeof(OrderShareController).GetMethod("CanCloseWechatShareAsync");
        Assert.NotNull(method);
        Task<bool> CanClose() => (Task<bool>)method!.Invoke(helper, new object[] { 4, DateTime.Today })!;
        Assert.True(await CanClose());
        var bankShare = await db.orderShare.AsNoTracking().SingleAsync(s => s.id == bank.orderShareId);
        bankShare.share_method = null; // 旧批次可能是仅为历史清账，不能因此触发微信解冻
        db.orderShare.Update(bankShare);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False(await CanClose());
        bankShare.share_method = "bank";
        db.orderShare.Update(bankShare);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var second = await ShareFixture.AddIntroducerAsync(db, 101);
        await orderService.SetIntroducerShareAsync(ShareFixture.Manager(), 1, second.id, 10, null, DateTime.Today.AddDays(7), true, "auto");
        Assert.False(await CanClose());
    }
}
