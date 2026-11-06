using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 租赁订单手动关单（租赁订单详情页「订单关闭」按钮，2026-11-06 用户要求）：
    ///   订单全部退完押金，或者无需退押金且全部租赁物都已归还，才能关；关单不可逆。
    /// 页面显示按钮（order.canManualClose）和关单接口校验共用 CanManualClose，避免两边口径不一致。
    /// 口径沿用订单的租赁状态（Order.rentProperties.rentStatus）和夜间关单（RentController.CloseOrder）：
    ///   · 全额退押金 → 可以关；
    ///   · 全部归还，且应退押金刚好为 0（无需退押金）→ 可以关；应退押金 &gt; 0 是还没退，&lt; 0 是客人还欠租金，都不关；
    ///   · 其余状态（未支付、未开始、租赁中、部分归还、部分退押金、已关闭）都不关；
    ///   · 还有应收没收齐（paying_amount &gt; 0）不关，与夜间关单一致。
    /// </summary>
    public static class RentOrderCloseRules
    {
        // 能手动关单的最低职级（与 OrderController.SetOrderCloseStatus 一致）
        public const int MIN_STAFF_LEVEL = 100;

        public static bool CanManualClose(Order? order)
        {
            if (order == null || order.type != "租赁" || order.valid != 1 || order.closed != 0)
            {
                return false;
            }
            if (order.paying_amount.HasValue && Math.Round(order.paying_amount.Value, 2) > 0)
            {
                return false;
            }
            string? status = order.rentProperties?.rentStatus;
            if (status == Order.RentStatus.全额退押金.ToString())
            {
                return true;
            }
            if (status == Order.RentStatus.全部归还.ToString())
            {
                // 应退押金 = 已收押金 − 应收费用 + 储值付租金（与 rentProperties 里算「退押金状态」的口径一致）
                double needRefund = (order.totalGuarantyAmount ?? 0) - (order.totalRentSummaryAmount ?? 0) + order.depositPaidAmount;
                return Math.Round(needRefund, 2) == 0;
            }
            return false;
        }
    }
}
