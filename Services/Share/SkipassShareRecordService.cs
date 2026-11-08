using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Share;

/// <summary>雪票订单上一条系统员工分账（每张票 1 元）的显示数据。</summary>
public sealed record SkipassShareRecordItem(int orderShareId, string receiver, double amount, string state, string plan,
    string? submitTime, string? successTime, string? memo);

/// <param name="ticketCount">订单上没有取消的雪票张数</param>
/// <param name="pickedUpCount">其中已取票的张数</param>
/// <param name="emptyReason">没有任何分账记录时说明原因，有记录时为 null</param>
public sealed record SkipassShareRecordResult(int ticketCount, int pickedUpCount, List<SkipassShareRecordItem> records, string? emptyReason);

/// <summary>
/// 后台「大好河山雪票」页每张票下面的分账记录：只读，看系统给推荐员工的分账（每张票 1 元，order_share 金额 1、没有 share_method）。
/// 介绍人分账（有 share_method）不在这里，它们在订单详情的「介绍人分账」卡片里。
/// </summary>
public sealed class SkipassShareRecordService(ApplicationDBContext db)
{
    public const string PlanText = "取票后自动分账";
    const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    public async Task<SkipassShareRecordResult> ListAsync(int orderId)
    {
        Models.Order? order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == orderId && o.valid == 1);
        if (order == null) throw new ShareException("订单不存在");
        if (order.type != "雪票") throw new ShareException("不是雪票订单");

        List<Models.SkiPass> passes = await db.skiPass.AsNoTracking().Where(s => s.order_id == orderId && s.is_cancel == 0).ToListAsync();
        List<OrderShare> shares = await db.orderShare.AsNoTracking()
            .Where(s => s.valid && s.order_id == orderId && s.share_method == null && s.amount == 1).OrderBy(s => s.id).ToListAsync();
        if (shares.Count == 0)
            return new SkipassShareRecordResult(passes.Count, passes.Count(p => p.is_used == 1), new List<SkipassShareRecordItem>(), await EmptyReasonAsync(order));

        List<int> relationIds = shares.Select(s => s.relation_id).Distinct().ToList();
        Dictionary<int, OrderShareRelation> relations = (await db.orderShareRelation.AsNoTracking()
            .Where(r => relationIds.Contains(r.id)).ToListAsync()).ToDictionary(r => r.id);
        List<int> staffIds = relations.Values.Where(r => r.staff_id != null).Select(r => r.staff_id!.Value).Distinct().ToList();
        Dictionary<int, string?> staffNames = (await db.staff.AsNoTracking().Where(s => staffIds.Contains(s.id)).ToListAsync())
            .ToDictionary(s => s.id, s => s.name);
        List<int> shareIds = shares.Select(s => s.id).ToList();
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(p => p.valid && shareIds.Contains(p.share_id)).ToListAsync();

        List<SkipassShareRecordItem> items = shares.Select(s =>
        {
            string receiver = "未知收款人";
            if (relations.TryGetValue(s.relation_id, out OrderShareRelation? relation))
            {
                string? staffName = relation.staff_id != null ? staffNames.GetValueOrDefault(relation.staff_id.Value) : null;
                receiver = !string.IsNullOrWhiteSpace(staffName) ? staffName.Trim()
                    : !string.IsNullOrWhiteSpace(relation.name) ? relation.name.Trim() : receiver;
            }
            return Describe(s, receiver, rows.Where(r => r.share_id == s.id).ToList());
        }).ToList();
        return new SkipassShareRecordResult(passes.Count, passes.Count(p => p.is_used == 1), items, null);
    }

    static SkipassShareRecordItem Describe(OrderShare share, string receiver, List<PaymentShare> mine)
    {
        double succeeded = mine.Where(r => r.success == true).Sum(r => r.amount);
        DateTime? submit = mine.Where(r => r.submit_time != null).Select(r => r.submit_time).Max();
        string? submitText = submit?.ToString(TimeFormat);
        string plan = share.plan_date != null ? share.plan_date.Value.ToString("yyyy-MM-dd") : PlanText;

        if (mine.Count > 0 && Math.Round(succeeded, 2) >= Math.Round(share.amount, 2) - 0.001)
        {
            DateTime? done = mine.Where(r => r.success == true && r.response_time != null).Select(r => r.response_time).Max();
            return new SkipassShareRecordItem(share.id, receiver, share.amount, "已分账", plan, submitText, done?.ToString(TimeFormat), null);
        }
        if (succeeded > 0)
            return new SkipassShareRecordItem(share.id, receiver, share.amount, "部分分账", plan, submitText, null, "部分已分，其余待核对");
        if (mine.Any(r => r.submit_time != null && r.response_time == null))
            return new SkipassShareRecordItem(share.id, receiver, share.amount, "处理中", plan, submitText, null, null);
        PaymentShare? rejected = mine.FirstOrDefault(r => r.submit_time != null && r.response_time != null);
        if (rejected != null)
            return new SkipassShareRecordItem(share.id, receiver, share.amount, "被拒绝", plan, submitText, null, (rejected.response_content ?? "").Trim());
        PaymentShare? blocked = mine.FirstOrDefault(r => r.can_not_share);
        if (blocked != null)
            return new SkipassShareRecordItem(share.id, receiver, share.amount, "不能自动分", plan, null, null, blocked.memo);
        return new SkipassShareRecordItem(share.id, receiver, share.amount, "待分账", plan, null, null, null);
    }

    async Task<string> EmptyReasonAsync(Models.Order order)
    {
        if (order.staff_id == null) return "这单没有记录推荐员工，不产生分账";
        Staff? staff = await db.staff.AsNoTracking().FirstOrDefaultAsync(s => s.id == order.staff_id);
        string name = string.IsNullOrWhiteSpace(staff?.name) ? "员工" + order.staff_id : staff!.name!.Trim();
        if (staff == null || staff.valid != 1) return "推荐员工「" + name + "」已离职，不产生分账";
        if (!await db.orderShareRelation.AsNoTracking().AnyAsync(r => r.staff_id == staff.id))
            return "推荐员工「" + name + "」没有分账收款账号，不产生分账";
        return "还没有生成分账记录";
    }
}
