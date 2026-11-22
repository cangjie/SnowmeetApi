using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Controllers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Rent;

namespace SnowmeetApi.Tests;

public class RentalBenefitQuoteServiceTests
{
    private static async Task<(int orderId, int rentalId)> Seed(CoachFixture f, bool automatic)
    {
        var db = f.NewContext();
        var order = new Order { type = "租赁", shop = "万龙服务中心", member_id = CoachFixture.StudentA, valid = 1 };
        db.order.Add(order); await db.SaveChangesAsync();
        var rental = new Rental { order_id = order.id, name = "雪板", valid = 1, settled = 1 };
        var category = new RentCategory { code = "0201", name = "单板", valid = 1 };
        db.rental.Add(rental); db.rentCategory.Add(category); await db.SaveChangesAsync();
        db.rentItem.Add(new RentItem { rental_id = rental.id, category_id = category.id, valid = 1 });
        db.rentalDetail.Add(new RentalDetail { rental_id = rental.id, rental_date = DateTime.Today, charge_type = "租金", amount = 200, valid = 1 });
        db.coachCard.Add(new CoachCard { coach_id = f.CoachId, member_id = CoachFixture.StudentA, token = Guid.NewGuid().ToString("N"),
            status = CoachCard.StatusClaimed, start_date = DateTime.Today.AddDays(-5), end_date = DateTime.Today.AddDays(5), rental_rate_snapshot = .5 });
        if (automatic) db.rentalBenefitPreference.Add(new RentalBenefitPreference { order_id = order.id, mode = "auto", staff_id = 9 });
        await db.SaveChangesAsync();
        return (order.id, rental.id);
    }

