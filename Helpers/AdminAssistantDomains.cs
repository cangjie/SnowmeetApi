using System;
using System.Collections.Generic;
using System.Linq;

namespace SnowmeetApi.Helpers
{
    /// <summary>
    /// 管理员助手的业务域注册表，与 reqai 的 admin_assistant_capabilities.py 一一对应。
    ///
    /// 协议校验、执行器路由、出站 action 类型、上下文键名、审计投影和脱敏字段表全部从这里派生。
    /// 之所以要有这张表：同一套字段原先在五处以上被逐字段硬编码，加一个业务域必然漏改，
    /// 而漏改的表现是「查出一个看起来合理的错数字」，没有任何一层会报错。
    ///
    /// 加一个业务域：往 All 里加一条，并实现对应的 IAdminAssistantQueryExecutor。
    /// </summary>
    public sealed class AdminAssistantDomain
    {
        public string actionType { get; init; } = "";
        public string domain { get; init; } = "";

        /// <summary>GetCommonOrders 的业务类型实参；雪票不走订单表，为 null。</summary>
        public string? bizType { get; init; }

        public string label { get; init; } = "";
        public string unit { get; init; } = "";
        public string countMetric { get; init; } = "order_count";
        public string clientActionType { get; init; } = "";
        public string contextKey { get; init; } = "";

        /// <summary>Shop 表上的能力位（rent / care / sale）；雪票按雪场而非门店，为 null。</summary>
        public string? shopCapability { get; init; }

        public string pageHint { get; init; } = "";
        public IReadOnlySet<string> fields { get; init; } = new HashSet<string>();
        public IReadOnlySet<string> metrics { get; init; } = new HashSet<string>();
        public IReadOnlySet<string> groupBy { get; init; } = new HashSet<string>();
        public int maxGroupBy { get; init; } = 2;

        public bool Allows(string field) => fields.Contains(field);
    }

    public static class AdminAssistantDomains
    {
        public const string CompletedStatus = "completed";

        /// <summary>四个域字段的并集，用来定义超集 state 的搬运表。</summary>
        public static readonly IReadOnlySet<string> AllFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "start_date", "end_date", "shop", "is_test", "is_entertain", "have_discount",
            "cell_suffix", "rent_status", "use_card", "has_retail", "keyword",
            "is_summer_care", "retail_type"
        };

        public static readonly IReadOnlySet<string> RentStatuses = new HashSet<string>(StringComparer.Ordinal)
        {
            "未支付", "未开始", "租赁中", "部分归还", "全部归还", "部分退押金", "全额退押金", "了结关闭", "临时订单"
        };

        public static readonly IReadOnlySet<string> RetailTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "零售", "租赁卡类", "养护卡类", "二手零售", "期货预定"
        };

        /// <summary>认得但暂时查不了的业务，用于把「不支持」和「不认识」区分开。</summary>
        public static readonly IReadOnlyList<string> KnownUnsupported = new List<string> { "二手回收", "水吧餐厅" };

        private static readonly HashSet<string> OrderMetrics = new(StringComparer.Ordinal)
        {
            "order_count", "charge_total", "paid_total", "refund_total", "unpaid_count"
        };

        private static HashSet<string> Fields(params string[] names) => new(names, StringComparer.Ordinal);

        public static readonly IReadOnlyDictionary<string, AdminAssistantDomain> All =
            new List<AdminAssistantDomain>
            {
                new()
                {
                    actionType = "rental_order.query", domain = "rental", bizType = "租赁",
                    label = "租赁订单", unit = "单", countMetric = "order_count",
                    clientActionType = "rental_order.show_results", contextKey = "rental_order_query",
                    shopCapability = "rent", pageHint = "租赁订单列表",
                    fields = Fields("start_date", "end_date", "shop", "is_test", "is_entertain",
                        "have_discount", "cell_suffix", "rent_status", "use_card", "has_retail", "keyword"),
                    metrics = OrderMetrics,
                    groupBy = Fields("rent_status", "shop", "biz_date"), maxGroupBy = 2
                },
                new()
                {
                    actionType = "care_order.query", domain = "care", bizType = "养护",
                    label = "养护订单", unit = "单", countMetric = "order_count",
                    clientActionType = "care_order.show_results", contextKey = "care_order_query",
                    shopCapability = "care", pageHint = "养护订单列表",
                    fields = Fields("start_date", "end_date", "shop", "is_test", "is_entertain",
                        "have_discount", "cell_suffix", "use_card", "is_summer_care", "keyword"),
                    metrics = OrderMetrics,
                    groupBy = Fields("shop", "biz_date"), maxGroupBy = 2
                },
                new()
                {
                    // 零售没有 keyword：GetCommonOrders 的关键词分支只实现了租赁和养护，
                    // 零售会静默落 default，传了也不生效。与其给一个假条件，不如不开放。
                    actionType = "retail_order.query", domain = "retail", bizType = "零售",
                    label = "零售订单", unit = "单", countMetric = "order_count",
                    clientActionType = "retail_order.show_results", contextKey = "retail_order_query",
                    shopCapability = "sale", pageHint = "零售订单列表",
                    fields = Fields("start_date", "end_date", "shop", "is_test", "is_entertain",
                        "have_discount", "cell_suffix", "retail_type"),
                    metrics = OrderMetrics,
                    groupBy = Fields("shop", "biz_date"), maxGroupBy = 2
                },
                new()
                {
                    // 自我游雪票走票券表，粒度是票券不是订单，所以计数用 ticket_count。
                    actionType = "ski_pass.query", domain = "ski_pass", bizType = null,
                    label = "雪票", unit = "张", countMetric = "ticket_count",
                    clientActionType = "ski_pass.show_results", contextKey = "ski_pass_query",
                    shopCapability = null, pageHint = "雪票列表",
                    fields = Fields("start_date", "end_date"),
                    metrics = Fields("ticket_count", "order_count", "charge_total", "paid_total", "refund_total"),
                    groupBy = Fields("biz_date"), maxGroupBy = 1
                },
            }.ToDictionary(item => item.actionType, StringComparer.Ordinal);

        public static AdminAssistantDomain Require(string actionType)
        {
            if (!All.TryGetValue(actionType, out AdminAssistantDomain? found))
                throw new InvalidOperationException("不支持的 action type");
            return found;
        }

        public static AdminAssistantDomain? Find(string? actionType) =>
            actionType != null && All.TryGetValue(actionType, out AdminAssistantDomain? found) ? found : null;

        public static AdminAssistantDomain? ByContextKey(string contextKey) =>
            All.Values.FirstOrDefault(item => item.contextKey == contextKey);

        public static IEnumerable<string> ContextKeys => All.Values.Select(item => item.contextKey);
    }
}
