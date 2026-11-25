using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 万龙体验中心、万龙服务中心租赁订单分给万龙的金额：应分 = 各租赁「租金 − 减免」合计 ÷ 2。
    // 2026-10-31 用户确认：以租金明细为准，绝不用「收款 − 退款」代替租金；用了优惠券按券后分。
    // 2026-11-17 用户确认：万龙服务中心开的租赁订单也要给万龙分账，规则和万龙体验中心一样（分租金的一半）。
    public static class WanlongShareRules
    {
        // 一笔付款能拿来算分账上限（30%）的金额，两个平台口径不同：
        // 微信按「付款 − 已退款」的净额（超过会被拒：分账金额超出最大分账比例，11-01 有 8 笔）；
        // 支付宝按付款总额，退款不让额度变小（实测付 6000 退 5140 后可分额度仍是 1800；
        // 上季 297 笔、11-01 的 00013 都是净额的 50% 且成功）。2026-11-02 用户确认比例都是 30%。
        public static double ShareBase(OrderPayment payment)
        {
            return WechatCapOnNet && payment.pay_method != null && payment.pay_method.Trim() == "微信支付"
                ? payment.unRefundedAmount : payment.amount;
        }

        /// <summary>微信的 30% 是否按扣退款后的净额算。11-02 实测微信按净额；规则变回去时只改这一个值。</summary>
        public static readonly bool WechatCapOnNet = true;

        /// <summary>万龙租赁分账的收款方（order_share_relation.id，「万龙租赁分账」）。</summary>
        public const int WanlongRelationId = 4;

        /// <summary>
        /// 要给万龙分账的门店：万龙体验中心、万龙服务中心（2026-11-17 用户定，服务中心的租赁订单也分租金的一半）。
        /// 只用于租赁订单；其它门店（崇礼旗舰店等）不分。
        /// </summary>
        public static bool IsWanlongShop(string? shop)
        {
            string s = (shop ?? "").Trim();
            return s == "万龙体验中心" || s == "万龙服务中心";
        }

        /// <summary>万龙租赁老商户 1604236346 在 wepay_key 里的 id。2026-11-22 起新订单走新商户（WanlongRentalNewMchId），老商户保留给之前的付款记录（退款仍走它）。</summary>
        public const int WanlongRentalMchId = 5;

        /// <summary>
        /// 万龙租赁新商户 1006012310（微信支付公钥模式）在 wepay_key 里的 id（2026-11-22 用户要求万龙租赁换微信支付账号，建行得到 1018）。
        /// 11-22 曾定新账号的租赁订单同样是非分账订单；2026-11-25 用户改了：新账号的所有付款反过来都是分账订单（见 FreezeAllPayments）。
        /// 2026-11-22 起体验中心、服务中心的万龙租赁订单一律走它（TenpayController.GetMchId）；老账号 id 5 保留，已付款订单的退款仍走老账号。
        /// </summary>
        public const int WanlongRentalNewMchId = 1018;

        /// <summary>
        /// 2026-11-17 用户定：万龙租赁商户（1604236346）下的租赁订单，从现在起一律是「非分账订单」——
        /// 下微信单时不带 profit_sharing 标识，付款记录 need_share = 0，资金不冻结。
        /// 原因：微信分账的最大可分账金额按「付款 − 退款」的净额算（11-01 起实测），而万龙租赁应分是租金的一半、押金退完后占净额 50%，几乎都超限被拒。
        /// </summary>
        public static bool IsNoShareOrder(string? orderType, int? mchId)
            => (orderType ?? "").Trim() == "租赁" && mchId == WanlongRentalMchId;

        /// <summary>
        /// 2026-11-25 用户定：为防止新微信支付账号 1006012310（wepay_key 1018）自动提现，凡是支付给它的（不分订单类型）一律是分账订单——
        /// 下微信单带 profit_sharing，付款记录 need_share = 1，资金冻结在「待分账」里，日后由人手动解冻。
        /// 所以这类付款：下单时一律带分账标识；夜间「关闭微信分账」任务不解冻（见 OrderShareController.ClosableWechatPaymentIdsAsync）。
        /// 微信对直连商户的冻结期最长 30 天，到期会自动解冻，手动解冻要赶在这之前。
        /// 这条取代 11-22 那条「新账号同样是非分账订单」。
        /// </summary>
        public static bool FreezeAllPayments(int? mchId) => mchId == WanlongRentalNewMchId;

        /// <summary>
        /// 下微信单时最终带给微信的 profit_sharing 标识：老商户 5 的租赁订单不带（11-17）；新商户 1018 的所有付款都带（11-25）；
        /// 其它商户照调用方（雪票有分账的才带）。
        /// </summary>
        public static bool ResolveProfitSharing(string? orderType, int? mchId, bool requested)
            => FreezeAllPayments(mchId) || (!IsNoShareOrder(orderType, mchId) && requested);

        /// <summary>
        /// 2026-11-17 用户定：所有给万龙生成的分账一律标成「不自动分账」（payment_share.can_not_share = 1），夜间任务不提交，应分的钱线下结算。
        /// </summary>
        public const string NoAutoMemo = "万龙租赁分账一律不自动分账（11-17 起，微信按净额限 30%）";

        /// <summary>
        /// 夜间「关闭微信分账」（解冻剩余资金）不用等分账成功后的等待期（ShareWorkflowRules.WechatUnfreezeWaitDays，11-18 起 1 天）、可以立刻解冻的付款：万龙租赁商户（1604236346）下的租赁订单
        /// （2026-11-17 用户定「只解冻万龙租赁商户」，万龙租赁不再走微信分账）。
        /// 雪票等其它类型还有 1 元分账等着提交，旗舰租赁等其它商户的老付款也不动，都仍按原规则。
        /// </summary>
        public static bool UnfreezeImmediately(string? orderType, int? mchId) => IsNoShareOrder(orderType, mchId);

        /// <summary>
        /// 解冻时微信回的错误说明表示这笔交易在微信侧已经不是冻结状态：「订单已完结分账，不支持分账」（已经完结）、
        /// 「订单已逾期解冻，不支持分账」（超过了可解冻的期限）。11-17 万龙租赁 189 笔里有 167 笔是这两种。
        /// 这种情况记为已完结（写 share_close_date），夜间任务不再每晚重试。
        /// </summary>
        public static bool IsAlreadyClosedAtWechat(string? errorMessage)
        {
            string m = errorMessage ?? "";
            return m.Contains("订单已完结分账") || m.Contains("订单已逾期解冻");
        }

        // 一个租赁计入分账基数的租金：有效「租金」明细合计，减去挂在这些租金明细上的减免，
        // 再减去不挂明细的减免（优惠券、整单减免），每笔减免只扣一次。
        // 超时费、赔偿金不是租金，挂在它们上面的减免也不扣；已免除那天（明细 valid=0）的减免不扣；
        // 招待、体验租赁不计入。
        public static double RentAfterDiscounts(Rental rental)
        {
            if (rental.entertain || rental.experience)
            {
                return 0;
            }
            List<RentalDetail> rentDetails = (rental.details ?? new List<RentalDetail>())
                .Where(d => d.valid == 1 && d.charge_type.Trim().Equals("租金")).ToList();
            HashSet<int> rentDetailIds = rentDetails.Select(d => d.id).ToHashSet();
            double discount = rental.availableDiscounts
                .Where(d => d.sub_biz_id == null || rentDetailIds.Contains((int)d.sub_biz_id))
                .Sum(d => d.amount);
            return rentDetails.Sum(d => d.amount) - discount;
        }
    }
}
