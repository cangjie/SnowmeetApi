using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    /// <summary>
    /// 自我游雪票。
    ///
    /// 与另外三个域不同源也不同粒度：数据来自票券表 skiPass（按出票日期过滤），
    /// 一个订单可能有多张票。所以先按订单去重再聚合 —— 直接按票券求和会把金额重复累加。
    /// 口径与「【雪票】自我游订单」页保持一致（该页同样排除南山；南山店已闭店）。
    /// </summary>
    public sealed class SkiPassQueryExecutor : IAdminAssistantQueryExecutor
    {
        private readonly ApplicationDBContext _db;

        public SkiPassQueryExecutor(ApplicationDBContext db)
        {
            _db = db;
        }

        public string actionType => "ski_pass.query";

        public async Task<AdminAssistantQueryExecution> ExecuteAsync(
            AdminAssistantQueryState state,
            AdminAssistantDomain domain,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy,
            CancellationToken cancellationToken)
        {
            DateTime start = state.start_date!.Value.Date;
            DateTime end = state.end_date!.Value.Date;

            List<SkiPass> passes = await _db.skiPass.AsNoTracking()
                .Where(pass => pass.valid == 1
                    && !pass.resort.Trim().Equals("南山")
                    && pass.create_date.Date >= start
                    && pass.create_date.Date <= end)
                .Include(pass => pass.order)
                    .ThenInclude(order => order.payments.Where(payment => payment.status.Equals("支付成功")))
                        .ThenInclude(payment => payment.refunds.Where(refund => refund.state == 1 || refund.refund_id != ""))
                .ToListAsync(cancellationToken);

            List<AssistantAggregateRow> rows = passes
                .Where(pass => pass.order != null)
                .GroupBy(pass => pass.order!.id)
                .Select(group =>
                {
                    Order order = group.First().order!;
                    return new AssistantAggregateRow(
                        order.id,
                        null,
                        order.biz_date,
                        null,
                        order.totalCharge,
                        order.paidAmount,
                        order.refundAmount,
                        order.paidAmount < order.totalCharge && order.closed == 0,
                        group.Count());
                })
                .ToList();

            return new AdminAssistantQueryExecution(state, AdminAssistantSummary.Build(rows, domain, metrics, groupBy));
        }
    }
}
