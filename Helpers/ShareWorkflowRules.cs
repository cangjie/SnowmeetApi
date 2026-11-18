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

    /// <summary>
    /// 微信分账成功（或银行转账记账）之后，等几个日历日再由夜间「关闭微信分账」任务（23:15）解冻剩余资金。
    /// 2026-11-18 用户定：由 4 天缩短为 1 天多——分账成功的第二天夜里就解冻。要再改，只改这一个值。
    /// </summary>
    public const int WechatUnfreezeWaitDays = 1;

    /// <summary>
    /// 最近一次分账成功（或银行转账记账）的时间是否已经过了等待期。按日期比、不按小时：
    /// 11-17 任何时刻分账成功，11-18 夜里的任务就算到期；11-18 当天成功的，要等 11-19 夜里。
    /// </summary>
    public static bool UnfreezeWaitOver(DateTime lastShareTime, DateTime now)
        => lastShareTime.Date <= now.Date.AddDays(-WechatUnfreezeWaitDays);

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
