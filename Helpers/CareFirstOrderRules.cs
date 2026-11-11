using System;

namespace SnowmeetApi.Helpers
{
    /// <summary>一个订单的时间点：订单 id + 开单时间。</summary>
    public record FirstOrderPoint(int OrderId, DateTime BizDate);

    /// <summary>member 上记着的那张首单订单，现在在订单表里是什么状态。</summary>
    public enum StoredOrderState
    {
        /// <summary>还在订单表里，仍属于这个会员，仍是有效养护订单。</summary>
        Valid,
        /// <summary>还在订单表里，但已不是（作废 / 变测试单 / 装备全删 / 换了会员）。</summary>
        Invalid,
        /// <summary>订单表里已经没有这一行（被归档 / 删除）。</summary>
        Missing
    }

    public enum FirstOrderAction { Keep, Upsert, Delete }

    public record FirstOrderDecision(FirstOrderAction Action, int OrderId = 0, DateTime BizDate = default);

    /// <summary>
    /// 养护首单记录的维护规则（纯函数）：已存的记录 vs 订单表里现算出的该会员最早一张有效养护订单，该怎么处理。
    ///   · 还没有记录：有最早有效单就记下，没有就什么也不做；
    ///   · 记录的订单仍有效：保留；除非现算出更早的有效单（补录 / 补装备后更早的那张），换成更早的；
    ///   · 记录的订单已失效（订单还在，但作废了）：「新」顺延给现算出的最早有效单，一张也没有就删记录；
    ///   · 记录的订单行已不在订单表里：那是「过去发生过」的事实，保留，不被现算结果改写
    ///     （只有现算出比它更早的有效单时才说明记录本身错了，才换）。
    /// 「更早」= 开单时间更早，时间相同取订单 id 小的。
    /// </summary>
    public static class CareFirstOrderRules
    {
        public static bool IsEarlier(FirstOrderPoint a, FirstOrderPoint b)
        {
            return a.BizDate < b.BizDate || (a.BizDate == b.BizDate && a.OrderId < b.OrderId);
        }

        public static FirstOrderDecision Resolve(FirstOrderPoint? stored, StoredOrderState storedState, FirstOrderPoint? earliestValid)
        {
            FirstOrderDecision Keep() => new(FirstOrderAction.Keep);
            FirstOrderDecision Upsert(FirstOrderPoint p) => new(FirstOrderAction.Upsert, p.OrderId, p.BizDate);

            if (stored == null) return earliestValid == null ? Keep() : Upsert(earliestValid);
            if (storedState == StoredOrderState.Invalid) return earliestValid == null ? new(FirstOrderAction.Delete) : Upsert(earliestValid);
            // Valid / Missing：只有现算出更早的有效单才换
            return earliestValid != null && IsEarlier(earliestValid, stored) ? Upsert(earliestValid) : Keep();
        }
    }
}
