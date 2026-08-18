using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 优惠券**模板**的纯规则。分工：
    /// TicketTransferRules = 券本身（转赠/过期/领取上限/banner），
    /// TicketAdminRules    = 管理后台怎么展示和分页，
    /// 本类                = 模板字段的校验与派生（有效期怎么算、商品优惠怎么落到价格上）。
    ///
    /// 这里的三个字段（available_days / discount_rate / discount_amount）在 2026-08-18 之前
    /// 是「DB 有列、代码零读取」的死字段。接活它们等于改动发券与养护定价两条线上链路，
    /// 所以口径全部收在这个纯函数文件里，由 TicketTemplateRulesTests 逐条锁死。
    /// </summary>
    public static class TicketTemplateRules
    {
        /// <summary>
        /// 业务线取值域。与 [order].type 对齐（生产库该列就是这四个值 + 雪票/聚合），
        /// 因为 biz_type 的作用就是把券路由到对应的开单流程。
        /// </summary>
        public static readonly string[] BizTypeOptions = new string[] { "零售", "养护", "租赁", "餐饮" };

        /// <summary>
        /// 模板字段校验。返回空列表 = 通过。
        /// 前端编辑页用 radio 三选一让互斥在 UI 上不可能违反，这里是服务端的第二道。
        /// </summary>
        public static List<string> ValidateTemplate(TicketTemplate template)
        {
            List<string> errors = new List<string>();
            if (template == null)
            {
                errors.Add("模板为空");
                return errors;
            }
            if (string.IsNullOrWhiteSpace(template.name))
            {
                errors.Add("模板名称不能为空");
            }
            if (string.IsNullOrWhiteSpace(template.type))
            {
                errors.Add("模板类型不能为空");
            }
            if (string.IsNullOrWhiteSpace(template.biz_type))
            {
                errors.Add("请选择业务类型");
            }
            else if (Array.IndexOf(BizTypeOptions, template.biz_type.Trim()) < 0)
            {
                errors.Add("业务类型只能是 " + string.Join("/", BizTypeOptions));
            }
            // 用户口径：两者必须有一个为 null。都为空是合法的，语义是"永久有效"。
            if (template.available_days != null && template.expire_date != null)
            {
                errors.Add("可用天数与总过期日只能设置一个");
            }
            if (template.available_days != null && template.available_days <= 0)
            {
                errors.Add("可用天数必须大于 0；不限期请留空");
            }
            return errors;
        }

        /// <summary>
        /// 一张新券的到期日。startDate 是券的**启用日**。
        ///
        /// 调用方显式传了 expireDate 的场景（转赠回赠按雪季末、雪票取卡按次日）不走这里——
        /// 那些是比模板更强的业务规则，优先级在调用方保留。
        /// </summary>
        public static DateTime ResolveTicketExpireDate(TicketTemplate template, DateTime startDate)
        {
            if (template == null)
            {
                return DateTime.MaxValue;
            }
            if (template.expire_date != null)
            {
                // 存量有 5 个模板两个字段都设了（ValidateTemplate 会拦新的）。固定截止日优先，
                // 因为它是绝对时点，比"启用后 N 天"更接近运营原意。
                return (DateTime)template.expire_date;
            }
            if (template.available_days != null && template.available_days > 0)
            {
                // 抹掉启用时刻的时分秒：同一天领的券必须同一天到期，
                // 否则 0:01 和 23:59 领的券有效期会差出一天。
                return startDate.Date.AddDays((int)template.available_days + 1).AddSeconds(-1);
            }
            return DateTime.MaxValue;
        }

        /// <summary>
        /// 商品优惠规则落到价格上。返回 (服务费, 券减免)。
        /// 优先级 <c>fixed_price</c> &gt; <c>discount_rate</c> &gt; <c>discount_amount</c>，
        /// 三个字段互斥是编辑页的约定，但存量数据有同时设的，所以这里必须定死先后。
        /// </summary>
        public static (double commonCharge, double ticketDiscount) ResolveProductDiscount(
            ProductTicketTemplate rule, double basePrice)
        {
            if (rule == null)
            {
                return (basePrice, 0);
            }
            if (rule.fixed_price != null)
            {
                // 0 是有效的一口价（模板 17/18 非雪季券就是 0），不能当"未设置"
                return ((double)rule.fixed_price, 0);
            }
            if (rule.discount_rate != null)
            {
                double rate = (double)rule.discount_rate;
                // 语义是"打几折的小数"：0.8 = 8 折。0 折请用一口价 0 表达；
                // 越界值一律按不打折处理，宁可少减也不能算出负价。
                if (rate > 0 && rate < 1)
                {
                    // 算的是钱，必须收到分：200 * (1 - 0.8) 的二进制结果是 39.999999999999993
                    return (basePrice, Math.Round(basePrice * (1 - rate), 2, MidpointRounding.AwayFromZero));
                }
                return (basePrice, 0);
            }
            if (rule.discount_amount != null)
            {
                return (basePrice, Math.Min((double)rule.discount_amount, basePrice));
            }
            return (basePrice, 0);
        }

        /// <summary>
        /// 给定商品挑规则。product_id == 0 是通配兜底（沿用 CareController 原有语义），
        /// 精确命中优先于兜底。已软删（valid=false）的规则跳过。
        /// </summary>
        public static ProductTicketTemplate MatchProductRule(
            IEnumerable<ProductTicketTemplate> rules, int productId)
        {
            if (rules == null)
            {
                return null;
            }
            List<ProductTicketTemplate> live = rules.Where(p => p != null && p.valid).ToList();
            return live.Where(p => p.product_id == productId).FirstOrDefault()
                ?? live.Where(p => p.product_id == 0).FirstOrDefault();
        }

        /// <summary>
        /// 列表页的「有效期口径」一句话。WXML 不支持方法调用，这类文案只能服务端派生。
        /// </summary>
        public static string DescribeValidity(TicketTemplate template)
        {
            if (template == null)
            {
                return "";
            }
            if (template.available_days != null && template.expire_date != null)
            {
                return "⚠ 冲突：同时设了天数与截止日";
            }
            if (template.expire_date != null)
            {
                return ((DateTime)template.expire_date).ToString("yyyy-MM-dd") + " 截止";
            }
            if (template.available_days != null)
            {
                return "启用后 " + template.available_days + " 天";
            }
            return "永久有效";
        }
    }
}
