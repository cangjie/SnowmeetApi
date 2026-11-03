namespace SnowmeetApi.Helpers
{
    // 什么时候可以在订单上设置介绍人分账。第一期只有万龙体验中心的租赁单，关单后；养护（发板后）、零售（收款后）在第二期。
    public static class ShareTriggerRules
    {
        /// <returns>null 表示可以设置；否则是不能设置的原因（直接显示给店长）。</returns>
        public static string? ReasonCannotSet(Models.Order order)
        {
            if (order.type == "租赁" && order.shop == "万龙体验中心")
            {
                if (order.hide) return "该订单已隐藏，不能设置介绍人分账";
                if (order.closed != 1 || order.close_date == null) return "该订单还没有关单";
                return null;
            }
            return "该类订单暂不支持设置介绍人分账";
        }
    }
}
