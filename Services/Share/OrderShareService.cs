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
/// 订单上的分销商分账：店长以上提交申请；方式、审核及支付资格由服务端校验。
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

    public async Task<bool> ChannelShareAvailableAsync(Models.Order order)
    {
        var payments = await db.orderPayment.AsNoTracking().Include(p => p.refunds)
            .Where(p => p.order_id == order.id && p.valid == 1 && p.status == "支付成功").ToListAsync();
        return ShareWorkflowRules.CanUseChannel(order, payments);
    }

    public async Task<List<OrderShareView>> ListAsync(int orderId)
    {
        bool ticket = await db.order.AsNoTracking().AnyAsync(o => o.id == orderId && o.type == "雪票");
        List<OrderShare> shares = await db.orderShare.AsNoTracking().Where(s => s.valid && s.order_id == orderId
            && !(ticket && s.share_method == null && s.amount == 1)).ToListAsync();
        List<int> relationIds = shares.Select(s => s.relation_id).ToList();
        Dictionary<int, Introducer> introducers = await ShareQueries.IntroducersByRelationAsync(db, relationIds);
        List<int> shareIds = shares.Select(s => s.id).ToList();
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(r => shareIds.Contains(r.share_id) && r.valid).ToListAsync();
        List<OrderShareView> views = new();
        foreach (OrderShare share in shares.OrderBy(s => s.id))
        {
            Introducer? introducer = introducers.GetValueOrDefault(share.relation_id);
            if (introducer == null) continue;   // 万龙那条和别的应分不在这里显示
            List<PaymentShare> mine = rows.Where(r => r.share_id == share.id).ToList();
            var facts = new ShareFacts(share.auto_share, share.batch_id,
                mine.Select(r => new RecordFacts(r.success, r.submit_time, r.response_time, r.can_not_share)).ToList());
            string method = ShareWorkflowRules.Method(share, facts);
            bool completed = ShareListRules.Status(facts) == "已分" && (share.share_method == null || share.batch_id != null
                || Math.Abs(Math.Round(mine.Sum(r => r.amount), 2) - Math.Round(share.amount, 2)) <= 0.001);
            string state = completed ? "已分账"
                : mine.Any(r => r.submit_time != null && r.response_time == null && r.success != true) ? "处理中"
                : mine.Any(r => r.submit_time != null && r.response_time != null && r.success != true) ? "被拒绝"
                : mine.Any(r => r.success == true) ? "部分分账"
                : method == ShareWorkflowRules.Auto && share.approved_at == null ? "待审核"
                : method == ShareWorkflowRules.Bank ? "待银行转账"
                : method == ShareWorkflowRules.Manual ? "待手动分账" : "待自动分账";
            views.Add(new OrderShareView(share.id, introducer.id, introducer.name, introducer.type, share.amount, state, mine.FirstOrDefault()?.memo ?? share.application_memo,
                share.plan_date?.ToString("yyyy-MM-dd")));
        }
        return views;
    }

    /// <param name="planDate">预计分账日期；null = 不指定，按默认规则（关单次日起的第一个夜间任务）。不能早于今天，已经存着的旧日期原样再存不算。</param>
    /// <param name="autoShare">旧客户端开关；关闭表示银行转账，新客户端使用 shareMethod。</param>
    public async Task<OrderShareView> SetIntroducerShareAsync(Staff op, int orderId, int introducerId, double amount, string? memo,
        DateTime? planDate = null, bool autoShare = true, string? shareMethod = null)
    {
        if (!StaffAccountRules.IsManager(op)) throw new ShareException("只有店长以上可以设置介绍人分账", 3);
        Models.Order order = await new OrderController(db, config, http).GetOrder(orderId);
        if (order == null || order.valid != 1) throw new ShareException("订单不存在");
        string? reason = ShareTriggerRules.ReasonCannotSet(order);
        if (reason != null) throw new ShareException(reason);
        bool channelAvailable = await ChannelShareAvailableAsync(order);
        string method = shareMethod ?? (autoShare && channelAvailable ? ShareWorkflowRules.Auto : ShareWorkflowRules.Bank);
        if (!ShareWorkflowRules.IsValidMethod(method)) throw new ShareException("请选择有效的分账方式");
        if (method != ShareWorkflowRules.Bank && !channelAvailable) throw new ShareException("该订单没有可分账的支付记录，请选择银行转账");
        amount = Math.Round(amount, 2);
        if (!(amount > 0)) throw new ShareException("请填写大于 0 的金额");
        // 锁住申请使用的收款关系，并让应分、付款分账和操作日志共同提交或回滚。
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        Introducer introducer = await db.introducer.AsNoTracking().FirstOrDefaultAsync(i => i.id == introducerId && i.valid == 1)
            ?? throw new ShareException("介绍人不存在或已停用");

        List<int> priorRelations = await db.introducerRelationHistory.AsNoTracking().Where(h => h.introducer_id == introducerId)
            .Select(h => h.relation_id).ToListAsync();
        bool ticketOrder = order.type == "雪票";
        if (await db.orderShare.AsNoTracking().AnyAsync(s => s.valid && s.order_id == orderId && priorRelations.Contains(s.relation_id)
            && !(ticketOrder && s.share_method == null && s.amount == 1)))
            throw new ShareException("该分销商在此订单已有历史收款账号的分账，请先处理原申请");

        OrderShare? existing = await db.orderShare.AsNoTracking()
            .FirstOrDefaultAsync(s => s.valid && s.order_id == orderId && s.relation_id == introducer.relation_id
                && !(ticketOrder && s.share_method == null && s.amount == 1));
        int existingId = existing?.id ?? 0;
        List<PaymentShare> existingRecords = existing == null ? new()
            : await db.paymentShare.AsNoTracking().Where(r => r.share_id == existingId && r.valid).ToListAsync();
        if (existing?.batch_id != null || existingRecords.Any(r => r.submit_time != null)) throw new ShareException("已提交或已记账，不能修改");
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
                plan_date = planDate, auto_share = method == ShareWorkflowRules.Auto, share_method = method, application_memo = memo?.Trim() };
            db.orderShare.Add(share);
        }
        else
        {
            share = existing;
            share.amount = amount;
            share.plan_date = planDate;
            share.auto_share = method == ShareWorkflowRules.Auto;
            share.share_method = method;
            share.application_memo = memo?.Trim();
            share.approved_by_staff_id = null;
            share.approved_at = null;
            share.dealed = false;
            db.orderShare.Update(share);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        List<PaymentShare> records = method == ShareWorkflowRules.Bank ? new() : await new OrderShareController(db, config, http).CreatePaymentShare(share);
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
            + "，分账方式 " + method));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        OrderShareView result = (await ListAsync(orderId)).First(v => v.orderShareId == share.id);
        await tx.CommitAsync();
        return result;
    }

    public async Task RemoveIntroducerShareAsync(Staff op, int orderShareId)
    {
        if (!StaffAccountRules.IsManager(op)) throw new ShareException("只有店长以上可以删除介绍人分账", 3);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        OrderShare share = await db.orderShare.AsNoTracking().FirstOrDefaultAsync(s => s.id == orderShareId && s.valid)
            ?? throw new ShareException("没有找到这条分账");
        if (share.share_method == null && share.amount == 1
            && await db.order.AsNoTracking().AnyAsync(o => o.id == share.order_id && o.type == "雪票"))
            throw new ShareException("雪票系统分账不能从分销商申请中删除");
        if (!await ShareQueries.IsIntroducerRelationAsync(db, share.relation_id))
        {
            throw new ShareException("这不是介绍人分账，不能在这里删除");
        }
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(r => r.share_id == orderShareId && r.valid).ToListAsync();
        if (share.batch_id != null || rows.Any(r => r.submit_time != null)) throw new ShareException("已提交或已记账，不能删除");
        Invalidate(share, rows);
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("order_share", "valid", share.id, "删除介绍人分账", null, op.id,
            "1", "0", "订单 " + share.order_id + "，金额 " + share.amount.ToString("0.##")));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await ReevaluateAsync(share.order_id);
        await tx.CommitAsync();
    }

    /// <summary>订单被全额退款后：还没提交的介绍人分账作废，已经提交的不动。</summary>
    public async Task VoidUnsubmittedIntroducerSharesAsync(int orderId)
    {
        List<int> introducerRelationIds = await ShareQueries.AllIntroducerRelationsAsync(db);
        bool ticket = await db.order.AsNoTracking().AnyAsync(o => o.id == orderId && o.type == "雪票");
        List<OrderShare> shares = await db.orderShare.AsNoTracking()
            .Where(s => s.valid && s.order_id == orderId && introducerRelationIds.Contains(s.relation_id)).ToListAsync();
        foreach (OrderShare share in shares)
        {
            if (share.batch_id != null || (ticket && share.share_method == null && share.amount == 1)) continue;
            List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(r => r.share_id == share.id && r.valid).ToListAsync();
            if (rows.Any(r => r.submit_time != null)) continue;
            Invalidate(share, rows);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await ReevaluateAsync(orderId);
    }

    /// <summary>
    /// 重新计算未提交记录的渠道提示；已提交和银行转账记账的记录保持原样。
    /// </summary>
    public async Task ReevaluateAsync(int orderId)
    {
        if (await db.order.AsNoTracking().AnyAsync(o => o.id == orderId && o.hide)) return;
        bool ticket = await db.order.AsNoTracking().AnyAsync(o => o.id == orderId && o.type == "雪票");
        List<OrderShare> shares = await db.orderShare.AsNoTracking().Where(s => s.valid && s.order_id == orderId).ToListAsync();
        if (shares.Count == 0) return;
        List<OrderPayment> payments = await db.orderPayment.AsNoTracking().Include(p => p.refunds)
            .Where(p => p.order_id == orderId && p.valid == 1 && p.status == "支付成功" && p.pay_method != null
                && (p.pay_method.Trim() == "微信支付" || p.pay_method.Trim() == "支付宝"))
            .ToListAsync();
        bool over = ShareCapRules.IsOverCap(shares.Where(s => s.share_method != ShareWorkflowRules.Bank).Sum(s => s.amount), payments);
        bool usedTicket = await ShareQueries.UsedTicketAsync(db, orderId);

        List<int> relationIds = shares.Select(s => s.relation_id).Distinct().ToList();
        List<int> introducerRelationIds = (await ShareQueries.IntroducersByRelationAsync(db, relationIds)).Keys.ToList();
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
            if (share.batch_id != null || (ticket && share.share_method == null && share.amount == 1)) continue;
            string? problem = relations.TryGetValue(share.relation_id, out OrderShareRelation? relation)
                ? ShareReceiverRules.Problem(relation, relation.binds ?? new List<ShareRelationBind>(), payment.pay_method) : null;
            string? autoMemo = usedTicket ? ShareMemoRules.TicketMemo
                : (problem ?? (over ? ShareCapRules.OverCapMemo(payment) : (share.auto_share ? null : ShareMemoRules.ManualChosenMemo)));
            bool flag = usedTicket || over || problem != null || !share.auto_share;
            if (share.share_method == ShareWorkflowRules.Auto) { autoMemo = problem; flag = problem != null; }
            if (share.share_method == ShareWorkflowRules.Manual) { autoMemo = problem ?? ShareMemoRules.ManualChosenMemo; flag = true; }
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
