using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    // 养护订单列表里每件装备显示的「金额」「减免」（只用于展示，不改库；填到 Care.displayCharge / displayDiscount）：
    //   · 普通装备：金额 = 项目收费 + 附加费；减免 = 手动减免 + 券减免（都是下单时落库的值，与详情页同口径）；
    //   · 招待 / 质保装备：下单时不计价，库里项目收费、减免都是 0。展示时按这件装备所做的项目算标准价
    //     （和开单时的服务费走同一套定价，见 OrderController.FillCareDisplayAmountsAsync）：
    //     金额 = 标准价 + 附加费，减免 = 金额，净额 0——「0 元订单无非是按所做项目计算金额，再减免到 0 元」。
    public static class CareAmountRules
    {
        public static bool IsFreeCare(Care care)
        {
            return care.warranty || care.entertain;
        }

        public static (double charge, double discount) FromStored(Care care)
        {
            return (Cents(care.common_charge + care.repair_charge), Cents(care.discount + care.ticket_discount));
        }

        public static (double charge, double discount) ForFreeCare(Care care, double standardCommonCharge)
        {
            double charge = Cents(standardCommonCharge + care.repair_charge);
            return (charge, charge);
        }

        private static double Cents(double value)
        {
            return Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
