using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Services.Share;

/// <summary>
/// 订单上的介绍人分账：店长以上设置、修改、删除；每次变化后按这一单所有分账的合计重算「自动还是手动」。
/// 设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-02-introducer-commission-design.md
/// </summary>
public sealed class OrderShareService(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http)
{
    /// <param name="state">待自动分账 / 手动处理 / 已分账 / 被拒绝</param>
    /// <param name="planDate">预计分账日期 yyyy-MM-dd，没指定为 null</param>
    public sealed record OrderShareView(int orderShareId, int introducerId, string name, string type, double amount, string state, string? memo,
        string? planDate);

    /// <summary>租赁单预填的金额：租金（扣减免后，同万龙那份的基数）的 10%；其他订单没有默认值。</summary>
    public async Task<double> SuggestedAmountAsync(int orderId)
    {
        Models.Order order = await new OrderController(db, config, http).GetOrder(orderId);
        if (order == null || order.type != "租赁" || order.rentals == null) return 0;
        return IntroducerRules.SuggestedRentShare(order.rentals.Sum(r => WanlongShareRules.RentAfterDiscounts(r)));
    }

    public async Task<List<OrderShareView>> ListAsync(int orderId)
    {
        List<OrderShare> shares = await db.orderShare.AsNoTracking().Where(s => s.valid && s.order_id == orderId).ToListAsync();
        List<int> relationIds = shares.Select(s => s.relation_id).ToList();
        List<Introducer> introducers = await db.introducer.AsNoTracking().Where(i => relationIds.Contains(i.relation_id)).ToListAsync();
        List<int> shareIds = shares.Select(s => s.id).ToList();
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(r => shareIds.Contains(r.share_id) && r.valid).ToListAsync();
        List<OrderShareView> views = new();
        foreach (OrderShare share in shares.OrderBy(s => s.id))
        {
            Introducer? introducer = introducers.FirstOrDefault(i => i.relation_id == share.relation_id);
            if (introducer == null) continue;   // 万龙那条和别的应分不在这里显示
            List<PaymentShare> mine = rows.Where(r => r.share_id == share.id).ToList();
            string state = mine.Any(r => r.success == true) ? "已分账"
                : mine.Any(r => r.submit_time != null) ? "被拒绝"
                : mine.Count == 0 || mine.Any(r => r.can_not_share) ? "手动处理" : "待自动分账";
            views.Add(new OrderShareView(share.id, introducer.id, introducer.name, introducer.type, share.amount, state, mine.FirstOrDefault()?.memo,
                share.plan_date?.ToString("yyyy-MM-dd")));
        }
        return views;
    }

