namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 崇礼旗舰店的续租定价（2026-11-22 用户定）：
    ///   发生续租（夜间任务给还在租的订单生成下一天的租金）时，每天的价格都按「多日」价——第一天也改成多日价，之后每天本来就是多日价；
    ///   只对今天及以后跑续租时命中的订单生效：第一次续租那一刻把第一天改成多日价，已经续租过的订单、已经记好的历史行不改。
    /// 第一天只会往低改（多日价比第一天的价还高就不动，不能让顾客多付）；手改过价的租赁仍以店员为准；其它门店不受影响。
    /// 实现在 RentController.ContinueRentalCore / RepriceFirstDayAtMultiDayPriceAsync。
    /// </summary>
    public static class ContinueRentRules
    {
        public const string MultiDayFromFirstDayShop = "崇礼旗舰店";

        /// <summary>系统续租生成的租金行的备注（RentController.ContinueRentalCore 写的）。</summary>
        public const string SystemContinueMemo = "系统续租";

        public static bool UsesMultiDayFromFirstDay(string? shop) => (shop ?? "").Trim() == MultiDayFromFirstDayShop;
    }
}
