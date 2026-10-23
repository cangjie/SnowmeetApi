namespace SnowmeetApi.Helpers
{
    // 店员手工确认收款（其他支付：现金 / 刷卡等）可用的支付方式规则。
    public static class PayMethodRules
    {
        public const string PERSONAL_WECHAT_TRANSFER_SUFFIX = "微信转账";

        // 2026-10-23 用户要求：暂时不允许个人微信转账收款。以「微信转账」结尾的支付方式
        // （苍杰微信转账、王政微信转账、肖志强工作微信转账、微信转账……）不出现在可选列表里，
        // 手工填写这类支付方式确认收款也会被拒绝。历史收款记录不受影响。
        public static bool IsPersonalWechatTransfer(string? payMethod)
        {
            return !string.IsNullOrWhiteSpace(payMethod)
                && payMethod.Trim().EndsWith(PERSONAL_WECHAT_TRANSFER_SUFFIX);
        }
    }
}
