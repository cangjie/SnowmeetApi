using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Share;

/// <summary>
/// 票取消了，这张票那 1 元系统分账也取消（2026-11-18 用户：票取消了，分账也应该是取消状态）。
/// 取消 = 把 order_share、payment_share 标成无效（valid = false，和分销商撤销分账同一做法），备注写明原因；
/// 无效的分账不会被夜间任务、取票后的自动分账提交，也不会出现在待分账列表里。
/// 只作废「还没提交给微信」的：已经提交过（处理中、已成功、被拒绝）的不动，那是真实发生过的结果。
/// 只动这张票自己的系统分账（金额 1、没有 share_method、没有记账批次）；介绍人分账、别的票的分账都不碰。
/// 「后台大好河山雪票」页的分账记录卡片对已取消的票显示「已取消」，见 SkipassShareRecordService。
/// </summary>
public sealed class SkipassShareCancelService(ApplicationDBContext db)
{
    public const string VoidMemo = "票已取消，这 1 元不再分账";

    /// <summary>作废这张票没提交的 1 元系统分账，返回被作废的 order_share.id（没有可作废的返回空列表，可以重复调用）。</summary>
    public async Task<List<int>> CancelPendingAsync(int skipassId)
    {
        List<int> ids = await db.orderShare.AsNoTracking()
            .Where(s => s.valid && s.skipass_id == skipassId && s.share_method == null && s.amount == 1 && s.batch_id == null)
            .Where(s => !db.paymentShare.Any(p => p.share_id == s.id && p.valid && p.submit_time != null))
            .Select(s => s.id).ToListAsync();
        if (ids.Count == 0) return ids;

        // 两张表一起改，不能只改一半：已经有事务就接在它里面
        var tx = db.Database.CurrentTransaction == null ? await db.Database.BeginTransactionAsync() : null;
        try
        {
            DateTime now = DateTime.Now;
            await db.paymentShare.Where(p => ids.Contains(p.share_id) && p.valid && p.submit_time == null)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.valid, false).SetProperty(p => p.memo, VoidMemo)
                    .SetProperty(p => p.update_date, (DateTime?)now));
            await db.orderShare.Where(s => ids.Contains(s.id)).ExecuteUpdateAsync(x => x.SetProperty(s => s.valid, false));
            if (tx != null) await tx.CommitAsync();
        }
        finally
        {
            if (tx != null) await tx.DisposeAsync();
        }
        return ids;
    }
}
