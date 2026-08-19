using System;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>养护商品在维护页上的状态标签。</summary>
    public class CareProductStateView
    {
        public string Label { get; set; } = "";
        public string Cls { get; set; } = "";
    }

    /// <summary>
    /// 养护商品（product.category_id = 14）维护的纯规则。
    ///
    /// 这批商品是养护定价的价目表：CareController.CalcCharge 按门店 + 服务组合
    /// 从里面取 sale_price（见 CarePricingRules）。改这里的价 = 改线上收费，
    /// 所以校验和状态口径收在这个纯函数文件里，由 CareProductRulesTests 锁死。
    /// </summary>
    public static class CareProductRules
    {
        /// <summary>养护服务商品固定挂在这个分类下。</summary>
        public const int CareCategoryId = 14;

        /// <summary>
        /// 维护页的状态。优先级：已停用 &gt; 已隐藏 &gt; 在售。
        /// valid 与 hidden 是两件事：valid=0 是这条记录作废，hidden=1 只是不在顾客端露出
        /// （养护服务本来大多不在商城卖，hidden 才是控制它能不能被券规则选到的那个开关）。
        /// </summary>
        public static CareProductStateView DescribeState(Product p)
        {
            if (p == null)
            {
                return new CareProductStateView() { Label = "", Cls = "" };
            }
            if (p.valid != 1)
            {
                return new CareProductStateView() { Label = "已停用", Cls = "invalid" };
            }
            if (p.hidden == 1)
            {
                return new CareProductStateView() { Label = "已隐藏", Cls = "hidden" };
            }
            return new CareProductStateView() { Label = "在售", Cls = "live" };
        }

        /// <summary>
        /// 保存前校验。返回 null 表示通过，否则是给店员看的第一条错误。
        ///
        /// 价格允许为 0（「免费打蜡升级」这类确实存在 0 元项），但不允许为负，
        /// 也不允许为空——空价会让 CalcCharge 算出 0，是静默免费。
        /// </summary>
        public static string Validate(string name, int? shopId, double? salePrice)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "请填写商品名称";
            }
            if (shopId == null || shopId <= 0)
            {
                return "请选择门店";
            }
            if (salePrice == null)
            {
                return "请填写价格";
            }
            if (salePrice < 0)
            {
                return "价格不能为负";
            }
            return null;
        }

        /// <summary>
        /// 商品名是否是养护定价能识别的那几个。
        /// CarePricingRules 按 care 的服务组合算出「双项/单项/双项加急/单项加急」再按名字取价，
        /// 名字对不上就取不到价、服务费静默变成 0 —— 2026-08-19 商品改名就踩过这个坑。
        /// 维护页据此给非标准名打提示，但**不拦截**：非雪季养护、免费打蜡升级这类
        /// 走的是别的路径，本来就不该受这套命名约束。
        /// </summary>
        public static bool IsPricingRecognizedName(string name)
        {
            string n = (name ?? "").Trim();
            return n == "双项" || n == "单项" || n == "双项加急" || n == "单项加急";
        }
    }
}
