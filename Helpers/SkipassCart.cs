using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SnowmeetApi.Helpers
{
    // 雪票购物车的请求体。一条 = 一种票 × 一个游玩日期 × 一个接收短信的手机号 × 张数。
    public class SkipassCartItem
    {
        public int productId { get; set; }
        public DateTime date { get; set; }
        public string cell { get; set; } = "";
        public string name { get; set; } = "";
        public int count { get; set; } = 1;
        public string? idNo { get; set; }
    }

    public class SkipassCartRequest
    {
        public List<SkipassCartItem> items { get; set; } = new List<SkipassCartItem>();
        public int? staffId { get; set; }
    }

    // 只放不碰数据库的判定，便于单测。
    // 供应商（自我游）一个订单只能订 1 张（2025-01-15 起，2026-10-31 对大好河山账号实测仍是），
    // 所以购物车里买几张就拆成几条「1 张」的雪票记录，各自向供应商下单、各自取消退款。
    public static class SkipassCartRules
    {
        public const int MAX_TICKETS = 10;
        static readonly Regex CellRegex = new Regex(@"^1\d{10}$");

        // 返回 null 表示通过，否则是给店员看的原因
        public static string? Validate(IEnumerable<SkipassCartItem>? items)
        {
            List<SkipassCartItem> list = (items ?? Enumerable.Empty<SkipassCartItem>()).ToList();
            if (list.Count == 0) return "购物车是空的";
            int total = 0;
            foreach (SkipassCartItem item in list)
            {
                if (item == null || item.productId <= 0) return "票种不正确";
                if (item.count < 1) return "张数不正确";
                if (!CellRegex.IsMatch((item.cell ?? "").Trim())) return "请填写正确的手机号";
                if (string.IsNullOrWhiteSpace(item.name)) return "请填写姓名";
                total += item.count;
                if (total > MAX_TICKETS) return "一次最多订 " + MAX_TICKETS + " 张";
            }
            return null;
        }

        // 每条按张数展开成 count=1 的条目（顺序不变）
        public static List<SkipassCartItem> Expand(IEnumerable<SkipassCartItem> items)
        {
            var result = new List<SkipassCartItem>();
            foreach (SkipassCartItem item in items)
            {
                for (int i = 0; i < item.count; i++)
                {
                    result.Add(new SkipassCartItem
                    {
                        productId = item.productId,
                        date = item.date.Date,
                        cell = item.cell.Trim(),
                        name = item.name.Trim(),
                        count = 1,
                        idNo = item.idNo
                    });
                }
            }
            return result;
        }
    }
}
