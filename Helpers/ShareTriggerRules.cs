namespace SnowmeetApi.Helpers
{
    // 有效订单可申请分销商分账；具体渠道资格另由 ShareWorkflowRules 根据支付记录判断。
    public static class ShareTriggerRules
    {
        /// <returns>null 表示可以设置；否则是不能设置的原因（直接显示给店长）。</returns>
        public static string? ReasonCannotSet(Models.Order order)
        {
            if (order.valid != 1 || order.hide) return "该订单已失效或隐藏，不能设置分销商分账";
            return null;
        }
    }
}
