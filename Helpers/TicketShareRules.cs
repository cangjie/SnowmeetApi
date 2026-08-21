using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 店员分享发券的纯规则。展示文案一律在这里派生——WXML 不支持方法调用。
    /// </summary>
    public static class TicketShareRules
    {
        /// <summary>
        /// 分享批次的状态。优先级：已撤回 &gt; 已领完 &gt; 分享中。
        /// 撤回排最前是因为它是人为终止，比"领完"更该被看见。
        /// </summary>
        public static TicketStateView DescribeBatchState(TicketShareBatch batch)
        {
            if (batch == null)
            {
                return new TicketStateView() { Label = "", Cls = "" };
            }
            if (batch.valid != 1)
            {
                return new TicketStateView() { Label = "已撤回", Cls = "revoked" };
            }
            if (batch.max_claims != null && batch.claim_count >= batch.max_claims)
            {
                return new TicketStateView() { Label = "已领完", Cls = "done" };
            }
            return new TicketStateView() { Label = "分享中", Cls = "sharing" };
        }

        /// <summary>分享方式文案。</summary>
        public static string DescribeShareType(string shareType)
        {
            return (shareType ?? "").Trim() == TicketShareBatch.ShareGroup ? "分享到群" : "分享给好友";
        }

        /// <summary>
        /// 领取进度文案：个人分享是「1 张，谁先点谁得」，群分享不限人数只报已领数。
        /// </summary>
        public static string DescribeProgress(TicketShareBatch batch)
        {
            if (batch == null)
            {
                return "";
            }
            if (batch.max_claims == null)
            {
                return "已领 " + batch.claim_count + " 张（不限人数，每人一张）";
            }
            return "已领 " + batch.claim_count + " / " + batch.max_claims + " 张";
        }

        /// <summary>批次还能不能被领：撤回了、或者领满了都不行。</summary>
        public static bool IsClaimable(TicketShareBatch batch)
        {
            return batch != null && batch.valid == 1
                && (batch.max_claims == null || batch.claim_count < batch.max_claims);
        }
    }
}