    /// <param name="planDate">预计分账日期；null = 不指定，按默认规则（关单次日起的第一个夜间任务）。不能早于今天，已经存着的旧日期原样再存不算。</param>
    /// <param name="autoShare">false = 店长把这条设成手动分账（只记账，不自动提交）；true = 能自动就自动，系统判断必须手动的（用券、没账号、超门槛）仍是手动。</param>
    public async Task<OrderShareView> SetIntroducerShareAsync(Staff op, int orderId, int introducerId, double amount, string? memo,
        DateTime? planDate = null, bool autoShare = true)
    {
        if (!StaffAccountRules.IsManager(op)) throw new ShareException("只有店长以上可以设置介绍人分账", 3);
        Models.Order order = await new OrderController(db, config, http).GetOrder(orderId);
        if (order == null || order.valid != 1) throw new ShareException("订单不存在");
        string? reason = ShareTriggerRules.ReasonCannotSet(order);
        if (reason != null) throw new ShareException(reason);
        amount = Math.Round(amount, 2);
        if (!(amount > 0)) throw new ShareException("请填写大于 0 的金额");
        Introducer introducer = await db.introducer.AsNoTracking().FirstOrDefaultAsync(i => i.id == introducerId && i.valid == 1)
            ?? throw new ShareException("介绍人不存在或已停用");

        OrderShare? existing = await db.orderShare.AsNoTracking()
            .FirstOrDefaultAsync(s => s.valid && s.order_id == orderId && s.relation_id == introducer.relation_id);
        int existingId = existing?.id ?? 0;
        List<PaymentShare> existingRecords = existing == null ? new()
            : await db.paymentShare.AsNoTracking().Where(r => r.share_id == existingId && r.valid).ToListAsync();
        if (existingRecords.Any(r => r.submit_time != null)) throw new ShareException("已提交，不能修改");
        planDate = planDate?.Date;
        if (planDate != null && planDate < DateTime.Today && planDate != existing?.plan_date?.Date)
        {
            throw new ShareException("预计分账日期不能早于今天");
        }

        double others = await db.orderShare.AsNoTracking().Where(s => s.valid && s.order_id == orderId && s.id != existingId).SumAsync(s => s.amount);
        double net = (await db.orderPayment.AsNoTracking().Include(p => p.refunds)
            .Where(p => p.order_id == orderId && p.valid == 1 && p.status == "支付成功").ToListAsync()).Sum(p => p.unRefundedAmount);
        if (others + amount > net + 0.001) throw new ShareException("分账合计不能超过实收扣掉已退款");

        foreach (PaymentShare r in existingRecords)
        {
            r.valid = false;
            r.update_date = DateTime.Now;
            db.paymentShare.Update(r);
        }
        OrderShare share;
        if (existing == null)
        {
            share = new OrderShare { order_id = orderId, relation_id = introducer.relation_id, amount = amount, valid = true, create_date = DateTime.Now,
                plan_date = planDate, auto_share = autoShare };
            db.orderShare.Add(share);
        }
        else
        {
            share = existing;
            share.amount = amount;
            share.plan_date = planDate;
            share.auto_share = autoShare;
            share.dealed = false;
            db.orderShare.Update(share);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        List<PaymentShare> records = await new OrderShareController(db, config, http).CreatePaymentShare(share);
        string? userMemo = string.IsNullOrWhiteSpace(memo) ? null : memo.Trim();
        if (userMemo != null)
        {
            foreach (PaymentShare r in records)
            {
                r.memo = ShareMemoRules.WithAuto(userMemo, r.memo);   // 人工备注在前，系统写的原因接在后面
                db.paymentShare.Update(r);
            }
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        await ReevaluateAsync(orderId);

        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "amount", share.id, "设置介绍人分账", null, op.id,
            existing == null ? null : existing.amount.ToString("0.##"), amount.ToString("0.##"),
            "订单 " + orderId + "，介绍人 " + introducer.name + "（" + introducer.id + "）"
            + (planDate == null ? "" : "，预计分账日期 " + planDate.Value.ToString("yyyy-MM-dd"))
            + (autoShare ? "" : "，手动分账")));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (await ListAsync(orderId)).First(v => v.orderShareId == share.id);
    }

