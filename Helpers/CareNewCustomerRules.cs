using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 养护订单列表的「新」标识（取代原来的「会 / 散」）：顾客第一次下「有效」的养护订单，这一单标「新」。
    //   · 有效养护订单 = order.valid=1、不是测试单（is_test=0）、至少有一件有效装备
    //     （没装备的是「临时订单」，装备被删的也不算）。这个口径只在 ValidCareOrderExpression 写一遍，
    //     数据库查询直接用它，内存里判断（IsValidCareOrder）也是它编译出来的，两边不会不一致；
    //   · 顾客按会员 id 认；订单没有会员的认不出顾客，不标；
    //   · 第一单按开单时间 biz_date 取最早，时间相同取 id 小的；
    //   · 「谁是第一单」不现算，读 member 表上记着的养护首单（care_first_order_id，CareFirstOrderService）：列表、报表都读那一份，
    //     订单表里的旧单被归档 / 删除后「新」也不会漂移。
    public static class CareNewCustomerRules
    {
        public static readonly Expression<Func<Order, bool>> ValidCareOrderExpression =
            o => o.type == "养护" && o.valid == 1 && o.is_test == 0 && o.cares.Any(c => c.valid == 1);

        private static readonly Func<Order, bool> ValidCareOrder = ValidCareOrderExpression.Compile();

        public static bool IsValidCareOrder(Order order)
        {
            return order.cares != null && ValidCareOrder(order);
        }

        // 给列表里的订单打「新」标：自己是有效养护订单、有会员、并且就是该会员的第一张有效订单
        public static void Mark(IEnumerable<Order> pageOrders, IReadOnlyDictionary<int, int> firstOrderIdByMember)
        {
            foreach (Order order in pageOrders)
            {
                order.isNewCareCustomer = order.member_id != null
                    && IsValidCareOrder(order)
                    && firstOrderIdByMember.TryGetValue((int)order.member_id, out int firstId)
                    && firstId == order.id;
            }
        }
    }
}
