using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;

namespace SnowmeetApi.Services;

/// <summary>
/// 「老会员」= 会员在系统里有过消费记录（用户 2026-10-30 确认的口径）。
///
/// 同一会员名下，存在任意业务类型（零售 / 租赁 / 养护 / 雪票 / 餐饮 …）的订单，
/// 订单有效（valid = 1）、非测试（is_test = 0），且至少一笔真实支付：
/// order_payment 的 valid = 1、status = 支付成功、is_debt = 0、amount &gt; 0。
/// 招待 / 质保（没有真实支付）、未付款、已取消、测试单都不算；没有会员（散客）一律不是老会员。
///
/// 当前这张单不会算进去：养护开单时订单还是草稿（valid = 0），而且还没有任何支付。
/// 库里的「老顾客」原本只指持有老顾客优惠券（模板 16），和有没有消费过对不上，所以这里单独定义。
/// </summary>
public static class MemberConsumption
{
    public static async Task<bool> HasPaidOrderAsync(ApplicationDBContext db, int? memberId)
    {
        if (memberId == null || memberId <= 0)
        {
            return false;
        }
        return await db.order.AsNoTracking().AnyAsync(o => o.member_id == memberId && o.valid == 1 && o.is_test == 0
            && db.orderPayment.Any(p => p.order_id == o.id && p.valid == 1 && p.status == "支付成功"
                && p.is_debt == 0 && p.amount > 0));
    }
}