    public async Task RemoveIntroducerShareAsync(Staff op, int orderShareId)
    {
        if (!StaffAccountRules.IsManager(op)) throw new ShareException("只有店长以上可以删除介绍人分账", 3);
        OrderShare share = await db.orderShare.AsNoTracking().FirstOrDefaultAsync(s => s.id == orderShareId && s.valid)
            ?? throw new ShareException("没有找到这条分账");
        if (!await db.introducer.AsNoTracking().AnyAsync(i => i.relation_id == share.relation_id))
        {
            throw new ShareException("这不是介绍人分账，不能在这里删除");
        }
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(r => r.share_id == orderShareId && r.valid).ToListAsync();
        if (rows.Any(r => r.submit_time != null)) throw new ShareException("已提交，不能删除");
        Invalidate(share, rows);
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "valid", share.id, "删除介绍人分账", null, op.id,
            "1", "0", "订单 " + share.order_id + "，金额 " + share.amount.ToString("0.##")));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await ReevaluateAsync(share.order_id);
    }

    /// <summary>订单被全额退款后：还没提交的介绍人分账作废，已经提交的不动。</summary>
    public async Task VoidUnsubmittedIntroducerSharesAsync(int orderId)
    {
        List<int> introducerRelationIds = await db.introducer.AsNoTracking().Select(i => i.relation_id).ToListAsync();
        List<OrderShare> shares = await db.orderShare.AsNoTracking()
            .Where(s => s.valid && s.order_id == orderId && introducerRelationIds.Contains(s.relation_id)).ToListAsync();
        foreach (OrderShare share in shares)
        {
            List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(r => r.share_id == share.id && r.valid).ToListAsync();
            if (rows.Any(r => r.submit_time != null)) continue;
            Invalidate(share, rows);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await ReevaluateAsync(orderId);
    }

    /// <summary>
    /// 按这一单所有有效应分的合计，重算每条还没提交的分账记录：自动还是手动，以及写在备注里的系统原因。
    /// 已提交的不碰；人工备注原样保留；用券的、介绍人没有收款账号或没绑定的、合计超过 30% 门槛的，都标手动。
    /// </summary>
    public async Task ReevaluateAsync(int orderId)
    {
        if (await db.order.AsNoTracking().AnyAsync(o => o.id == orderId && o.hide)) return;
        List<OrderShare> shares = await db.orderShare.AsNoTracking().Where(s => s.valid && s.order_id == orderId).ToListAsync();
        if (shares.Count == 0) return;
        List<OrderPayment> payments = await db.orderPayment.AsNoTracking().Include(p => p.refunds)
            .Where(p => p.order_id == orderId && p.valid == 1 && p.status == "支付成功" && (p.pay_method == "微信支付" || p.pay_method == "支付宝"))
            .ToListAsync();
        bool over = ShareCapRules.IsOverCap(shares.Sum(s => s.amount), payments);
        bool usedTicket = await ShareQueries.UsedTicketAsync(db, orderId);

        List<int> relationIds = shares.Select(s => s.relation_id).Distinct().ToList();
        List<int> introducerRelationIds = await db.introducer.AsNoTracking().Where(i => relationIds.Contains(i.relation_id)).Select(i => i.relation_id).ToListAsync();
        Dictionary<int, OrderShareRelation> relations = (await db.orderShareRelation.AsNoTracking().Include(r => r.binds)
            .Where(r => introducerRelationIds.Contains(r.id)).ToListAsync()).ToDictionary(r => r.id);
        List<int> shareIds = shares.Select(s => s.id).ToList();
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking()
            .Where(r => shareIds.Contains(r.share_id) && r.valid && r.submit_time == null).ToListAsync();

        foreach (PaymentShare row in rows)
        {
            OrderPayment? payment = payments.FirstOrDefault(p => p.id == row.payment_id);
            if (payment == null) continue;
            OrderShare share = shares.First(s => s.id == row.share_id);
            string? problem = relations.TryGetValue(share.relation_id, out OrderShareRelation? relation)
                ? ShareReceiverRules.Problem(relation, relation.binds ?? new List<ShareRelationBind>(), payment.pay_method) : null;
            string? autoMemo = usedTicket ? ShareMemoRules.TicketMemo
                : (problem ?? (over ? ShareCapRules.OverCapMemo(payment) : (share.auto_share ? null : ShareMemoRules.ManualChosenMemo)));
            bool flag = usedTicket || over || problem != null || !share.auto_share;
            string? memo = ShareMemoRules.WithAuto(row.memo, autoMemo);
            if (row.can_not_share == flag && row.memo == memo) continue;
            row.can_not_share = flag;
            row.memo = memo;
            row.update_date = DateTime.Now;
            db.paymentShare.Update(row);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private void Invalidate(OrderShare share, List<PaymentShare> rows)
    {
        share.valid = false;
        db.orderShare.Update(share);
        foreach (PaymentShare r in rows)
        {
            r.valid = false;
            r.update_date = DateTime.Now;
            db.paymentShare.Update(r);
        }
    }
}
