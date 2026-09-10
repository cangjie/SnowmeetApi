using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;

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

        private static bool IsContext(JsonElement context)
        {
            if (!HasOnlyProperties(context, "rental_order_query")) return false;
            if (!context.TryGetProperty("rental_order_query", out JsonElement state) || state.ValueKind == JsonValueKind.Null) return true;
            if (!HasOnlyProperties(state, "start_date", "end_date", "shop", "rent_status", "is_test", "is_entertain",
                "have_discount", "use_card", "has_retail", "cell_suffix", "keyword")) return false;
            return IsOptionalDate(state, "start_date") && IsOptionalDate(state, "end_date") &&
                IsOptionalString(state, "shop") && IsOptionalString(state, "rent_status") &&
                IsOptionalBoolean(state, "is_test") && IsOptionalBoolean(state, "is_entertain") &&
                IsOptionalBoolean(state, "have_discount") && IsOptionalBoolean(state, "use_card") &&
                IsOptionalBoolean(state, "has_retail") && IsOptionalString(state, "cell_suffix") &&
                IsOptionalString(state, "keyword");
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
