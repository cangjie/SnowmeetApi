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
    internal static Task<bool> UsedTicketAsync(ApplicationDBContext db, int orderId)
        => db.discount.AnyAsync(d => d.valid == 1 && d.biz_type == "租赁"
            && d.ticket_code != null && d.ticket_code.Trim() != ""
            && db.rental.Any(r => r.id == d.biz_id && r.order_id == orderId && r.valid == 1));
}
