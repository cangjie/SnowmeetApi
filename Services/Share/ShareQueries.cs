using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services.Share;

// 分账记录生成和重算共用的查询。
internal static class ShareQueries
{
    /// <summary>
    /// 2026-10-31 用户要求：用了优惠券的租赁单照常生成分账记录，但不自动分账（夜间 ExecuteWLShare 跳过 can_not_share），需要时手动分。
    /// 券号为空串的是历史迁移数据，不算用了券；别的订单的券不算。
    /// </summary>
    /// <summary>
    /// 这条收款方是不是某个介绍人的。新版后端可能先于建表脚本上线（introducer 表还不存在）：雪票付款回调和夜间关单
    /// 都要生成分账记录，不能因为这张表不存在就报错，查不到就当作不是介绍人（也就不做介绍人的收款方检查）。
    /// </summary>
    internal static async Task<bool> IsIntroducerRelationAsync(ApplicationDBContext db, int relationId)
    {
        try
        {
            return await db.introducer.AnyAsync(i => i.relation_id == relationId);
        }
        catch (DbException e)
        {
            Console.WriteLine("introducer 表查不到，按「不是介绍人」处理：" + e.Message);
            return false;
        }
    }

    internal static Task<bool> UsedTicketAsync(ApplicationDBContext db, int orderId)
        => db.discount.AnyAsync(d => d.valid == 1 && d.biz_type == "租赁"
            && d.ticket_code != null && d.ticket_code.Trim() != ""
            && db.rental.Any(r => r.id == d.biz_id && r.order_id == orderId && r.valid == 1));
}
