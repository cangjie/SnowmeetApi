using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    /// <summary>
    /// 一个业务域的只读查询执行器。
    ///
    /// 域和执行器一一对应：注册表里加了域却忘了执行器，解析阶段就会因为找不到执行器而
    /// 明确报错，而不是悄悄去查另一张表。
    /// </summary>
    public interface IAdminAssistantQueryExecutor
    {
        string actionType { get; }

        Task<AdminAssistantQueryExecution> ExecuteAsync(
            AdminAssistantQueryState state,
            AdminAssistantDomain domain,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy,
            CancellationToken cancellationToken);
    }

    public sealed record AdminAssistantQueryExecution(
        AdminAssistantQueryState state,
        QuerySummary summary);
}
