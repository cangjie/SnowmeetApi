using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services;

/// <summary>含卡订单整单退款的持久化准备与完成状态。</summary>
public sealed class CardOrderRefundService(ApplicationDBContext db)
{
    public sealed record Preparation(int? BatchId, string? Error);

    public static Task<bool> IsApprovedChannelRequestAsync(ApplicationDBContext db, int refundId) =>
        db.orderPaymentRefund.AsNoTracking().AnyAsync(r => r.id == refundId && r.card_refund_batch_id != null
            && r.card_refund_state == "sending" && db.cardOrderRefundBatch.Any(b => b.id == r.card_refund_batch_id
                && b.order_id == r.order_id && b.status == "processing"));

    public async Task<Preparation> PrepareAsync(int orderId, int? staffId, string reason, List<OrderPaymentRefund> requested)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await CareCardSaleService.LockAsync(db, "care-card-order:" + orderId);

        var existing = await db.cardOrderRefundBatch.AsNoTracking().Where(b => b.order_id == orderId && b.status != "failed")
            .OrderByDescending(b => b.id).FirstOrDefaultAsync();
        if (existing != null)
        {
            if (existing.status != "processing") return new Preparation(null, "该订单已退款");
            var allExistingLegs = await db.orderPaymentRefund.AsNoTracking()
                .Where(r => r.card_refund_batch_id == existing.id).ToListAsync();
            var existingLegs = allExistingLegs.GroupBy(r => r.payment_id)
                .Select(g => g.OrderByDescending(r => r.id).First()).ToList();
            if (requested == null || requested.Count != existingLegs.Count
                || requested.Select(r => r.payment_id).Distinct().Count() != requested.Count
                || existingLegs.Any(p => !requested.Any(r => r.payment_id == p.payment_id
                    && double.IsFinite(r.amount) && Math.Round((decimal)r.amount, 2) == Math.Round((decimal)p.amount, 2))))
                return new Preparation(null, "退款申请已变化，请刷新后查询退款状态");
            return new Preparation(existing.id, null);
        }

