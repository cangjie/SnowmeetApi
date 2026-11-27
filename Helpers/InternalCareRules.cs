using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    public static class InternalCareRules
    {
        public const string OrderType = "内部养护";

        public static string Status(IEnumerable<CareTask>? tasks)
        {
            var valid = (tasks ?? Enumerable.Empty<CareTask>()).Where(t => t.valid == 1).ToList();
            if (valid.Count == 0 || valid.All(t => t.status == "未开始")) return "未开始";
            if (valid.All(t => t.status == "已完成")) return "已完成";
            return "进行中";
        }
    }
}
