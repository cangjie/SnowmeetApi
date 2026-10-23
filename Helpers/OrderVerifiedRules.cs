using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 订单「已核验本人」标识位 order.wechat_unverified 的判定（1 = 已核验为订单会员本人，命名与字面相反，历史遗留）。
    // 为 1 时，养护 0 元核销、储值支付、支付宝付款、租赁储值付租金和次卡都不再要求顾客扫「微信身份核验码」。
    // 置 1 的来源有三个：
    //   1. 开单前顾客在接待页扫码报到并验证了身份——订单一生成就是 1（CheckinVouches / ResolveOnSave）；
    //   2. 顾客扫「微信身份核验码」（PaymentIdentity/VerifyWechatIdentity）；
    //   3. 订单会员本人用微信付款成功（ResolveOnPaid）。
    // 这里只放不碰数据库的判定，便于单测。
    public static class OrderVerifiedRules
    {
        public const string LOG_SCENE = "开单";

        // 扫码报到能否给这张单担保：顾客本人扫码、手机号已验证（店员输手机号进入、散客都不算），
        // 是这位店员出的码，报到会员就是订单会员，且在报到后 60 分钟内（与顾客等待页同一时限）。
        public static bool CheckinVouches(ScanQrCode? sq, int staffId, int? orderMemberId, DateTime now)
        {
            return sq != null
                && ReceptCheckinRules.IsCheckinCode(sq.code)
                && ReceptCheckinRules.IsVerified(sq)
                && sq.staff_id == staffId
                && orderMemberId != null && sq.scaner_member_id == orderMemberId
                && sq.scan_time != null
                && now < sq.scan_time.Value.AddMinutes(ReceptCheckinRules.CUSTOMER_WAIT_MINUTES);
        }

        // 开单草稿每次保存时的取值。不采信前端传来的值：草稿接口把整单原样写库，前端可能带着上一单的值。
        // 报到能担保 → 1；否则保留库里已有的 1，前提是订单会员没换（换了人就不再是那位本人）；其余 0。
        public static bool ResolveOnSave(bool checkinVouches, bool savedVerified, int? savedMemberId, int? newMemberId)
        {
            if (checkinVouches)
            {
                return true;
            }
            return savedVerified && savedMemberId != null && savedMemberId == newMemberId;
        }

        // 支付成功时的取值（memberAfter 是按本次付款同步后的订单会员）：
        // 订单会员本人用微信付款 → 1；此前已核验、且这次付款没有改订单会员（本人用支付宝、他人代付）→ 保持 1；其余 0。
        public static bool ResolveOnPaid(bool verifiedBefore, int? memberBefore, int? memberAfter,
            string? payMethod, bool isProxyPay, int? payerMemberId)
        {
            bool selfWechatPay = payMethod != null && payMethod.Trim() == "微信支付"
                && !isProxyPay && payerMemberId != null && payerMemberId == memberAfter;
            if (selfWechatPay)
            {
                return true;
            }
            return verifiedBefore && memberBefore != null && memberBefore == memberAfter;
        }

        // 开单时取值变化的日志
        public static CoreDataModLog ChangeLog(int orderId, bool verified, int staffId, int? checkinId)
        {
            string memo = verified
                ? "开单前扫码报到已验证本人（扫码记录 " + checkinId + "）"
                : "订单会员已更换，取消已核验本人";
            return CoreDataModLog.CreateManualLog("Order", "wechat_unverified", orderId, LOG_SCENE,
                null, staffId, verified ? "0" : "1", verified ? "1" : "0", memo);
        }
    }
}
