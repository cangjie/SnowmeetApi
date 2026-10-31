using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 万龙体验中心租赁订单分给万龙的金额：应分 = 各租赁「租金 − 减免」合计 ÷ 2。
    // 2026-10-31 用户确认：以租金明细为准，绝不用「收款 − 退款」代替租金；用了优惠券按券后分。
    public static class WanlongShareRules
    {
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
