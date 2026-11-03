using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 万龙体验中心租赁订单分给万龙的金额：应分 = 各租赁「租金 − 减免」合计 ÷ 2。
    // 2026-10-31 用户确认：以租金明细为准，绝不用「收款 − 退款」代替租金；用了优惠券按券后分。
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
