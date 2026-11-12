using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers;

/// <summary>分销商分账方式和可用支付渠道；渠道是否最终接受分账由支付平台决定。</summary>
public static class ShareWorkflowRules
{
    public const string Auto = "auto";
    public const string Manual = "manual";
    public const string Bank = "bank";

    public static bool IsValidMethod(string? method) => method is Auto or Manual or Bank;

    public static bool CanUsePayment(Models.Order order, OrderPayment payment)
    {
        if (order.valid != 1 || order.hide || payment.valid != 1 || payment.status != "支付成功"
            || payment.unRefundedAmount <= 0 || string.IsNullOrWhiteSpace(payment.out_trade_no)) return false;
        return (payment.pay_method ?? "").Trim() switch
        {
            "微信支付" => payment.need_share == 1 && payment.share_close_date == null
                && payment.mch_id != null && !string.IsNullOrWhiteSpace(payment.wepay_trans_id),
            // 用户确认：所有有效支付宝付款都可以尝试渠道分账。
            "支付宝" => !string.IsNullOrWhiteSpace(payment.ali_trade_no),
            _ => false,
        };
    }

    public static bool CanUseChannel(Models.Order order, IEnumerable<OrderPayment> payments)
        => payments.Any(p => CanUsePayment(order, p));

    public static string Method(OrderShare share, ShareFacts facts, bool systemTicket = false)
    {
        if (share.batch_id != null) return Bank;
        if (IsValidMethod(share.share_method)) return share.share_method!;
        if (systemTicket) return Auto;
        return ShareListRules.Mode(facts) == "手动" ? Bank : Auto;
    }
}