        var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == orderId && o.valid == 1);
        if (order == null) return new Preparation(null, "订单不存在");
        var eligibility = await CardOrderRefundEligibility.EvaluateAsync(db, orderId);
        if (!eligibility.CanRefund) return new Preparation(null, eligibility.Reason);
        foreach (int id in eligibility.CardIds) await CareCardSaleService.LockAsync(db, "punch-card:" + id);
        eligibility = await CardOrderRefundEligibility.EvaluateAsync(db, orderId);
        if (!eligibility.CanRefund) return new Preparation(null, eligibility.Reason);

        var payments = await db.orderPayment.AsNoTracking().Where(p => p.order_id == orderId && p.status == "支付成功"
                && (p.pay_method == "微信支付" || p.pay_method == "支付宝" || p.valid == 1))
            .OrderBy(p => p.id).ToListAsync();
        if (payments.Count == 0 || payments.Any(p => p.amount <= 0 || !double.IsFinite(p.amount)))
            return new Preparation(null, "订单没有可退款的有效收款记录");
        if (payments.Any(p => p.is_debt != 0))
            return new Preparation(null, "订单包含挂账收款，请联系管理员核对");
        if (CareCardSaleService.IsSingleOrder(order)
            && Math.Round(payments.Sum(p => (decimal)p.amount), 2) != Math.Round((decimal)order.total_amount, 2))
            return new Preparation(null, "订单收款与应收不一致，请联系管理员核对");
        string[] supportedMethods = { "微信支付", "支付宝", "现金", "POS机刷卡", "京东收银" };
        if (payments.Any(p => !supportedMethods.Contains((p.pay_method ?? "").Trim())))
            return new Preparation(null, "订单包含不支持的收款方式，请联系管理员核对");
        if (requested == null || requested.Count != payments.Count
            || requested.Any(r => !double.IsFinite(r.amount) || r.amount <= 0)
            || requested.Select(r => r.payment_id).Distinct().Count() != requested.Count
            || payments.Any(p => !requested.Any(r => r.payment_id == p.id
                && Math.Round((decimal)r.amount, 2) == Math.Round((decimal)p.amount, 2))))
            return new Preparation(null, "含卡订单只能全额退款，请刷新后重试");
        if (payments.Any(p => string.IsNullOrWhiteSpace(p.out_trade_no)
                && (p.pay_method == "微信支付" || p.pay_method == "支付宝")))
            return new Preparation(null, "支付记录缺少交易单号，请联系管理员核对");

        int[] paymentIds = payments.Select(p => p.id).ToArray();
        var oldRefunds = await db.orderPaymentRefund.AsNoTracking().Where(r => paymentIds.Contains(r.payment_id)).ToListAsync();
        if (oldRefunds.Any(r => r.state == 1 || !string.IsNullOrWhiteSpace(r.refund_id)
                || r.card_refund_batch_id == null || r.card_refund_state != "failed"))
            return new Preparation(null, "订单已有退款或退款处理中，请联系管理员核对");

        var batch = new CardOrderRefundBatch { order_id = orderId, status = "processing",
            amount = payments.Sum(p => Math.Round((decimal)p.amount, 2)), create_date = DateTime.Now, update_date = DateTime.Now };
        db.cardOrderRefundBatch.Add(batch);
        await db.SaveChangesAsync();
        foreach (var payment in payments)
        {
            db.orderPaymentRefund.Add(new OrderPaymentRefund { order_id = orderId, payment_id = payment.id,
                amount = payment.amount, reason = reason ?? "", staff_id = staffId, create_date = DateTime.Now,
                out_refund_no = "CARD_" + batch.id + "_" + payment.id,
                card_refund_batch_id = batch.id, card_refund_state = "prepared" });
        }
        var cards = await db.punchCard.AsTracking().Where(c => eligibility.CardIds.Contains(c.id)).ToListAsync();
        foreach (var card in cards)
        {
            db.cardOrderRefundCard.Add(new CardOrderRefundCard { batch_id = batch.id, card_id = card.id });
            card.refund_pending = true;
            card.update_date = DateTime.Now;
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("punch_card", "refund_pending", card.id,
                "整单退款冻结", null, staffId, "0", "1", "订单 " + orderId));
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new Preparation(batch.id, null);
    }

    public async Task<string> SubmitPreparedAsync(int batchId, IConfiguration config, IHttpContextAccessor http)
    {
        var legs = await db.orderPaymentRefund.AsNoTracking().Where(r => r.card_refund_batch_id == batchId).ToListAsync();
        var refundIds = legs.GroupBy(r => r.payment_id).Select(g => g.OrderByDescending(r => r.id).First().id).OrderBy(id => id);
        foreach (int previousRefundId in refundIds)
        {
            int? claimedRefundId = await ClaimRefundAsync(batchId, previousRefundId);
            if (claimedRefundId == null) continue;
            int refundId = claimedRefundId.Value;
            db.ChangeTracker.Clear();
            var refund = await db.orderPaymentRefund.AsNoTracking().FirstAsync(r => r.id == refundId);
            var payment = await db.orderPayment.AsNoTracking().FirstAsync(p => p.id == refund.payment_id);
            try
            {
                switch ((payment.pay_method ?? "").Trim())
                {
                    case "微信支付":
                        await new TenpayController(db, config, http).Refund(refundId);
                        break;
                    case "支付宝":
                        await new AliController(db, config, http).Refund(refundId);
                        break;
                    case "现金":
                    case "POS机刷卡":
                    case "京东收银":
                        await db.orderPaymentRefund.Where(r => r.id == refundId && r.card_refund_state == "sending")
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.state, 1).SetProperty(r => r.card_refund_state, "success"));
                        break;
                    default:
                        await db.orderPaymentRefund.Where(r => r.id == refundId && r.card_refund_state == "sending")
                            .ExecuteUpdateAsync(s => s.SetProperty(r => r.card_refund_state, "unknown"));
                        break;
                }
            }
            catch (Exception ex)
            {
                // 连接异常时渠道可能已受理；保留冻结，按原退款单号查询，不能当作失败重发。
                Console.WriteLine("[含卡整单退款] 请求结果未知 refund=" + refundId + " " + ex.GetType().Name);
                await db.orderPaymentRefund.Where(r => r.id == refundId && r.card_refund_state == "sending")
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.card_refund_state, "unknown"));
            }
            db.ChangeTracker.Clear();
        }
        await ReconcileAsync(batchId);
        return await db.cardOrderRefundBatch.AsNoTracking().Where(b => b.id == batchId)
            .Select(b => b.status).FirstAsync();
    }

    private async Task<int?> ClaimRefundAsync(int batchId, int refundId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var batch = await db.cardOrderRefundBatch.AsNoTracking().FirstOrDefaultAsync(b => b.id == batchId);
        if (batch == null) return null;
        await CareCardSaleService.LockAsync(db, "care-card-order:" + batch.order_id);
        if (!await db.cardOrderRefundBatch.AsNoTracking().AnyAsync(b => b.id == batchId && b.status == "processing")) return null;
        var original = await db.orderPaymentRefund.AsNoTracking().FirstOrDefaultAsync(r => r.id == refundId && r.card_refund_batch_id == batchId);
        if (original == null) return null;
        int latestId = await db.orderPaymentRefund.AsNoTracking().Where(r => r.card_refund_batch_id == batchId
            && r.payment_id == original.payment_id).MaxAsync(r => r.id);
        if (latestId != refundId) return null;
        if (original.card_refund_state == "failed")
        {
            var retry = new OrderPaymentRefund { order_id = original.order_id, payment_id = original.payment_id,
                amount = original.amount, reason = original.reason, staff_id = original.staff_id,
                oper_member_id = original.oper_member_id, create_date = DateTime.Now,
                out_refund_no = "CARD_" + batchId + "_" + original.payment_id + "_R" + original.id,
                card_refund_batch_id = batchId, card_refund_state = "prepared" };
            db.orderPaymentRefund.Add(retry);
            await db.SaveChangesAsync();
            refundId = retry.id;
        }
        int claimed = await db.orderPaymentRefund.Where(r => r.id == refundId && r.card_refund_batch_id == batchId
                && (r.card_refund_state == "prepared" || r.card_refund_state == "retryable"))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.card_refund_state, "sending"));
        await tx.CommitAsync();
        return claimed == 1 ? refundId : null;
    }

    /// <summary>退款渠道确认后幂等更新整单和卡；处理中和未知状态均保持冻结。</summary>
    public async Task ReconcileAsync(int batchId)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var first = await db.cardOrderRefundBatch.AsNoTracking().FirstOrDefaultAsync(b => b.id == batchId);
        if (first == null) return;
        await CareCardSaleService.LockAsync(db, "care-card-order:" + first.order_id);
        var batch = await db.cardOrderRefundBatch.AsTracking().FirstAsync(b => b.id == batchId);
        await db.Entry(batch).ReloadAsync();
        if (batch.status != "processing") return;
        var allRefunds = await db.orderPaymentRefund.AsNoTracking().Where(r => r.card_refund_batch_id == batchId).ToListAsync();
        var refunds = allRefunds.GroupBy(r => r.payment_id).Select(g => g.OrderByDescending(r => r.id).First()).ToList();
        if (refunds.Count == 0) return;
        if (refunds.Sum(r => Math.Round((decimal)r.amount, 2)) != batch.amount)
            throw new InvalidOperationException("退款分笔金额不一致，保留冻结并请管理员核对");
        bool allSucceeded = refunds.All(r => r.state == 1 && r.card_refund_state == "success");
        bool allFailed = refunds.All(r => r.card_refund_state == "failed");
        if (!allSucceeded && !allFailed) return;

        int[] ids = await db.cardOrderRefundCard.AsNoTracking().Where(r => r.batch_id == batchId)
            .Select(r => r.card_id).Distinct().OrderBy(id => id).ToArrayAsync();
        foreach (int id in ids) await CareCardSaleService.LockAsync(db, "punch-card:" + id);
        var cards = await db.punchCard.AsTracking().Where(c => ids.Contains(c.id)).ToListAsync();
        if (cards.Count != ids.Length || cards.Count == 0) throw new InvalidOperationException("退款卡记录不完整，保留冻结并请管理员核对");
        foreach (var card in cards)
        {
            await db.Entry(card).ReloadAsync();
            if (allSucceeded) card.is_refund = true;
            card.refund_pending = false;
            card.update_date = DateTime.Now;
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("punch_card", allSucceeded ? "is_refund" : "refund_pending", card.id,
                allSucceeded ? "整单退款成功" : "整单退款失败恢复", null, null,
                allSucceeded ? "0" : "1", allSucceeded ? "1" : "0", "订单 " + batch.order_id));
        }
        batch.status = allSucceeded ? "succeeded" : "failed";
        batch.update_date = DateTime.Now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
