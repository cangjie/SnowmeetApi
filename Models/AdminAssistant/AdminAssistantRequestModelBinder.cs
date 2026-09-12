using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SnowmeetApi.Helpers;

namespace SnowmeetApi.Models.AdminAssistant
{
    public sealed class AdminAssistantRequestModelBinder : IModelBinder
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false
        };

        public async Task BindModelAsync(ModelBindingContext bindingContext)
        {
            try
            {
                using JsonDocument document = await JsonDocument.ParseAsync(bindingContext.HttpContext.Request.Body,
                    cancellationToken: bindingContext.HttpContext.RequestAborted);
                AdminAssistantRequest? request = IsValidEnvelope(document.RootElement)
                    ? JsonSerializer.Deserialize<AdminAssistantRequest>(document.RootElement.GetRawText(), JsonOptions)
                    : null;
                bindingContext.Result = ModelBindingResult.Success(request);
            }
            catch (JsonException)
            {
                bindingContext.Result = ModelBindingResult.Success(null);
            }
        }

        private static bool IsValidEnvelope(JsonElement root)
        {
            if (!HasOnlyProperties(root, "version", "page_key", "question", "conversation", "context")) return false;
            if (!HasType(root, "version", JsonValueKind.String) || !HasType(root, "page_key", JsonValueKind.String) ||
                !HasType(root, "question", JsonValueKind.String)) return false;
            if (root.TryGetProperty("conversation", out JsonElement conversation) && !IsConversation(conversation)) return false;
            return !root.TryGetProperty("context", out JsonElement context) || IsContext(context);
        }

        private static bool IsConversation(JsonElement conversation)
        {
            if (conversation.ValueKind != JsonValueKind.Array) return false;
            foreach (JsonElement message in conversation.EnumerateArray())
            {
                if (!HasOnlyProperties(message, "role", "content") || !HasType(message, "role", JsonValueKind.String) ||
                    !HasType(message, "content", JsonValueKind.String)) return false;
            }
            return true;
        }

        /// <summary>
        /// 上下文按业务域分键保存，另带一个 active_query_type 指明当前进行中的是哪个域。
        /// 各域的字段白名单来自 AdminAssistantDomains，和协议校验用的是同一张表。
        /// </summary>
        private static bool IsContext(JsonElement context)
        {
            List<string> allowed = new() { "active_query_type" };
            allowed.AddRange(AdminAssistantDomains.ContextKeys);
            if (!HasOnlyProperties(context, allowed.ToArray())) return false;
            if (!IsOptionalString(context, "active_query_type")) return false;
            if (context.TryGetProperty("active_query_type", out JsonElement activeType) &&
                activeType.ValueKind == JsonValueKind.String &&
                AdminAssistantDomains.Find(activeType.GetString()) == null) return false;

            foreach (AdminAssistantDomain domain in AdminAssistantDomains.All.Values)
            {
                if (!context.TryGetProperty(domain.contextKey, out JsonElement state) ||
                    state.ValueKind == JsonValueKind.Null) continue;
                if (!IsDomainState(state, domain)) return false;
            }
            return true;
        }

        private static bool IsDomainState(JsonElement state, AdminAssistantDomain domain)
        {
            if (!HasOnlyProperties(state, domain.fields.ToArray())) return false;
            foreach (string field in domain.fields)
            {
                bool ok = field switch
                {
                    "start_date" or "end_date" => IsOptionalDate(state, field),
                    "is_test" or "is_entertain" or "have_discount" or "use_card"
                        or "has_retail" or "is_summer_care" => IsOptionalBoolean(state, field),
                    _ => IsOptionalString(state, field)
                };
                if (!ok) return false;
            }
            return true;
        }

        private static bool HasOnlyProperties(JsonElement value, params string[] names)
        {
            if (value.ValueKind != JsonValueKind.Object) return false;
            HashSet<string> allowed = new(names, StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                if (!allowed.Contains(property.Name) || !seen.Add(property.Name)) return false;
            }
            return true;
        }

        private static bool HasType(JsonElement value, string property, JsonValueKind kind) =>
            value.TryGetProperty(property, out JsonElement child) && child.ValueKind == kind;

        private static bool IsOptionalString(JsonElement value, string property) => !value.TryGetProperty(property, out JsonElement child) ||
            child.ValueKind is JsonValueKind.Null or JsonValueKind.String;

        private static bool IsOptionalBoolean(JsonElement value, string property) => !value.TryGetProperty(property, out JsonElement child) ||
            child.ValueKind is JsonValueKind.Null or JsonValueKind.True or JsonValueKind.False;

        private static bool IsOptionalDate(JsonElement value, string property)
        {
            if (!value.TryGetProperty(property, out JsonElement child) || child.ValueKind == JsonValueKind.Null) return true;
            return child.ValueKind == JsonValueKind.String && DateTime.TryParseExact(child.GetString(), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }
    }
}
