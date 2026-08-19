using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 养护服务费的「服务组合 → 商品」映射。
    ///
    /// **判定依据是 care 表，不是商品表**：care 上的 need_edge / need_wax / free_wax / need_unwax
    /// 才是顾客到底要做什么的真相；商品表只是给常用固定组合配了个价，不是所有服务都按商品收费。
    ///
    /// 计费口径（用户 2026-08-19 拍板，与生产库近两个雪季实付价格逐项吻合）：
    ///   need_edge  修刃    —— 计一项
    ///   need_wax   热打蜡  —— 计一项
    ///   free_wax   机打蜡  —— **不计费**（生产：单机蜡 11 单全是 0 元；
    ///                          「修刃+热蜡+机蜡」344 单实付 230，与不带机蜡的 1528 单同价）
    ///   need_unwax 刮蜡    —— **不计费**（生产：单刮蜡 6 单全是 0 元）
    ///
    /// 2026-08-19 之前这套映射是靠商品名关键词猜的（IndexOf("修刃打蜡") / IndexOf("立等") 一长串）。
    /// 商品目录改名成 双项/单项/双项加急/单项加急 后那些关键词一个都匹配不上，
    /// 会让所有非次卡养护单的服务费静默变成 0 —— 这个类就是来替掉它的。
    /// </summary>
    public static class CarePricingRules
    {
        public const string SummerBizType = "非雪季养护";
        public const string SummerProductName = "非雪季养护";
        /// <summary>直接寄存：这次做的就是"非雪季养护"整包，没有单项服务。</summary>
        public const string SummerNow = "now";
        /// <summary>先双项、晚点寄存：这次做的是双项，寄存留到下次凭券来。</summary>
        public const string SummerLater = "later";

        private const string TwoItems = "双项";
        private const string OneItem = "单项";
        private const string UrgentSuffix = "加急";

        /// <summary>
        /// 表里没配规则时的兜底：2026-08-18 之前写死在 CareController.CalcCharge 里的那套。
        ///
        /// 当时只有一条——老顾客优惠券（模板 16）双项减 30、单项减 20，且**不看门店**。
        /// 现在这套优惠已经进了 product_ticket_template，正常走表；
        /// 但万一某个门店的商品漏配规则，回退到这里而不是一分不减，
        /// 免得顾客拿着券却发现没优惠。
        ///
        /// 减免不超过原价（原价 0 的项不倒找钱）。
        /// </summary>
        public static double LegacyTicketDiscount(int templateId, Care care, double basePrice)
        {
            if (templateId != LegacyDiscountTemplateId)
            {
                return 0;
            }
            int items = CountChargeableItems(care);
            double discount = items >= 2 ? 30 : (items == 1 ? 20 : 0);
            return Math.Min(discount, Math.Max(basePrice, 0));
        }

        /// <summary>老顾客优惠券。唯一一个曾经写死在代码里的券优惠。</summary>
        public const int LegacyDiscountTemplateId = 16;

        /// <summary>计费项数：修刃 + 热打蜡，各算一项；机打蜡和刮蜡不计。</summary>
        public static int CountChargeableItems(Care care)
        {
            if (care == null)
            {
                return 0;
            }
            return (care.need_edge == 1 ? 1 : 0) + (care.need_wax == 1 ? 1 : 0);
        }

        /// <summary>
        /// 该 care 应当对应的商品名。返回空串 = 没有计费项，不收服务费。
        /// 商品名是目前唯一能标识"这是双项还是单项"的字段（care_project_count 在养护服务商品上全是 NULL）。
        /// </summary>
        public static string ResolveProductName(Care care)
        {
            if (care == null)
            {
                return "";
            }
            // 非雪季两种走法（CareController.ApplyDefaultServices 里由券模板决定）：
            //   券17 非雪季养护券 → summer = "now"，服务项全清零 → 对应商品「非雪季养护」
            //   券18 非雪季赠双项 → summer = "later"，打开修刃+热蜡 → 就是普通「双项」
            // 所以 later 不能也返回「非雪季养护」，它得落回下面的按项数逻辑。
            string summer = (care.summer ?? "").Trim();
            if (summer != SummerLater)
            {
                bool isSummerPackage = summer == SummerNow
                    || (care.biz_type != null && care.biz_type.Trim() == SummerBizType
                        && CountChargeableItems(care) == 0);
                if (isSummerPackage)
                {
                    return SummerProductName;
                }
            }
            int items = CountChargeableItems(care);
            if (items <= 0)
            {
                // 只做机打蜡或只刮蜡：有服务但不计费，没有对应商品
                return "";
            }
            string baseName = items >= 2 ? TwoItems : OneItem;
            return care.urgent == 1 ? baseName + UrgentSuffix : baseName;
        }

        /// <summary>
        /// 在该门店的在售商品里挑出对应的那个。找不到返回 null（上层按 0 元处理）。
        ///
        /// 加急单在没配加急商品的店（南山、崇礼旗舰店只有双项/单项）**回退到非加急价**，
        /// 而不是判定为无商品——后者会让这些店的加急单静默免费。
        /// </summary>
        public static Product MatchProduct(IEnumerable<Product> products, Care care)
        {
            string name = ResolveProductName(care);
            if (products == null || name == "")
            {
                return null;
            }
            List<Product> list = products.Where(p => p != null && p.name != null).ToList();
            Product hit = list.FirstOrDefault(p => p.name.Trim() == name);
            if (hit != null)
            {
                return hit;
            }
            // 回退：把"加急"后缀去掉再找一次
            if (name.EndsWith(UrgentSuffix))
            {
                string fallback = name.Substring(0, name.Length - UrgentSuffix.Length);
                return list.FirstOrDefault(p => p.name.Trim() == fallback);
            }
            return null;
        }
    }
}