    [Fact]
    public async Task ReturnedButNotRefundedOrderUsesTheCurrentMembershipRate()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _) = await Seed(f, true);
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.True(q.canChange);
        Assert.Equal(100m, q.quote.Payable);
        Assert.NotNull(Assert.Single(q.units).coachCardId);
    }

    [Fact]
    public async Task NewOrderWithAutomaticMatchingPersistsItsPreference()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, false);
        var db = f.NewContext();
        await db.order.Where(o => o.id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.valid, 0));
        await db.rental.Where(r => r.id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(r => r.valid, 0).SetProperty(r => r.noGuaranty, true));
        await db.rentItem.Where(i => i.rental_id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(i => i.noCode, true));
        var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.PlaceRentOrder(orderId, "session-1", autoBenefit: true);
        var data = Assert.IsType<ApiResult<Order>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(0, data.code);
        Assert.Equal("auto", (await f.NewContext().rentalBenefitPreference.SingleAsync(p => p.order_id == orderId)).mode);
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(100m, q.quote.Payable);
    }

    [Fact]
    public async Task DraftPersistsAndRestoresAnExplicitNoBenefitChoice()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var input = new Order { shop = "万龙服务中心", type = "租赁", member_id = CoachFixture.StudentA,
            rentals = new() { new Rental { name = "雪板", benefit_mode = "none", rentItems = new() } } };
        RentController Controller() => new(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var saved = await Controller().SaveRentRecept(input, "session-1", benefitOptions: true);
        var payload = Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(saved.Result).Value);
        Assert.Equal(0, payload.code);
        var order = payload.data!;
        int rentalId = Assert.Single(order.rentals).id;
        Assert.Equal("none", (await f.NewContext().rentalBenefitRentalPreference.SingleAsync(p => p.rental_id == rentalId)).mode);
        var restored = await Controller().GetReceptingOrder(order.id, "session-1", benefitOptions: true);
        var restoredOrder = Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(restored.Result).Value).data!;
        Assert.Equal("none", Assert.Single(restoredOrder.rentals).benefit_mode);
    }

    [Fact]
    public async Task ManuallySelectedMembershipKeepsItsIdentityAndUsesTheUpdatedTemplateRate()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        var card = await db.coachCard.SingleAsync(c => c.member_id == CoachFixture.StudentA);
        var template = new CoachCardTemplate { coach_id = f.CoachId, name = "会籍卡", token = "quote-update", valid = 1,
            care_prices = "[]", rental_rate = .5 };
        db.coachCardTemplate.Add(template); await db.SaveChangesAsync();
        await db.coachCard.Where(c => c.id == card.id).ExecuteUpdateAsync(s => s.SetProperty(c => c.template_id, template.id));
        await db.rental.Where(r => r.id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(r => r.coach_card_id, card.id));
        var detail = await db.rentalDetail.SingleAsync(d => d.rental_id == rentalId);
        db.discount.Add(new Discount { order_id = orderId, biz_type = "租赁", biz_id = rentalId, sub_biz_type = "日租金",
            sub_biz_id = detail.id, amount = 20, valid = 1 });
        db.coachCardUse.Add(new CoachCardUse { order_id = orderId, card_id = card.id, coach_id = f.CoachId, member_id = CoachFixture.StudentA,
            biz_type = "租赁", biz_id = rentalId, sub_biz_id = detail.id, mode = "rate", mode_value = .9, base_amount = 200, discount_amount = 20 });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(100m, q.quote.Payable);
        Assert.Equal(card.id, Assert.Single(q.units).coachCardId);
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        var after = f.NewContext();
        Assert.Equal(100, (await after.discount.SingleAsync(d => d.biz_id == rentalId && d.valid == 1)).amount);
        var use = await after.coachCardUse.SingleAsync(u => u.biz_id == rentalId);
        Assert.Equal((card.id, 100d, .5), (use.card_id, use.discount_amount, use.mode_value));
    }

    [Fact]
    public async Task LegacyOrderWithSelectedCardRepricesItButDoesNotAddCardsToUnselectedRentals()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, false);
        var db = f.NewContext();
        var card = await db.coachCard.SingleAsync(c => c.member_id == CoachFixture.StudentA);
        await db.rental.Where(r => r.id == rentalId).ExecuteUpdateAsync(s => s.SetProperty(r => r.coach_card_id, card.id));
        var another = new Rental { order_id = orderId, valid = 1, settled = 1, name = "雪鞋" };
        db.rental.Add(another); await db.SaveChangesAsync();
        db.rentalDetail.Add(new RentalDetail { rental_id = another.id, rental_date = DateTime.Today, charge_type = "租金", amount = 100, valid = 1 });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal("auto", q.mode);
        Assert.Equal(200m, q.quote.Payable); // manually selected 5折: 100; unselected legacy rental: 100
        Assert.True(q.units.Single(u => u.rentalId == another.id).manual);
    }

    [Fact]
    public async Task LegacyOrderAndExplicitNoneAreNotAutomaticallyChanged()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, false);
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal("none", q.mode);
        Assert.Equal(200m, q.quote.Payable);
        var db = f.NewContext();
        db.rentalBenefitPreference.Add(new RentalBenefitPreference { order_id = orderId, mode = "auto", staff_id = 9 });
        db.rentalBenefitRentalPreference.Add(new RentalBenefitRentalPreference { rental_id = rentalId, mode = "none", staff_id = 9 });
        await db.SaveChangesAsync();
        var manual = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(200m, manual.quote.Payable);
        Assert.True(Assert.Single(manual.units).manual);
    }

    [Fact]
    public async Task AManuallyEditedDailyRentIsNotOverridden()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        var detail = await db.rentalDetail.SingleAsync(d => d.rental_id == rentalId);
        db.rentalBenefitDetailPreference.Add(new RentalBenefitDetailPreference { rental_detail_id = detail.id, staff_id = 9 });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(200m, q.quote.Payable);
        Assert.True(Assert.Single(q.units).manual);
    }

    [Fact]
    public async Task EditingDailyRentAfterBenefitSettlementIsAllowedAndAudited()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        var detail = await db.rentalDetail.SingleAsync(d => d.rental_id == rentalId);
        db.rentalBenefitSettlement.Add(new RentalBenefitSettlement
        {
            order_id = orderId,
            rent_payable = 200,
            staff_id = 9,
            create_date = DateTime.Now
        });
        await db.SaveChangesAsync();
        var controller = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.UpdateRentalDayChargesByStaff(rentalId, detail.id, 150, 0, 0, "测试手动改租金", "session-1");
        Assert.Equal(0, Assert.IsType<ApiResult<Rental?>>(Assert.IsType<OkObjectResult>(result.Result).Value).code);
        var after = f.NewContext();
        Assert.True(await after.rentalBenefitDetailPreference.AnyAsync(p => p.rental_detail_id == detail.id && p.staff_id == 9));
        var audit = await after.coreDataModLog.SingleAsync(l => l.table_name == "rental_detail"
            && l.key_value == detail.id && l.field_name == "amount");
        Assert.Equal(9, audit.staff_id);
        Assert.Equal("200", audit.prev_value);
        Assert.Equal("150", audit.current_value);
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(150m, q.quote.Payable);
    }

    [Fact]
    public async Task WaivingDailyRentAfterBenefitSettlementIsAllowedAndAudited()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        var detail = await db.rentalDetail.SingleAsync(d => d.rental_id == rentalId);
        db.rentalBenefitSettlement.Add(new RentalBenefitSettlement
        {
            order_id = orderId,
            rent_payable = 200,
            staff_id = 9,
            create_date = DateTime.Now
        });
        await db.SaveChangesAsync();
        var controller = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.UpdateRentalDayChargesByStaff(rentalId, detail.id, 60, 0, 0, "测试免除租金",
            "session-1", waived: true);
        Assert.Equal(0, Assert.IsType<ApiResult<Rental?>>(Assert.IsType<OkObjectResult>(result.Result).Value).code);
        var after = f.NewContext();
        Assert.Equal(0, (await after.rentalDetail.SingleAsync(d => d.id == detail.id)).valid);
        var audit = await after.coreDataModLog.SingleAsync(l => l.table_name == "rental_detail"
            && l.key_value == detail.id && l.field_name == "valid");
        Assert.Equal(9, audit.staff_id);
        Assert.Equal("1", audit.prev_value);
        Assert.Equal("0", audit.current_value);
    }

    [Fact]
    public async Task EditingDailyRentIsRejectedForClosedOrder()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, false);
        var db = f.NewContext();
        await db.order.Where(o => o.id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.closed, 1));
        var detail = await db.rentalDetail.SingleAsync(d => d.rental_id == rentalId);
        var controller = new RentController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.UpdateRentalDayChargesByStaff(rentalId, detail.id, 150, 0, 0, "测试已关闭订单", "session-1");
        var data = Assert.IsType<ApiResult<Rental?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, data.code);
        Assert.Equal("订单已关闭，无法修改租金明细", data.message);
        Assert.Equal(200, (await f.NewContext().rentalDetail.SingleAsync(d => d.id == detail.id)).amount);
        Assert.Empty(await f.NewContext().coreDataModLog.ToListAsync());
    }

    [Fact]
    public async Task LegacyUnallocatedDiscountIsPreservedAndBlocksAutomaticStacking()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        db.discount.Add(new Discount { order_id = orderId, biz_type = "租赁", biz_id = rentalId,
            sub_biz_type = null, sub_biz_id = null, amount = 40, valid = 1 });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.True(Assert.Single(q.units).manual);
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        var rental = await f.NewContext().rental.Where(r => r.id == rentalId).Include(r => r.details).Include(r => r.discounts).SingleAsync();
        Assert.Equal(160, rental.totalSummary);
        Assert.Single(rental.discounts.Where(d => d.valid == 1));
    }

    [Fact]
    public async Task MembershipDiscountTouchesRentButNotRepairCharges()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        var repair = new RentalDetail { rental_id = rentalId, rental_date = DateTime.Today, charge_type = "赔偿金", amount = 80, valid = 1 };
        db.rentalDetail.Add(repair); await db.SaveChangesAsync();
        db.discount.Add(new Discount { order_id = orderId, biz_type = "租赁", biz_id = rentalId,
            sub_biz_type = "赔偿金", sub_biz_id = repair.id, amount = 30, valid = 1 });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(100m, q.quote.Payable);
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        var rental = await f.NewContext().rental.Where(r => r.id == rentalId).Include(r => r.details).Include(r => r.discounts).SingleAsync();
        Assert.Equal(150, rental.totalSummary);
        Assert.Equal(30, rental.totalRepairationDiscountAmount);
    }

    [Fact]
    public async Task PendingRefundFreezesAutomaticChanges()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _) = await Seed(f, true);
        var db = f.NewContext();
        db.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = orderId, payment_id = 1, amount = 1, reason = "退款中" });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.False(q.canChange);
        Assert.Equal(200m, q.quote.Payable);
    }

    [Fact]
    public async Task CommittingMembershipDiscountIsIdempotent()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        var committed = await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        Assert.Equal(100, committed.rent_payable);
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        var db = f.NewContext();
        Assert.Equal(100, (await db.discount.SingleAsync(d => d.biz_id == rentalId && d.valid == 1)).amount);
        Assert.Single(await db.coachCardUse.Where(u => u.order_id == orderId && u.status == CoachCardUse.StatusEffective).ToListAsync());
        Assert.Single(await db.rentalBenefitSettlement.Where(s => s.order_id == orderId).ToListAsync());
    }

    [Fact]
    public async Task ChangedCardPriceRejectsOldQuoteWithoutMutatingOrder()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        await f.NewContext().coachCard.Where(c => c.member_id == CoachFixture.StudentA)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.rental_rate_snapshot, .7));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now));
        Assert.Empty(await f.NewContext().discount.Where(d => d.biz_id == rentalId && d.valid == 1).ToListAsync());
    }

    [Fact]
    public async Task PunchIsNotConsumedUntilMemberIdentityIsVerified()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, _) = await Seed(f, true);
        var db = f.NewContext();
        db.punchCard.Add(new PunchCard { member_id = CoachFixture.StudentA, biz_type = "租赁", card_name = "租赁次卡", total = 1 });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.True(Assert.Single(q.quote.Lines).UsesPunch);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now));
        Assert.Equal(0, (await f.NewContext().punchCard.SingleAsync()).punches ?? 0);
        await f.NewContext().order.Where(o => o.id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.wechat_unverified, true));
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        Assert.Equal(1, (await f.NewContext().punchCard.SingleAsync()).punches);
        Assert.Single(await f.NewContext().punchCardUsed.Where(u => u.order_id == orderId).ToListAsync());
    }

    [Fact]
    public async Task WholeOrderCouponIsConsumedOnceAndTheRentTotalCountsItOnce()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        db.ticketTemplate.Add(new TicketTemplate { id = 110, name = "租赁券", type = "租赁券", memo = "", miniapp_recept_path = "", biz_type = "租赁", valid = 1 });
        db.productTicketTemplate.Add(new ProductTicketTemplate { ticket_template_id = 110, product_id = 0, discount_rate = .25, valid = true });
        db.ticket.Add(new Ticket { code = "rent-test", member_id = CoachFixture.StudentA, template_id = 110, name = "租赁券",
            open_id = "x", valid = 1, is_active = 1, used = 0, create_date = DateTime.Now });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal("rent-test", q.quote.CouponCode);
        Assert.Equal(50m, q.quote.Payable);
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        var after = f.NewContext();
        Assert.Equal(1, (await after.ticket.SingleAsync(t => t.code == "rent-test")).used);
        var rent = await after.rental.Where(r => r.id == rentalId).Include(r => r.details).Include(r => r.discounts).SingleAsync();
        Assert.Equal(50, rent.totalSummary);
        Assert.Equal(150, Assert.Single(rent.discounts.Where(d => d.valid == 1)).amount);
        Assert.Empty(await after.coachCardUse.Where(u => u.order_id == orderId && u.status == CoachCardUse.StatusEffective).ToListAsync());
    }

    [Fact]
    public async Task RentalCouponTemplateAcceptsOnlyOneWholeOrderRule()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        f.Db.ticketTemplate.Add(new TicketTemplate { id = 110, name = "租赁券", type = "租赁券", memo = "", miniapp_recept_path = "", biz_type = "租赁", valid = 1 });
        await f.Db.SaveChangesAsync();
        async Task<int> Save(int productId, bool rentalWholeOrder = true)
        {
            var ctl = new TicketTemplateAdminController(f.NewContext(), f.Config);
            var r = await ctl.SaveProductRuleByStaff(new TicketTemplateAdminController.SaveRuleRequest
                { templateId = 110, productId = productId, mode = "rate", value = .5, rentalWholeOrder = rentalWholeOrder }, "session-1");
            return Assert.IsType<ApiResult<object>>(Assert.IsType<OkObjectResult>(r.Result).Value).code;
        }
        Assert.Equal(1, await Save(200));
        Assert.Equal(0, await Save(0));
        Assert.Equal(1, await Save(0));
        Assert.Equal(0, await Save(200, rentalWholeOrder: false)); // old client retains its product-rule editor
    }

    [Fact]
    public async Task SeasonCardCoversOnlyOneEligibleProjectPerDay()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        db.rentalDetail.Add(new RentalDetail { rental_id = rentalId, rental_date = DateTime.Today.AddDays(1), charge_type = "租金", amount = 120, valid = 1 });
        db.punchCard.Add(new PunchCard { member_id = CoachFixture.StudentA, biz_type = "租赁", card_name = "租赁季卡", total = null });
        await db.order.Where(o => o.id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.wechat_unverified, true));
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        Assert.Equal(0m, q.quote.Payable);
        Assert.Equal(2, q.quote.Lines.Count(l => l.UsesSeason));
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        Assert.Equal(2, await f.NewContext().rentalSeasonCardUse.CountAsync(u => u.order_id == orderId && u.valid));
        Assert.Equal(0, await f.NewContext().rentalDetail.CountAsync(d => d.rental_id == rentalId && d.valid == 1));
    }

    [Fact]
    public async Task RefundCannotExceedTheActualRentAfterBenefitCommit()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (orderId, rentalId) = await Seed(f, true);
        var db = f.NewContext();
        var payment = new OrderPayment { order_id = orderId, amount = 500, status = "支付成功", pay_method = "现金", valid = 1, out_trade_no = "test" };
        db.orderPayment.Add(payment);
        var guaranty = new Guaranty { order_id = orderId, biz_type = "租赁", biz_id = rentalId, amount = 500, valid = 1 };
        db.guaranty.Add(guaranty);
        await db.SaveChangesAsync();
        db.guarantyPayment.Add(new GuarantyPayment { guaranty_id = guaranty.id, payment_id = payment.id });
        await db.SaveChangesAsync();
        var q = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(orderId, false, DateTime.Now);
        await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, q.fingerprint, 9, DateTime.Now);
        var controller = new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
        var result = await controller.Refund(orderId, new() { new OrderPaymentRefund { payment_id = payment.id, amount = 401, reason = "租赁退押金" } }, "session-1");
        var data = Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, data.code);
        Assert.Empty(await f.NewContext().orderPaymentRefund.Where(r => r.order_id == orderId).ToListAsync());
        var allowed = await new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
            .Refund(orderId, new() { new OrderPaymentRefund { payment_id = payment.id, amount = 400, reason = "租赁退押金" } }, "session-1");
        Assert.Equal(0, Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(allowed.Result).Value).code);
        var repeated = await new OrderController(f.NewContext(), f.Config, new HttpContextAccessor { HttpContext = new DefaultHttpContext() })
            .Refund(orderId, new() { new OrderPaymentRefund { payment_id = payment.id, amount = 400, reason = "租赁退押金" } }, "session-1");
        Assert.Equal(1, Assert.IsType<ApiResult<Order?>>(Assert.IsType<OkObjectResult>(repeated.Result).Value).code);
        Assert.Equal(400, (await f.NewContext().orderPaymentRefund.SingleAsync(r => r.order_id == orderId)).amount);
    }

    [Fact]
    public async Task TwoOrdersCannotConsumeTheSameLastPunch()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (first, _) = await Seed(f, true);
        var (second, _) = await Seed(f, true);
        var db = f.NewContext();
        db.punchCard.Add(new PunchCard { member_id = CoachFixture.StudentA, biz_type = "租赁", card_name = "租赁次卡", total = 1 });
        await db.order.Where(o => o.id == first || o.id == second).ExecuteUpdateAsync(s => s.SetProperty(o => o.wechat_unverified, true));
        await db.SaveChangesAsync();
        var q1 = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(first, false, DateTime.Now);
        var q2 = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(second, false, DateTime.Now);
        async Task<bool> TryCommit(int orderId, string fingerprint)
        {
            try { await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(orderId, fingerprint, 9, DateTime.Now); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(TryCommit(first, q1.fingerprint), TryCommit(second, q2.fingerprint));
        Assert.Equal(1, results.Count(x => x));
        Assert.Equal(1, (await f.NewContext().punchCard.SingleAsync()).punches);
        Assert.Single(await f.NewContext().punchCardUsed.ToListAsync());
    }

    [Fact]
    public async Task TwoOrdersCannotUseOneSeasonCardOnTheSameDay()
    {
        await using var f = await CoachFixture.Create(withPricing: false);
        var (first, _) = await Seed(f, true);
        var (second, _) = await Seed(f, true);
        var db = f.NewContext();
        db.punchCard.Add(new PunchCard { member_id = CoachFixture.StudentA, biz_type = "租赁", card_name = "租赁季卡", total = null });
        await db.order.Where(o => o.id == first || o.id == second).ExecuteUpdateAsync(s => s.SetProperty(o => o.wechat_unverified, true));
        await db.SaveChangesAsync();
        var q1 = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(first, false, DateTime.Now);
        var q2 = await new RentalBenefitQuoteService(f.NewContext()).QuoteAsync(second, false, DateTime.Now);
        async Task<bool> TryCommit(int id, string fingerprint)
        {
            try { await new RentalBenefitSettlementService(f.NewContext()).CommitAsync(id, fingerprint, 9, DateTime.Now); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(TryCommit(first, q1.fingerprint), TryCommit(second, q2.fingerprint));
        Assert.Equal(1, results.Count(x => x));
        Assert.Single(await f.NewContext().rentalSeasonCardUse.Where(u => u.valid).ToListAsync());
    }
}
