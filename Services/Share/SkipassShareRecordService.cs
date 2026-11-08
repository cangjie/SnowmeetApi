using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;

namespace SnowmeetApi.Services.Share;

/// <summary>一张雪票上那笔系统员工分账（1 元）的显示数据。</summary>
public sealed record SkipassShareRecordItem(int orderShareId, string receiver, double amount, string state, string plan,
    string? submitTime, string? successTime, string? memo);

/// <param name="skipassId">雪票 ski_pass.id</param>
/// <param name="pickedUp">这张票是否已取票</param>
/// <param name="record">这张票的分账；一张票最多一笔，没有时为 null</param>
/// <param name="emptyReason">没有分账时说明原因，有分账时为 null</param>
public sealed record SkipassShareRecordResult(int skipassId, bool pickedUp, SkipassShareRecordItem? record, string? emptyReason);

/// <summary>
/// 后台「大好河山雪票」页每张票下面的分账记录：只读，看系统给推荐员工的那 1 元（order_share 金额 1、没有 share_method）。
/// 分账对应的是雪票而不是订单：一张票最多一笔（order_share.skipass_id）。
/// 介绍人分账（有 share_method）不在这里，它们在订单详情的「介绍人分账」卡片里。
/// </summary>
public sealed class SkipassShareRecordService(ApplicationDBContext db)
{
    public const string PlanText = "取票后自动分账";
    const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    public async Task<SkipassShareRecordResult> GetBySkipassAsync(int skipassId)
    {
        Models.SkiPass? pass = await db.skiPass.AsNoTracking().FirstOrDefaultAsync(s => s.id == skipassId);
        if (pass == null || pass.order_id == null) throw new ShareException("雪票不存在");
        Models.Order? order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == pass.order_id && o.valid == 1);
        if (order == null || order.type != "雪票") throw new ShareException("不是雪票订单");
        bool pickedUp = pass.is_used == 1;

        OrderShare? share = await SystemShares(order.id).Where(s => s.skipass_id == skipassId).OrderBy(s => s.id).FirstOrDefaultAsync();
        if (share == null) return new SkipassShareRecordResult(skipassId, pickedUp, null, await EmptyReasonAsync(order));

        OrderShareRelation? relation = await db.orderShareRelation.AsNoTracking().FirstOrDefaultAsync(r => r.id == share.relation_id);
        string receiver = "未知收款人";
        if (relation != null)
        {
            string? staffName = relation.staff_id != null ? (await db.staff.AsNoTracking().FirstOrDefaultAsync(s => s.id == relation.staff_id))?.name : null;
            receiver = !string.IsNullOrWhiteSpace(staffName) ? staffName.Trim()
                : !string.IsNullOrWhiteSpace(relation.name) ? relation.name.Trim() : receiver;
        }
        List<PaymentShare> rows = await db.paymentShare.AsNoTracking().Where(p => p.valid && p.share_id == share.id).ToListAsync();
        return new SkipassShareRecordResult(skipassId, pickedUp, Describe(share, receiver, rows), null);
    }

    /// <summary>订单上有效的系统员工分账：金额 1、没有 share_method（金额恰好是 1 的介绍人分账靠 share_method 区分）。</summary>
    IQueryable<OrderShare> SystemShares(int orderId)
        => db.orderShare.AsNoTracking().Where(s => s.valid && s.order_id == orderId && s.share_method == null && s.amount == 1);

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
        List<OrderShare> orderShares = await SystemShares(order.id).ToListAsync();
        if (orderShares.Any(s => s.skipass_id == null)) return "旧订单的分账没有记到具体的票上";
        if (orderShares.Count > 0) return "这张票没有分账";
        if (order.staff_id == null) return "这单没有记录推荐员工，不产生分账";
        Staff? staff = await db.staff.AsNoTracking().FirstOrDefaultAsync(s => s.id == order.staff_id);
        string name = string.IsNullOrWhiteSpace(staff?.name) ? "员工" + order.staff_id : staff!.name!.Trim();
        if (staff == null || staff.valid != 1) return "推荐员工「" + name + "」已离职，不产生分账";
        if (!await db.orderShareRelation.AsNoTracking().AnyAsync(r => r.staff_id == staff.id))
            return "推荐员工「" + name + "」没有分账收款账号，不产生分账";
        return "这张票还没有分账记录";
    }
}
