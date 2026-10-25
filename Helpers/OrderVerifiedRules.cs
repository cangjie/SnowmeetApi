using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 订单「已核验本人」标识位 order.wechat_unverified 的判定（1 = 已核验为订单会员本人，命名与字面相反，历史遗留）。
    // 为 1 时，养护 0 元核销、储值支付、支付宝付款、租赁储值付租金和次卡都不再要求顾客扫「微信身份核验码」。
    // 置 1 的来源：
    //   1. 开单前顾客在接待页扫码报到并验证了身份——订单一生成就是 1（CheckinVouches / ResolveOnSave）；
    //   2. 顾客扫「微信身份核验码」（PaymentIdentity/VerifyWechatIdentity）；
    //   3. 订单会员本人用微信付款成功（ResolveOnPaid）；
    //   4. 店员从接待页输入手机号，完成店长授权（含当天授权复用）后进入接待。
    // 这里只放不碰数据库的判定，便于单测。
    public static class OrderVerifiedRules
    {
        public const string LOG_SCENE = "开单";

        // 同一店员、同一订单会员：本人扫码沿用 60 分钟；手机号进入须为当天已授权、未作废的接待记录。
        public static bool CheckinVouches(ScanQrCode? sq, int staffId, int? orderMemberId, DateTime now)
        {
            if (sq == null || !ReceptCheckinRules.IsCheckinCode(sq.code) || sq.staff_id != staffId
                || orderMemberId == null || sq.scaner_member_id != orderMemberId) return false;
            if (ReceptCheckinRules.IsVerified(sq))
                return sq.scan_time != null
                    && now < sq.scan_time.Value.AddMinutes(ReceptCheckinRules.CUSTOMER_WAIT_MINUTES);
            return ReceptCheckinRules.IsPhoneEntry(sq) && sq.authed == 1 && sq.stoped == 0
                && sq.create_date.Date == now.Date && sq.create_date <= now;
        }

        // 后端自己找报到：在这位店员最近出的码里，挑能担保这张单的最近一次报到；没有返回 null。
        // 不需要前端带报到码，所以从手动录入页、找回中断单等入口开的单，顾客刚扫码报到过也算。
        public static ScanQrCode? PickVouchingCheckin(IEnumerable<ScanQrCode> candidates, int staffId,
            int? orderMemberId, DateTime now)
        {
            return candidates
                .Where(sq => CheckinVouches(sq, staffId, orderMemberId, now))
                .OrderByDescending(sq => ReceptCheckinRules.IsPhoneEntry(sq) ? sq.update_date ?? sq.create_date : sq.scan_time)
                .FirstOrDefault();
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

        // 养护单是否用了会员权益：券、会员卡（次卡 / 季卡）、储值支付。店员手动减免不算
        public static bool CareUsesBenefit(bool payWithDeposit, IEnumerable<(string? ticketCode, bool useCard)> cares)
        {
            return payWithDeposit
                || cares.Any(c => c.useCard || !string.IsNullOrWhiteSpace(c.ticketCode));
        }

        // 2026-10-24 用户确认：养护单只有「用了会员权益、且还没核验本人」时，才必须由会员本人微信付款。
        // 已核验本人（扫码报到 / 手机号授权 / 扫过核验码）或没用权益的单，允许他人代付，订单仍归原会员。
        public static bool CareRequiresMemberPayer(bool usesBenefit, bool verified)
        {
            return usesBenefit && !verified;
        }

        // 养护单付款后不改订单会员：代付时会员的券、卡、季卡开卡和养护完成赠券都还归原会员。
        // 只有原来没挂会员的养护单，才按付款人补上会员。
        public static bool KeepOrderMemberOnPaid(string? orderType, int? orderMemberId)
        {
            return orderType != null && orderType.Trim() == "养护" && orderMemberId != null;
        }

        // 开单时取值变化的日志
        public static CoreDataModLog ChangeLog(int orderId, bool verified, int staffId, int? checkinId)
        {
            string memo = verified
                ? "开单前接待已验证本人（接待记录 " + checkinId + "，顾客扫码或手机号授权）"
                : "订单会员已更换，取消已核验本人";
            return CoreDataModLog.CreateManualLog("Order", "wechat_unverified", orderId, LOG_SCENE,
                null, staffId, verified ? "0" : "1", verified ? "1" : "0", memo);
        }
    }
}
