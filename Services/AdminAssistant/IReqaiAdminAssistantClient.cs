using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public interface IReqaiAdminAssistantClient
    {
        Task<string> PlanAsync(ReqaiPlanRequest request, CancellationToken cancellationToken);
        Task<AssistantReply> FinalizeAsync(ReqaiFinalizeRequest request, CancellationToken cancellationToken);
        Task<LegacyRentIntent> LegacyRentIntentAsync(string question, CancellationToken cancellationToken);
        Task<AssistantReply> LegacyPageHelpAsync(AdminAssistantRequest request, int staffId, string traceId, CancellationToken cancellationToken);
    }

    public interface IAdminAssistantService
    {
        Task<AdminAssistantExecutionResult> AskAsync(AdminAssistantRequest request, Models.Staff staff,
            string traceId, bool structuredEnabled, CancellationToken cancellationToken);
    }

    public sealed class AdminAssistantExecutionResult
    {
        public AdminAssistantResponse response { get; init; } = new();
        public AdminAssistantAuditData audit { get; init; } = new();
    }

    public sealed class ReqaiException : Exception
    {
        public HttpStatusCode statusCode { get; }

        public ReqaiException(HttpStatusCode statusCode)
            : base("管理员助手服务请求失败")
        {
            this.statusCode = statusCode;
        }

        public ReqaiException()
            : this(HttpStatusCode.BadGateway)
        {
        }
    }
}
