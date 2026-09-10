using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public sealed class ReqaiAdminAssistantClient : IReqaiAdminAssistantClient
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new DateOnlyJsonConverter(), new DateTimeDateOnlyJsonConverter() }
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public ReqaiAdminAssistantClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public Task<string> PlanAsync(ReqaiPlanRequest request, CancellationToken cancellationToken) =>
            PostAsync("/api/service/admin-assistant/plan", request, cancellationToken);

        public async Task<AssistantReply> FinalizeAsync(ReqaiFinalizeRequest request, CancellationToken cancellationToken)
        {
            string payload = await PostAsync("/api/service/admin-assistant/finalize", request, cancellationToken);
            return ParseFinalizeReply(payload);
        }

        public async Task<LegacyRentIntent> LegacyRentIntentAsync(string question, CancellationToken cancellationToken)
        {
            string payload = await PostAsync("/api/service/rent-query-intent", new { question }, cancellationToken);
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("intent", out JsonElement intent)) throw new ReqaiException();
                LegacyRentIntent? parsed = JsonSerializer.Deserialize<LegacyRentIntent>(intent.GetRawText(), JsonOptions);
                if (parsed == null || (parsed.status != "ready" && parsed.status != "clarification_required" && parsed.status != "unsupported"))
                    throw new ReqaiException();
                return parsed;
            }
            catch (JsonException)
            {
                throw new ReqaiException();
            }
        }

        public async Task<AssistantReply> LegacyPageHelpAsync(AdminAssistantRequest request, int staffId, string traceId,
            CancellationToken cancellationToken)
        {
            string payload = await PostAsync("/api/service/page-help", new
            {
                page_key = request.page_key,
                operation = "follow_up",
                prompt = request.question,
                staff_id = staffId,
                trace_id = traceId,
                business_context = new { conversation = request.conversation }
            }, cancellationToken);
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("answer", out JsonElement answer) ||
                    answer.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(answer.GetString()))
                    throw new ReqaiException();

                List<JsonElement> citations = new();
                if (root.TryGetProperty("citations", out JsonElement citationElement))
                {
                    if (citationElement.ValueKind != JsonValueKind.Array) throw new ReqaiException();
                    foreach (JsonElement citation in citationElement.EnumerateArray()) citations.Add(citation.Clone());
                }
                return new AssistantReply { text = answer.GetString()!, citations = citations };
            }
            catch (JsonException)
            {
                throw new ReqaiException();
            }
        }

        private async Task<string> PostAsync(string path, object body, CancellationToken cancellationToken)
        {
            string baseUrl = _configuration["Reqai:BaseUrl"]?.TrimEnd('/') ?? "";
            string token = _configuration["Reqai:ServiceToken"] ?? "";
            if (baseUrl.Length == 0 || token.Length == 0)
                throw new InvalidOperationException("reqai 服务地址或服务凭据未配置");

            using HttpRequestMessage request = new(HttpMethod.Post, baseUrl + path)
            {
                Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Snowmeet-Service-Token", token);
            using HttpResponseMessage response = await _httpClientFactory.CreateClient("Reqai").SendAsync(request, cancellationToken);
            string payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) throw new ReqaiException(response.StatusCode);
            return payload;
        }

        private static AssistantReply ParseFinalizeReply(string payload)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(payload);
                JsonElement root = document.RootElement;
                RequireOnlyProperties(root, "version", "reply", "model", "effort");
                if (!root.TryGetProperty("version", out JsonElement version) || version.GetString() != "1" ||
                    !root.TryGetProperty("reply", out JsonElement reply))
                    throw new ReqaiException();

                string planJson = "{\"version\":\"1\",\"reply\":" + reply.GetRawText() + ",\"actions\":[]}";
                return AdminAssistantProtocolRules.ParsePlan(planJson).reply!;
            }
            catch (JsonException)
            {
                throw new ReqaiException();
            }
            catch (InvalidOperationException)
            {
                throw new ReqaiException();
            }
        }

        private static void RequireOnlyProperties(JsonElement element, params string[] allowed)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new ReqaiException();
            HashSet<string> names = new(allowed, StringComparer.Ordinal);
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new ReqaiException();
            }
        }

        private sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
        {
            public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                DateOnly.Parse(reader.GetString()!);

            public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
        }

        private sealed class DateTimeDateOnlyJsonConverter : JsonConverter<DateTime>
        {
            public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                DateTime.Parse(reader.GetString()!);

            public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString("yyyy-MM-dd"));
        }
    }
}
