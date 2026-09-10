using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Models.AdminAssistant;

namespace SnowmeetApi.Services.AdminAssistant
{
    public sealed class RentalOrderQueryExecutor : IRentalOrderQueryExecutor
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public RentalOrderQueryExecutor(
            ApplicationDBContext db,
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<RentalOrderQueryExecution> ExecuteAsync(
            RentalOrderQueryState state,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy,
            CancellationToken cancellationToken)
        {
            if (state.shop != null && !await _db.shop.AsNoTracking().AnyAsync(
                shop => shop.rent == 1 && shop.name == state.shop,
                cancellationToken))
            {
                throw new InvalidOperationException("租赁门店不支持");
            }

            OrderController orderController = new(_db, _config, _httpContextAccessor);
            List<Order> orders = await orderController.GetCommonOrders(
                null, state.shop, null, null, "租赁", state.start_date, state.end_date,
                null, state.is_test, state.is_entertain, null, null, state.have_discount,
                null, null, null, null, null, state.keyword, null, null, null, state.use_card,
                state.cell_suffix, state.rent_status, state.has_retail);
            orders = OrderQueryRules.FilterByCustomerCellSuffix(orders, state.cell_suffix);

            List<RentalOrderAggregateRow> rows = orders.Select(order => new RentalOrderAggregateRow(
                order.id,
                order.shop,
                order.biz_date,
                order.rentProperties?.rentStatus ?? "临时订单",
                order.totalCharge,
                order.paidAmount,
                order.refundAmount,
                order.paidAmount < order.totalCharge && order.closed == 0)).ToList();

            return new RentalOrderQueryExecution(state, RentalOrderAssistantSummary.Build(rows, metrics, groupBy));
        }
    }
}
