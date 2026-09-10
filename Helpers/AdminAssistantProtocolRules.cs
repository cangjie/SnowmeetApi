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
        private static readonly HashSet<string> QueryFields = new(StringComparer.Ordinal)
        {
            "start_date", "end_date", "shop", "rent_status", "is_test", "is_entertain",
            "have_discount", "use_card", "has_retail", "cell_suffix", "keyword"
        };

        private static readonly HashSet<string> RentStatuses = new(StringComparer.Ordinal)
        {
            "未支付", "未开始", "租赁中", "部分归还", "全部归还", "部分退押金", "全额退押金", "了结关闭", "临时订单"
        };

        private static readonly HashSet<string> Metrics = new(StringComparer.Ordinal)
        {
            "order_count", "charge_total", "paid_total", "refund_total", "unpaid_count"
        };

        private static readonly HashSet<string> GroupBy = new(StringComparer.Ordinal)
        {
            "rent_status", "shop", "biz_date"
        };

        public static ReqaiPlanResponse ParsePlan(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            RequireObject(root, "计划");
            RejectUnknownProperties(root, "version", "reply", "actions");

            string version = RequiredString(root, "version");
            if (version != "1") throw new InvalidOperationException("不支持的协议版本");

            AssistantReply? reply = null;
            if (root.TryGetProperty("reply", out JsonElement replyElement) && replyElement.ValueKind != JsonValueKind.Null)
                reply = ParseReply(replyElement);

            List<ReqaiPlanAction> actions = new();
            if (root.TryGetProperty("actions", out JsonElement actionsElement))
            {
                if (actionsElement.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("actions 必须是数组");
                if (actionsElement.GetArrayLength() > 1) throw new InvalidOperationException("最多允许一个 action");
                foreach (JsonElement action in actionsElement.EnumerateArray()) actions.Add(ParseAction(action));
            }
            if (reply == null && actions.Count == 0) throw new InvalidOperationException("reply 和 actions 不能同时为空");

            return new ReqaiPlanResponse { version = version, reply = reply, actions = actions };
        }

        public static RentalOrderQueryPatch ParseArguments(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return ParseArguments(document.RootElement);
        }

        public static RentalOrderQueryPatch ParseArguments(JsonElement element)
        {
            RequireObject(element, "arguments");
            RejectUnknownProperties(element, QueryFields);
            RentalOrderQueryPatch patch = new();
            foreach (JsonProperty property in element.EnumerateObject())
            {
                patch.Specified.Add(property.Name);
                SetPatchValue(patch, property.Name, property.Value);
            }
            return patch;
        }

        public static RentalOrderQueryState Merge(string mode, RentalOrderQueryState current, RentalOrderQueryPatch patch)
        {
            if (mode != "replace" && mode != "patch") throw new InvalidOperationException("不支持的 action mode");
            RentalOrderQueryState merged = mode == "patch" ? Copy(current) : new RentalOrderQueryState();
            foreach (string field in patch.Specified) CopyPatchValue(patch, merged, field);
            return merged;
        }

        public static void ValidateQuery(RentalOrderQueryState state)
        {
            if (state.start_date == null || state.end_date == null)
                throw new AdminAssistantClarificationException("请明确查询日期范围。");
            if (state.end_date < state.start_date || state.end_date > state.start_date.Value.AddDays(365))
                throw new InvalidOperationException("查询日期范围不能超过 365 天");
            if (state.rent_status != null && !RentStatuses.Contains(state.rent_status))
                throw new InvalidOperationException("租赁状态不支持");
            if (state.cell_suffix != null && (state.cell_suffix.Length < 4 || state.cell_suffix.Length > 15 || !state.cell_suffix.All(char.IsDigit)))
                throw new InvalidOperationException("手机号条件不合法");
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
                citations.AddRange(citationElement.EnumerateArray().Select(value => value.Clone()));
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
            if (type != "rental_order.query") throw new InvalidOperationException("不支持的 action type");
            string mode = RequiredString(element, "mode");
            if (mode != "replace" && mode != "patch") throw new InvalidOperationException("不支持的 action mode");
            if (!element.TryGetProperty("arguments", out JsonElement arguments)) throw new InvalidOperationException("action 缺少 arguments");

            AggregationRequest aggregation = element.TryGetProperty("aggregation", out JsonElement aggregationElement)
                ? ParseAggregation(aggregationElement) : new AggregationRequest();
            return new ReqaiPlanAction { id = id, type = type, mode = mode, arguments = ParseArguments(arguments), aggregation = aggregation };
        }

        private static AggregationRequest ParseAggregation(JsonElement element)
        {
            RequireObject(element, "aggregation");
            RejectUnknownProperties(element, "metrics", "group_by");
            List<string> metrics = element.TryGetProperty("metrics", out JsonElement metricsElement)
                ? ParseStringArray(metricsElement, "metrics") : new List<string> { "order_count" };
            List<string> groupBy = element.TryGetProperty("group_by", out JsonElement groupElement)
                ? ParseStringArray(groupElement, "group_by") : new List<string>();
            if (metrics.Count == 0 || metrics.Any(metric => !Metrics.Contains(metric)) || metrics.Distinct(StringComparer.Ordinal).Count() != metrics.Count)
                throw new InvalidOperationException("聚合指标不支持");
            if (groupBy.Count > 2 || groupBy.Any(group => !GroupBy.Contains(group)) || groupBy.Distinct(StringComparer.Ordinal).Count() != groupBy.Count)
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

        private static void SetPatchValue(RentalOrderQueryPatch patch, string name, JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Null) return;
            switch (name)
            {
                case "start_date": patch.start_date = ParseDate(value, name); break;
                case "end_date": patch.end_date = ParseDate(value, name); break;
                case "shop": patch.shop = ParseString(value, name, 64); break;
                case "rent_status": patch.rent_status = ParseString(value, name, 64); break;
                case "is_test": patch.is_test = ParseBool(value, name); break;
                case "is_entertain": patch.is_entertain = ParseBool(value, name); break;
                case "have_discount": patch.have_discount = ParseBool(value, name); break;
                case "use_card": patch.use_card = ParseBool(value, name); break;
                case "has_retail": patch.has_retail = ParseBool(value, name); break;
                case "cell_suffix": patch.cell_suffix = ParseString(value, name, 15); break;
                case "keyword": patch.keyword = ParseString(value, name, 100); break;
            }
        }

        private static void CopyPatchValue(RentalOrderQueryPatch patch, RentalOrderQueryState target, string name)
        {
            switch (name)
            {
                case "start_date": target.start_date = patch.start_date; break;
                case "end_date": target.end_date = patch.end_date; break;
                case "shop": target.shop = patch.shop; break;
                case "rent_status": target.rent_status = patch.rent_status; break;
                case "is_test": target.is_test = patch.is_test; break;
                case "is_entertain": target.is_entertain = patch.is_entertain; break;
                case "have_discount": target.have_discount = patch.have_discount; break;
                case "use_card": target.use_card = patch.use_card; break;
                case "has_retail": target.has_retail = patch.has_retail; break;
                case "cell_suffix": target.cell_suffix = patch.cell_suffix; break;
                case "keyword": target.keyword = patch.keyword; break;
            }
        }

        private static RentalOrderQueryState Copy(RentalOrderQueryState source) => new()
        {
            start_date = source.start_date, end_date = source.end_date, shop = source.shop, rent_status = source.rent_status,
            is_test = source.is_test, is_entertain = source.is_entertain, have_discount = source.have_discount,
            use_card = source.use_card, has_retail = source.has_retail, cell_suffix = source.cell_suffix, keyword = source.keyword
        };

        private static DateTime ParseDate(JsonElement element, string name)
        {
            string value = ParseString(element, name, 10);
            if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime result))
                throw new InvalidOperationException(name + " 必须是 yyyy-MM-dd 日期");
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

        private static string RequiredString(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement value)) throw new InvalidOperationException("缺少 " + name);
            string result = ParseString(value, name, int.MaxValue);
            if (result.Length == 0) throw new InvalidOperationException(name + " 不能为空");
            return result;
        }

        private static void RequireObject(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidOperationException(name + " 必须是对象");
        }

        private static void RejectUnknownProperties(JsonElement element, params string[] allowed) =>
            RejectUnknownProperties(element, new HashSet<string>(allowed, StringComparer.Ordinal));

        private static void RejectUnknownProperties(JsonElement element, ISet<string> allowed)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!allowed.Contains(property.Name)) throw new InvalidOperationException("不支持的字段：" + property.Name);
                if (!seen.Add(property.Name)) throw new InvalidOperationException("字段不能重复：" + property.Name);
            }
        }
    }
}
