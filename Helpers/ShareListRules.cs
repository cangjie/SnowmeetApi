using System;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Helpers
{
    // 分账列表的规则（纯函数）。设计：snowmeet_ai_doc/docs/superpowers/specs/2026-11-03-share-list-manual-batch-design.md §6、§7。

    /// <summary>一条 payment_share（分账记录）里列表要用的事实。</summary>
    public sealed record RecordFacts(bool? Success, DateTime? SubmitTime, DateTime? ResponseTime, bool CannotShare);

    /// <summary>一条 order_share（订单上一个收款人的应分）和它名下有效分账记录的事实。</summary>
    public sealed record ShareFacts(bool AutoShare, int? BatchId, IReadOnlyList<RecordFacts> Records);

    public static class ShareListRules
    {
        public const int BatchNoMaxLength = 40;

        /// <summary>已分 = 记过手动批次，或有分账记录且全部成功；其余（包括只成功了一部分）都是未分。</summary>
        public static string Status(ShareFacts f)
            => f.BatchId != null || (f.Records.Count > 0 && f.Records.All(r => r.Success == true)) ? "已分" : "未分";

        /// <summary>
        /// 手动的原因；自动返回 null。判断顺序：已记手动批次 → 店长设成手动 → 系统标了不能自动分 → 自动提交被拒绝 → 没有分账记录。
        /// 已经全部自动分完的（没有批次）一定是自动。
        /// </summary>
        public static string? ManualReason(ShareFacts f)
        {
            if (f.BatchId != null) return "已记手动批次";
            if (f.Records.Count > 0 && f.Records.All(r => r.Success == true)) return null;
            if (!f.AutoShare) return "设为手动";
            if (f.Records.Any(r => r.SubmitTime == null && r.CannotShare)) return "系统标手动";
            if (f.Records.Any(r => r.SubmitTime != null && r.ResponseTime != null && r.Success != true)) return "自动被拒绝";
            if (f.Records.Count == 0) return "没有分账记录";
            return null;
        }

        public static string Mode(ShareFacts f) => ManualReason(f) == null ? "自动" : "手动";

        /// <summary>可以勾选做手动分账：未分 + 手动。</summary>
        public static bool Selectable(ShareFacts f) => Status(f) == "未分" && Mode(f) == "手动";

        /// <param name="batchesToday">今天已经有几个批次</param>
        public static string DefaultBatchNo(DateTime today, int batchesToday)
            => "MS-" + today.ToString("yyyyMMdd") + "-" + (batchesToday + 1).ToString("00");

        /// <returns>批次号合法返回 null，否则是给用户看的原因（去首尾空格后判断）。</returns>
        public static string? BatchNoProblem(string? batchNo)
        {
            string no = (batchNo ?? "").Trim();
            if (no == "") return "请填写批次号";
            if (no.Length > BatchNoMaxLength) return "批次号最长 " + BatchNoMaxLength + " 个字";
            return null;
        }
    }
}
