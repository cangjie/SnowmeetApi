using System;
using System.Linq.Expressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 退款行里哪些算「已申请」：用于退款前按「已申请」额度封顶，防止重复退。
    //   state = 1：已成功（现金等不走渠道的退款也是直接 1）。
    //   state = 0 且有渠道退款单号：已提交到微信/支付宝、处理中，必须继续占额度。
    //   state = 0 且没有退款单号：渠道拒绝或根本没发出去（如微信「基本账户余额不足，请充值后重新发起」），钱从未动过，
    //     不占额度——否则这一行会把重试拦死，页面只提示「退款金额已变化，请刷新后重试」，刷新也没用。
    // 与 OrderPayment.refundedAmount 的口径一致。
    public static class RefundRequestRules
    {
        public static readonly Expression<Func<OrderPaymentRefund, bool>> CountsAsRequested =
            r => r.state == 1 || (r.refund_id != null && r.refund_id != "");
    }
}
