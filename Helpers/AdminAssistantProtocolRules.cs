using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Helpers
{
    public static class AdminAssistantProtocolRules
    {
        /// <summary>
        /// keyword 只能是订单号或顾客姓名。命中这些词说明模型在用自由文本表达
        /// 本该由专属字段承载的概念 —— 那正是「查养护查成租赁备注含养护」的成因。
        /// 门店名不在这里：它是库里的业务数据，由 AdminAssistantService 用 shop 表另行核对。
        /// </summary>
        private static readonly string[] ConceptKeywords = BuildConceptKeywords();

        private static string[] BuildConceptKeywords()
        {
            HashSet<string> terms = new(StringComparer.Ordinal)
            {
                "租赁", "养护", "零售", "雪票", "自我游",
                "次卡", "季卡", "储值", "减免", "招待", "测试单", "押金",
                "订单数", "应收", "实收", "退款"
            };
            foreach (string status in AdminAssistantDomains.RentStatuses) terms.Add(status);
            foreach (string retailType in AdminAssistantDomains.RetailTypes) terms.Add(retailType);
            foreach (string business in AdminAssistantDomains.KnownUnsupported) terms.Add(business);
            return terms.OrderByDescending(term => term.Length).ToArray();
        }

        public static ReqaiPlanResponse ParsePlan(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            RequireObject(root, "计划");
            RejectUnknownProperties(root, "version", "reply", "actions", "unsupported");

            string version = RequiredString(root, "version");
            if (version != "1") throw new InvalidOperationException("不支持的协议版本");

            AssistantReply? reply = null;
            if (root.TryGetProperty("reply", out JsonElement replyElement) && replyElement.ValueKind != JsonValueKind.Null)
                reply = ParseReply(replyElement);

            AdminAssistantUnsupported? unsupported = null;
            if (root.TryGetProperty("unsupported", out JsonElement unsupportedElement) && unsupportedElement.ValueKind != JsonValueKind.Null)
                unsupported = ParseUnsupported(unsupportedElement);

            List<ReqaiPlanAction> actions = new();
            if (root.TryGetProperty("actions", out JsonElement actionsElement))
            {
                if (actionsElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("actions 必须是数组");
                if (actionsElement.GetArrayLength() > 1) throw new InvalidOperationException("最多允许一个 action");
                foreach (JsonElement action in actionsElement.EnumerateArray()) actions.Add(ParseAction(action));
            }
            if (unsupported != null && actions.Count > 0) throw new InvalidOperationException("unsupported 与 actions 不能同时出现");
            if (unsupported != null && reply == null) throw new InvalidOperationException("unsupported 必须同时给出文字说明");
            if (reply == null && actions.Count == 0) throw new InvalidOperationException("reply 和 actions 不能同时为空");

            return new ReqaiPlanResponse { version = version, reply = reply, actions = actions, unsupported = unsupported };
        }

        private static AdminAssistantUnsupported ParseUnsupported(JsonElement element)
        {
            RequireObject(element, "unsupported");
            RejectUnknownProperties(element, "reason", "target_domain", "detail", "suggested_page");
            AdminAssistantUnsupported parsed = new()
            {
                reason = RequiredString(element, "reason"),
                target_domain = RequiredString(element, "target_domain"),
                detail = RequiredString(element, "detail"),
                suggested_page = RequiredString(element, "suggested_page")
            };
            if (parsed.detail.Length > 200) throw new InvalidOperationException("unsupported 说明过长");
            return parsed;
        }

        public static AdminAssistantQueryPatch ParseArguments(string json, AdminAssistantDomain domain)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ParseArguments(document.RootElement, domain);
        }

        public static AdminAssistantQueryPatch ParseArguments(JsonElement element, AdminAssistantDomain domain)
        {
            RequireObject(element, "arguments");
            RejectUnknownProperties(element, domain.fields);
            AdminAssistantQueryPatch patch = new();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                patch.Specified.Add(property.Name);
                SetPatchValue(patch, property.Name, property.Value);
            }
            return patch;
        }

        public static AdminAssistantQueryState Merge(string mode, AdminAssistantQueryState current, AdminAssistantQueryPatch patch)
        {
            if (mode != "replace" && mode != "patch") throw new InvalidOperationException("不支持的 action mode");
            AdminAssistantQueryState merged = mode == "patch" ? Copy(current) : new AdminAssistantQueryState();
            foreach (string field in patch.Specified) CopyPatchValue(patch, merged, field);
            return merged;
        }

        public static void ValidateQuery(AdminAssistantQueryState state, AdminAssistantDomain domain)
        {
            if (state.start_date == null || state.end_date == null)
                throw new AdminAssistantClarificationException("请明确查询日期范围。");
            if (state.start_date.Value.TimeOfDay != TimeSpan.Zero || state.end_date.Value.TimeOfDay != TimeSpan.Zero)
                throw new InvalidOperationException("查询日期必须是 yyyy-MM-dd");
            if (state.end_date < state.start_date || state.end_date.Value.Date - state.start_date.Value.Date > TimeSpan.FromDays(365))
                throw new InvalidOperationException("查询日期范围不能超过 365 天");

            // 本域没有的条件即使被合并进来也不能放行：它可能来自客户端伪造的上下文，
            // 也可能来自 patch 跨域，两种情况都必须挡在执行器之前。
            foreach (string field in AdminAssistantDomains.AllFields)
            {
                if (!domain.Allows(field) && state.Read(field) != null)
                    throw new InvalidOperationException(domain.label + "不支持条件 " + field);
            }

            state.shop = NormalizeOptional(state.shop, "门店", 64);
            state.rent_status = NormalizeOptional(state.rent_status, "租赁状态", 64);
            state.retail_type = NormalizeOptional(state.retail_type, "零售子类型", 64);
            state.keyword = NormalizeOptional(state.keyword, "关键词", 40);
            state.cell_suffix = NormalizeOptional(state.cell_suffix, "手机号条件", 15);

            if (state.rent_status != null && !AdminAssistantDomains.RentStatuses.Contains(state.rent_status))
                throw new InvalidOperationException("租赁状态不支持");
            if (state.retail_type != null && !AdminAssistantDomains.RetailTypes.Contains(state.retail_type))
                throw new InvalidOperationException("零售子类型不支持");
            if (state.cell_suffix != null && (state.cell_suffix.Length < 4 || state.cell_suffix.Length > 15 || !state.cell_suffix.All(char.IsDigit)))
                throw new InvalidOperationException("手机号条件不合法");
            if (state.keyword != null) ValidateKeyword(state.keyword);
        }

        /// <summary>keyword 不能夹带业务概念。服务端硬校验，不依赖模型听话。</summary>
        public static void ValidateKeyword(string keyword)
        {
            if (keyword.Any(char.IsWhiteSpace))
                throw new AdminAssistantKeywordException("关键词不能包含空格");
            foreach (string term in ConceptKeywords)
            {
                if (keyword.Contains(term, StringComparison.Ordinal))
                    throw new AdminAssistantKeywordException("关键词不能表达「" + term + "」这类已有专属条件的概念");
            }
        }

        private static AssistantReply ParseReply(JsonElement element)
        {
            RequireObject(element, "reply");
            RejectUnknownProperties(element, "text", "citations");
            string text = RequiredString(element, "text");
            if (text.Length > 12_000) throw new InvalidOperationException("reply 文本过长");
            List<JsonElement> citations = new();
            if (element.TryGetProperty("citations", out JsonElement citationElement))
            {
                if (citationElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("citations 必须是数组");
            }
            return new AssistantReply { text = text, citations = citations };
        }

        private static ReqaiPlanAction ParseAction(JsonElement element)
        {
            RequireObject(element, "action");
            RejectUnknownProperties(element, "id", "type", "mode", "arguments", "aggregation");
            string id = RequiredString(element, "id");
            if (id.Length > 64) throw new InvalidOperationException("action id 过长");
            string type = RequiredString(element, "type");
            AdminAssistantDomain domain = AdminAssistantDomains.Require(type);
            string mode = RequiredString(element, "mode");
            if (mode != "replace" && mode != "patch") throw new InvalidOperationException("不支持的 action mode");
            if (!element.TryGetProperty("arguments", out JsonElement arguments)) throw new InvalidOperationException("action 缺少 arguments");

            AggregationRequest aggregation = element.TryGetProperty("aggregation", out JsonElement aggregationElement)
                ? ParseAggregation(aggregationElement, domain) : new AggregationRequest();
            return new ReqaiPlanAction
            {
                id = id, type = type, mode = mode,
                arguments = ParseArguments(arguments, domain), aggregation = aggregation
            };
        }

        private static AggregationRequest ParseAggregation(JsonElement element, AdminAssistantDomain domain)
        {
            RequireObject(element, "aggregation");
            RejectUnknownProperties(element, "metrics", "group_by");
            List<string> metrics = element.TryGetProperty("metrics", out JsonElement metricsElement)
                ? ParseStringArray(metricsElement, "metrics") : new List<string> { domain.countMetric };
            List<string> groupBy = element.TryGetProperty("group_by", out JsonElement groupElement)
                ? ParseStringArray(groupElement, "group_by") : new List<string>();
            if (metrics.Count == 0 || metrics.Any(metric => !domain.metrics.Contains(metric)) || metrics.Distinct(StringComparer.Ordinal).Count() != metrics.Count)
                throw new InvalidOperationException("聚合指标不支持");
            if (groupBy.Count > domain.maxGroupBy || groupBy.Any(group => !domain.groupBy.Contains(group)) || groupBy.Distinct(StringComparer.Ordinal).Count() != groupBy.Count)
                throw new InvalidOperationException("聚合分组不支持");
            return new AggregationRequest { metrics = metrics, group_by = groupBy };
        }

        private static List<string> ParseStringArray(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Array) throw new InvalidOperationException(name + " 必须是数组");
            List<string> values = new();
            foreach (JsonElement value in element.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.String) throw new InvalidOperationException(name + " 只能包含字符串");
                values.Add(value.GetString()!);
            }
            return values;
        }

        private static void SetPatchValue(AdminAssistantQueryPatch patch, string name, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null) return;
            switch (name)
            {
                case "start_date": patch.start_date = ParseDate(value, name); break;
                case "end_date": patch.end_date = ParseDate(value, name); break;
                case "shop": patch.shop = ParseString(value, name, 64); break;
                case "is_test": patch.is_test = ParseBool(value, name); break;
                case "is_entertain": patch.is_entertain = ParseBool(value, name); break;
                case "have_discount": patch.have_discount = ParseBool(value, name); break;
                case "cell_suffix": patch.cell_suffix = ParseString(value, name, 15); break;
                case "rent_status": patch.rent_status = ParseString(value, name, 64); break;
                case "use_card": patch.use_card = ParseBool(value, name); break;
                case "has_retail": patch.has_retail = ParseBool(value, name); break;
                case "keyword": patch.keyword = ParseString(value, name, 40); break;
                case "is_summer_care": patch.is_summer_care = ParseBool(value, name); break;
                case "retail_type": patch.retail_type = ParseString(value, name, 64); break;
            }
        }

        private static void CopyPatchValue(AdminAssistantQueryPatch patch, AdminAssistantQueryState target, string name)
        {
            switch (name)
            {
                case "start_date": target.start_date = patch.start_date; break;
                case "end_date": target.end_date = patch.end_date; break;
                case "shop": target.shop = patch.shop; break;
                case "is_test": target.is_test = patch.is_test; break;
                case "is_entertain": target.is_entertain = patch.is_entertain; break;
                case "have_discount": target.have_discount = patch.have_discount; break;
                case "cell_suffix": target.cell_suffix = patch.cell_suffix; break;
                case "rent_status": target.rent_status = patch.rent_status; break;
                case "use_card": target.use_card = patch.use_card; break;
                case "has_retail": target.has_retail = patch.has_retail; break;
                case "keyword": target.keyword = patch.keyword; break;
                case "is_summer_care": target.is_summer_care = patch.is_summer_care; break;
                case "retail_type": target.retail_type = patch.retail_type; break;
            }
        }

        private static AdminAssistantQueryState Copy(AdminAssistantQueryState source) => new()
        {
            start_date = source.start_date, end_date = source.end_date, shop = source.shop,
            is_test = source.is_test, is_entertain = source.is_entertain, have_discount = source.have_discount,
            cell_suffix = source.cell_suffix, rent_status = source.rent_status, use_card = source.use_card,
            has_retail = source.has_retail, keyword = source.keyword,
            is_summer_care = source.is_summer_care, retail_type = source.retail_type
        };

        private static DateTime ParseDate(JsonElement element, string name)
        {
            string value = ParseString(element, name, 10);
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                throw new InvalidOperationException(name + " 必须是 yyyy-MM-dd");
            return result;
        }

        private static string ParseString(JsonElement element, string name, int maxLength)
        {
            if (element.ValueKind != JsonValueKind.String) throw new InvalidOperationException(name + " 必须是字符串");
            string value = element.GetString()!;
            if (value.Length > maxLength) throw new InvalidOperationException(name + " 过长");
            return value;
        }

        private static bool ParseBool(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.True && element.ValueKind != JsonValueKind.False)
                throw new InvalidOperationException(name + " 必须是布尔值");
            return element.GetBoolean();
        }

        private static string? NormalizeOptional(string? value, string name, int maxLength)
        {
            if (value == null) return null;
            string trimmed = value.Trim();
            if (trimmed.Length == 0) throw new InvalidOperationException(name + "不能为空白");
            if (trimmed.Length > maxLength) throw new InvalidOperationException(name + "过长");
            return trimmed;
        }

        private static string RequiredString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                throw new InvalidOperationException("缺少字段 " + name);
            return value.GetString()!;
        }

        private static void RequireObject(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(name + " 必须是对象");
        }

        private static void RejectUnknownProperties(JsonElement element, params string[] allowed) =>
            RejectUnknownProperties(element, new HashSet<string>(allowed, StringComparer.Ordinal));

        private static void RejectUnknownProperties(JsonElement element, IReadOnlySet<string> allowed)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!allowed.Contains(property.Name)) throw new InvalidOperationException("包含不支持的字段 " + property.Name);
                if (!seen.Add(property.Name)) throw new InvalidOperationException("重复字段 " + property.Name);
            }
        }
    }

    /// <summary>keyword 夹带了业务概念。这不是服务故障，应转成明确拒答而不是 502。</summary>
    public sealed class AdminAssistantKeywordException : Exception
    {
        public AdminAssistantKeywordException(string message) : base(message) { }
    }
}
