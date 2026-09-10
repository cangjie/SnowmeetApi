using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public interface IRentalOrderQueryExecutor
    {
        Task<RentalOrderQueryExecution> ExecuteAsync(
            RentalOrderQueryState state,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy,
            CancellationToken cancellationToken);
    }

    public sealed record RentalOrderQueryExecution(
        RentalOrderQueryState state,
        RentalOrderQuerySummary summary);
}
