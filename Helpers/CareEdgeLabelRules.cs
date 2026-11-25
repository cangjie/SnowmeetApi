using System;
using System.Globalization;
using System.Text.RegularExpressions;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 修刃角度与标签存根（用户 2026-11-25 要求）：
    ///   · 修刃开始之前，角度都可以改；
    ///   · 改了角度，标签存根上印的「修刃 89」就错了，必须重新打印标签，不重新打印不能点「开始修刃」。
    ///
    /// 状态记在 care.label_edge_degree（varchar(20) NULL）：「标签上印的角度」。
    ///   · NULL = 没有待重打的改动（标签和当前角度一致，开单时打的就是当时的角度；存量数据全是 NULL）；
    ///   · 第一次改角度时，把改之前的角度记进去（那就是标签上印的），之后连改几次都不变；
    ///   · 重新打印成功后清回 NULL；
    ///   · 待重打 = 它不是 NULL 且和当前角度不一致，所以把角度又改回标签上的值，也就不用重打了。
    /// 标签上 edge_degree 为 NULL 时印的是 89（print_care_label.js），所以比较时同样把 NULL 当 89。
    /// </summary>
    public static class CareEdgeLabelRules
    {
        public const string DefaultDegree = "89";
        public const string EdgeTaskName = "修刃";

        // 角度：1～2 位整数，可带 1 位小数，范围 (0, 90]
        private static readonly Regex AnglePattern = new(@"^\d{1,2}(\.\d)?$", RegexOptions.Compiled);

        /// <summary>标签上印出来的角度（edge_degree 为空按 89，和打印标签的小程序组件一致）。</summary>
        public static string PrintedDegree(string? edgeDegree)
            => edgeDegree == null ? DefaultDegree : edgeDegree.Trim();

        /// <summary>这件装备有没有「角度改过、标签还没重新打」的状态。</summary>
        public static bool IsPending(string? labelEdgeDegree, string? edgeDegree)
        {
            if (labelEdgeDegree == null) return false;
            return !string.Equals(labelEdgeDegree.Trim(), PrintedDegree(edgeDegree), StringComparison.Ordinal);
        }

        /// <summary>解析店员输入的新角度；合法返回规范写法（"89.0" → "89"），不合法返回 false 和原因。</summary>
        public static bool TryParseAngle(string? input, out string normalized, out string error)
        {
            normalized = "";
            error = "";
            string text = (input ?? "").Trim();
            if (text.Length == 0)
            {
                error = "请输入修刃角度";
                return false;
            }
            if (!AnglePattern.IsMatch(text)
                || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                || value <= 0 || value > 90)
            {
                error = "修刃角度要在 0～90 之间，最多一位小数";
                return false;
            }
            normalized = value.ToString("0.#", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// 能不能改角度：装备有效、没取消、要修刃，且修刃任务存在、还没开始。返回不能改的原因，能改返回 null。
        /// </summary>
        public static string? WhyCannotChange(Care? care, CareTask? edgeTask)
        {
            if (care == null || care.valid != 1) return "未找到养护装备";
            if (care.is_cancel) return "这件养护已取消，不能修改角度";
            if (care.need_edge != 1 || edgeTask == null) return "这件装备没有修刃任务，不能修改角度";
            if (edgeTask.status != "未开始") return "修刃已经开始，不能再修改角度";
            return null;
        }

        /// <summary>开始修刃前的拦截提示；不用拦返回 null。</summary>
        public static string? StartBlockedMessage(Care? care)
        {
            if (care == null || !IsPending(care.label_edge_degree, care.edge_degree)) return null;
            return "修刃角度已改为 " + PrintedDegree(care.edge_degree) + "°（标签上还是 "
                + care.label_edge_degree!.Trim() + "°），请先重新打印标签存根，再开始修刃";
        }
    }
}
