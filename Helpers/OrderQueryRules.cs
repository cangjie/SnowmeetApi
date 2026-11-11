using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    public static class OrderQueryRules
    {
        public static List<Order> FilterByCustomerCellSuffix(IEnumerable<Order> orders, string? cellSuffix)
        {
            string? normalized = string.IsNullOrWhiteSpace(cellSuffix) ? null : cellSuffix.Trim();
            if (normalized == null)
            {
                return orders.ToList();
            }
            return orders.Where(order => order.customerCell.EndsWith(normalized, StringComparison.Ordinal)).ToList();
        }

        // 顾客自助购买次卡/季卡下单时，零售明细的 retail_type = 商品类目 biz_type + "卡类"（RentController.PlaceMyPunchCardOrder）
        private static readonly string[] SelfServiceCardRetailTypes = { "租赁卡类", "养护卡类" };

        // 「顾客自助买卡、但还没付款」的订单：员工用的零售订单列表不显示它们。
        // 腾讯小程序安全检测会用固定账号反复走自助购买流程下单、从不付款，每天堆出几单待支付单，把列表淹掉；
        // 真有顾客自助买卡并付了款，那是真实收入，仍然要显示。判断口径偏保守：
        //   · 有 staff_id（店员开的单）→ 显示；
        //   · 有效明细为空，或含卡类以外的商品 → 显示；
        //   · 有任何一条支付成功的支付记录，或订单流水状态已是「已支付」→ 显示。
        public static bool IsUnpaidSelfServiceCardOrder(Order order)
        {
            if (order.staff_id != null) return false;
            List<Retail> items = (order.retails ?? new List<Retail>()).Where(r => r.valid == 1).ToList();
            if (items.Count == 0) return false;
            if (!items.All(r => r.retail_type != null && SelfServiceCardRetailTypes.Contains(r.retail_type.Trim()))) return false;
            if (string.Equals(order.pay_flow_status?.Trim(), "已支付", StringComparison.Ordinal)) return false;
            if ((order.payments ?? new List<OrderPayment>()).Any(p => p.status != null && p.status.Trim() == "支付成功")) return false;
            return true;
        }

        public static List<Order> ExcludeUnpaidSelfServiceCardOrders(IEnumerable<Order> orders)
        {
            return orders.Where(order => !IsUnpaidSelfServiceCardOrder(order)).ToList();
        }
    }
}
