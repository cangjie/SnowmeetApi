using System;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 查询接口的 shop 参数：一个店名，或逗号（, 或 ，）分隔的多个店名（小程序「区域=万龙 + 全部门店」联合查询两家万龙门店）。
    /// 兼容旧客户端：旧客户端只发单个店名或不带，解析成一个元素的列表，等价于原来的 o.shop.Trim().Equals(shop.Trim())；
    /// 不带（null）= 全部门店。店名里没有逗号（shop_list 与历史订单都核对过）。
    /// </summary>
    public static class ShopFilter
    {
        private static readonly char[] Separators = { ',', '，' };

        /// <summary>
        /// null = 不限门店。其余返回去空格、去重后的店名（保持出现顺序）。
        /// 只有一个片段时原样保留（包括 ""：和旧谓词一样一家都匹配不上，不悄悄变成「全部」）；
        /// 有多个片段时丢掉空片段（"a,," 当 "a" 用）。
        /// </summary>
        public static List<string>? Parse(string? shop)
        {
            if (shop == null) return null;
            List<string> parts = shop.Split(Separators).Select(s => s.Trim()).ToList();
            if (parts.Count > 1) parts = parts.Where(s => s.Length > 0).ToList();
            return parts.Distinct(StringComparer.Ordinal).ToList();
        }

        /// <summary>给 EF 谓词用：不限门店时 all = true；list 永远非 null（Contains 在 null 集合参数上可能抛异常）。</summary>
        public static (bool all, List<string> list) ForQuery(string? shop)
        {
            List<string>? parsed = Parse(shop);
            return (parsed == null, parsed ?? new List<string>());
        }
    }
}
