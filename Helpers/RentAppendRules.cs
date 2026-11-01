using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 租赁追加的规则。
    public static class RentAppendRules
    {
        // 主单是否已生效：生效标记已置上（含招待等 0 元单），或已经收到款（部分历史单有款但没置标记）。
        // 2026-11-01 用户要求：主单没付款、没生效时不能确认追加，否则免押追加项会当场「已发放」。
        public static bool MainOrderEffective(Order order)
        {
            return order.dealed == 1 || order.paidAmount > 0;
        }
    }
}
