using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Share;

internal static class ShareChannelValidation
{
    internal static async Task CheckAsync(ApplicationDBContext db, OrderShare share, List<PaymentShare> records)
    {
        if (!share.valid || share.batch_id != null || records.Count == 0 || records.Any(r => r.submit_time != null)
            || Math.Abs(Math.Round(records.Sum(r => r.amount), 2) - Math.Round(share.amount, 2)) > 0.001)
            throw new ShareException("分账记录已提交、金额不完整或状态已变化，请刷新后核对");
        var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == share.order_id)
            ?? throw new ShareException("订单不存在");
        var relation = await db.orderShareRelation.AsNoTracking().Include(r => r.binds).FirstOrDefaultAsync(r => r.id == share.relation_id)
            ?? throw new ShareException("分销商收款信息不存在");
        var ids = records.Select(r => r.payment_id).Distinct().ToList();
        var payments = await db.orderPayment.AsNoTracking().Include(p => p.refunds).Where(p => ids.Contains(p.id)).ToListAsync();
        foreach (var row in records)
        {
            var payment = payments.FirstOrDefault(p => p.id == row.payment_id);
            if (payment == null || payment.order_id != share.order_id || !ShareWorkflowRules.CanUsePayment(order, payment))
                throw new ShareException("订单支付记录已不可分账，请刷新后选择银行转账");
            string? problem = ShareReceiverRules.Problem(relation, relation.binds ?? new List<ShareRelationBind>(), payment.pay_method);
            if (problem != null) throw new ShareException(problem);
            if ((payment.pay_method == "微信支付" && (payment.mch_id == null || string.IsNullOrWhiteSpace(payment.wepay_trans_id)))
                || (payment.pay_method == "支付宝" && string.IsNullOrWhiteSpace(payment.ali_trade_no)))
                throw new ShareException("支付渠道交易信息不完整，无法提交分账");
        }
        foreach (var group in records.GroupBy(r => r.payment_id))
        {
            var payment = payments.First(p => p.id == group.Key);
            if (group.Sum(r => r.amount) > WanlongShareRules.ShareBase(payment) + 0.001)
                throw new ShareException("分账金额超过付款可用金额，请核对退款和申请金额");
        }
    }
}
