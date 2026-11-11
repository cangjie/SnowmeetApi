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
        // 万龙的养护统一由服务中心做：体验中心可以接单（2026-11-09 起 shop_list.care=1，接待页按这个标志放行），
        // 订单店名保持开单门店（体验中心的单记体验中心、单号 WT_YH_…），但价目 / 单刮蜡一口价 / 开板刮蜡券 / 教练卡
        // 都按服务中心算（本方法），收款进服务中心的养护商户（TenpayController.GetMchId 的 3 号）。
        // 只有旧版 ReceptController（旧客户端）还会在下单时把店名也改成服务中心。
        public static string ResolveCareShop(string? shop)
        {
            string name = (shop ?? "").Trim();
            return name == "万龙体验中心" ? "万龙服务中心" : name;
        }

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

        /// <summary>
        /// 万龙服务中心「只选了刮蜡」的一口价（用户 2026-10-30 提出）：
        /// 会员之前有过消费记录（老会员，口径见 MemberConsumption）80 元，否则 100 元。
        /// 刮蜡和别的项目一起选、以及其他门店，都不走这条，保持上面的原有计费。
        /// </summary>
        public const string ScrapeOnlyShop = "万龙服务中心";
        public const double ScrapeOnlyOldMemberPrice = 80;
        public const double ScrapeOnlyNewMemberPrice = 100;

        /// <summary>
        /// care 上的服务只勾了刮蜡：没有修刃、热蜡、机打蜡，也不是非雪季、质保、招待。
        /// 立等、维修等不影响「只刮蜡」的判定；已选的券 / 卡也不看——它们会被 ClearBenefitsForScrapeOnly 摘掉。
        /// </summary>
        public static bool IsScrapeOnly(Care care)
        {
            if (care == null)
            {
                return false;
            }
            bool summer = !string.IsNullOrWhiteSpace(care.summer)
                || (care.biz_type != null && care.biz_type.Trim() == SummerBizType);
            // 养板自带刮蜡（默认带出），它不是「只刮蜡」：养板按自己的价目收，不能落到 80 / 100 的一口价
            return care.need_unwax == 1 && care.need_edge != 1 && care.need_wax != 1 && care.free_wax != 1
                && care.maintain_board != 1 && !summer && !care.warranty && !care.entertain;
        }

        /// <summary>
        /// 养板价目的商品名（用户 2026-11-10）：单选养板「养板」220，修刃+养板「修刃养板」280；
        /// 老顾客券（模板 16）分别减 20 / 30 → 200 / 250。价目只配在万龙服务中心（体验中心同价），
        /// 别的门店没有这两个商品，选养板会报「没有养板价目」而不是静默免费。
        /// </summary>
        public const string MaintainBoardName = "养板";
        public const string EdgeMaintainBoardName = "修刃养板";

        /// <summary>这件 care 是否按养板收费：选了养板，且不是非雪季、质保、招待（它们各有各的算法）。</summary>
        public static bool AppliesMaintainBoardPrice(Care care)
        {
            if (care == null || care.maintain_board != 1)
            {
                return false;
            }
            bool summer = !string.IsNullOrWhiteSpace(care.summer)
                || (care.biz_type != null && care.biz_type.Trim() == SummerBizType);
            return !summer && !care.warranty && !care.entertain;
        }

        /// <summary>
        /// 养板只认老顾客券（模板 16，上面的 200 / 250 就是凭它的价）：其余券 / 会员卡 / 学员卡一律摘掉，
        /// 并把随它们带来的减免一并清零（减免可能来自别的券，区分不了来源）——留着只会在订单生效时白白核销一张券 / 扣一次卡。
        /// ticket 传这件 care 当前所选券码对应的券（没选 / 查不到传 null；查不到的券码也摘掉）。
        /// 返回 true = 确实摘掉了东西（给开单页提示用）；没选任何券卡时不动，店员手填的减免保留。
        /// 老顾客券的减免随后由定价按「保底」补齐，不受这里清零影响。
        /// 开单页预览（CalcCareCharge）和下单（PlaceCareOrder）都要在定价前先调这个。
        /// </summary>
        public static bool ClearBenefitsForMaintainBoard(Care care, Ticket? ticket)
        {
            if (!AppliesMaintainBoardPrice(care))
            {
                return false;
            }
            bool hasTicket = !string.IsNullOrWhiteSpace(care.ticket_code);
            bool dropTicket = hasTicket && (ticket == null || ticket.template_id != LegacyDiscountTemplateId);
            bool dropOthers = care.use_card || care.card_id != null || care.coach_card_id != null;
            if (!dropTicket && !dropOthers)
            {
                return false;
            }
            if (dropTicket)
            {
                care.ticket_code = null;
            }
            care.use_card = false;
            care.card_id = null;
            care.card_name = null;
            care.coach_card_id = null;
            care.discount = 0;
            care.ticket_discount = 0;
            return true;
        }

        /// <summary>
        /// 这件 care 在这家店是否按「单刮蜡一口价」收费。
        /// 门店先走 ResolveCareShop 归并（万龙体验中心 → 万龙服务中心）：订单店名保持开单门店（体验中心的单就是体验中心），
        /// 开单页预览和下单传进来的都可能是体验中心，不归并就会把体验中心的单刮蜡算成免费。
        /// </summary>
        public static bool AppliesScrapeOnlyPrice(string? shop, Care care)
        {
            return ResolveCareShop(shop) == ScrapeOnlyShop && IsScrapeOnly(care);
        }

        /// <summary>
        /// 只选了刮蜡就不该选中任何券、会员卡、学员卡（用户 2026-10-30）：一口价不叠加任何优惠，
        /// 留着只会在订单生效时白白核销一张券 / 扣一次卡。把已选的都摘掉，并把随它们带来的减免一并清零
        /// （减免可能来自折扣券 16，区分不了来源——与开单页换券时清减免同一口径）。
        /// 返回 true = 确实摘掉了东西（给开单页提示用）；没选任何券卡时不动，店员手填的减免保留。
        /// 开单页预览（CalcCareCharge）和下单（PlaceCareOrder）都要在定价前先调这个。
        /// </summary>
        public static bool ClearBenefitsForScrapeOnly(string? shop, Care care, bool keepBoardTicket = false)
        {
            if (!AppliesScrapeOnlyPrice(shop, care))
            {
                return false;
            }
            // keepBoardTicket：这件 care 选的是一张可用的「开板刮蜡券」（封板后发的，专抵这笔一口价），券留着，
            // 其余（会员卡 / 学员卡 / 减免）照旧摘掉
            bool keepTicket = keepBoardTicket && !string.IsNullOrWhiteSpace(care.ticket_code);
            bool hasBenefit = (!keepTicket && !string.IsNullOrWhiteSpace(care.ticket_code)) || care.use_card
                || care.card_id != null || care.coach_card_id != null;
            if (!hasBenefit)
            {
                return false;
            }
            if (!keepTicket)
            {
                care.ticket_code = null;
            }
            care.use_card = false;
            care.card_id = null;
            care.card_name = null;
            care.coach_card_id = null;
            care.discount = 0;
            care.ticket_discount = 0;
            return true;
        }

        public static double ScrapeOnlyPrice(bool isOldMember)
        {
            return isOldMember ? ScrapeOnlyOldMemberPrice : ScrapeOnlyNewMemberPrice;
        }

        /// <summary>
        /// 开板刮蜡券（模板 20，封板取板后发给顾客，见 CareSealRules）。
        /// 它只有一个用处：抵掉万龙服务中心「只选刮蜡」的一口价（老会员 80 / 其他 100），抵完这件装备 0 元。
        /// 其余场合都没有东西可抵——别的门店刮蜡本来就不收费，刮蜡跟修刃 / 热蜡一起选时刮蜡也不单独计费——
        /// 所以那些场合不允许选，免得白白核销一张券。
        /// </summary>
        public const int BoardScrapeTicketTemplateId = CareSealRules.SealTicketTemplateId;

        /// <summary>
        /// 这张券此刻能不能抵这件 care：模板 20、这件 care 正是万龙服务中心的单刮蜡、券有效未用、
        /// 在有效期内，并且属于下单的会员（散客不能用——券是发给会员的）。
        /// 服务端不信前端选的券码：开单页预览和下单两边都要走这个判断。
        /// </summary>
        public static bool CanRedeemBoardTicket(string? shop, Care care, Ticket? ticket, int? memberId, DateTime now)
        {
            return ticket != null && ticket.template_id == BoardScrapeTicketTemplateId
                && AppliesScrapeOnlyPrice(shop, care)
                && ticket.valid == 1 && ticket.used == 0 && ticket.is_active == 1
                && memberId != null && ticket.member_id == memberId
                && (ticket.start_date == null || ticket.start_date.Value.Date <= now.Date)
                && (ticket.expire_date == null || ticket.expire_date.Value.Date >= now.Date);
        }

        /// <summary>
        /// 单刮蜡一口价下的券卡取舍，开单页预览（CalcCareCharge）和下单（PlaceCareOrder）共用，要在定价前调：
        /// ① 选的是可用的开板刮蜡券 → 留下，摘掉其余券卡；
        /// ② 选的是开板刮蜡券但此刻抵不了（别的门店 / 不是单刮蜡 / 券已用、过期、不属于该会员）→ 把这张券摘掉，不核销；
        /// ③ 其余情况 → 与 ClearBenefitsForScrapeOnly 原来的口径完全一致（单刮蜡一口价时摘掉所有券卡）。
        /// ticket 传这件 care 当前所选券码对应的券（没选 / 查不到就传 null）。
        /// </summary>
        public static ScrapeOnlyBenefitResult ApplyScrapeOnlyBenefitRules(string? shop, Care care, Ticket? ticket,
            int? memberId, DateTime now)
        {
            bool keep = CanRedeemBoardTicket(shop, care, ticket, memberId, now);
            bool boardTicketRemoved = false;
            if (ticket != null && ticket.template_id == BoardScrapeTicketTemplateId && !keep
                && !string.IsNullOrWhiteSpace(care.ticket_code))
            {
                care.ticket_code = null;
                boardTicketRemoved = true;
            }
            bool cleared = ClearBenefitsForScrapeOnly(shop, care, keep);
            return new ScrapeOnlyBenefitResult(cleared, boardTicketRemoved, keep);
        }

        public readonly record struct ScrapeOnlyBenefitResult(bool BenefitsCleared, bool BoardTicketRemoved, bool BoardTicketKept);

        /// <summary>计费项数：修刃 + 热打蜡（或养板——二者互斥，养板就是「蜡」那一项），各算一项；机打蜡和刮蜡不计。</summary>
        public static int CountChargeableItems(Care care)
        {
            if (care == null)
            {
                return 0;
            }
            return (care.need_edge == 1 ? 1 : 0) + (care.need_wax == 1 || care.maintain_board == 1 ? 1 : 0);
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
            // 养板有自己的价目，不走单项 / 双项
            string baseName = care.maintain_board == 1
                ? (care.need_edge == 1 ? EdgeMaintainBoardName : MaintainBoardName)
                : (items >= 2 ? TwoItems : OneItem);
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
