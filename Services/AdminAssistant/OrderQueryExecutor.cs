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
    /// <summary>
    /// 租赁 / 养护 / 零售 三个域共用的执行器：它们都落在 GetCommonOrders 上，
    /// 差别只有业务类型、可用筛选字段和门店能力位，全部来自注册表。
    /// </summary>
    public abstract class OrderQueryExecutor : IAdminAssistantQueryExecutor
    {
        private readonly ApplicationDBContext _db;
        private readonly IConfiguration _config;
        private readonly IHttpContextAccessor _httpContextAccessor;

        protected OrderQueryExecutor(
            ApplicationDBContext db,
            IConfiguration config,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _config = config;
            _httpContextAccessor = httpContextAccessor;
        }

        public abstract string actionType { get; }

        public async Task<AdminAssistantQueryExecution> ExecuteAsync(
            AdminAssistantQueryState state,
            AdminAssistantDomain domain,
            IReadOnlyCollection<string> metrics,
            IReadOnlyList<string> groupBy,
            CancellationToken cancellationToken)
        {
            await RequireShopSupportsDomain(state.shop, domain, cancellationToken);

            OrderController orderController = new(_db, _config, _httpContextAccessor);
            List<Order> orders = await orderController.GetCommonOrders(
                null, state.shop, null, null, domain.bizType, state.start_date, state.end_date,
                null, state.is_test, state.is_entertain, null, null, state.have_discount,
                null, null, null, null, state.retail_type, state.keyword, state.is_summer_care,
                null, null, state.use_card, state.cell_suffix, state.rent_status, state.has_retail);
            orders = OrderQueryRules.FilterByCustomerCellSuffix(orders, state.cell_suffix);

            List<AssistantAggregateRow> rows = orders.Select(order => new AssistantAggregateRow(
                order.id,
                order.shop,
                order.biz_date,
                order.rentProperties?.rentStatus ?? "临时订单",
                order.totalCharge,
                order.paidAmount,
                order.refundAmount,
                order.paidAmount < order.totalCharge && order.closed == 0)).ToList();

            return new AdminAssistantQueryExecution(state, AdminAssistantSummary.Build(rows, domain, metrics, groupBy));
        }

        private async Task RequireShopSupportsDomain(
            string? shop, AdminAssistantDomain domain, CancellationToken cancellationToken)
        {
            if (shop == null || domain.shopCapability == null) return;
            bool supported = domain.shopCapability switch
            {
                "rent" => await _db.shop.AsNoTracking().AnyAsync(item => item.rent == 1 && item.name == shop, cancellationToken),
                "care" => await _db.shop.AsNoTracking().AnyAsync(item => item.care == 1 && item.name == shop, cancellationToken),
                "sale" => await _db.shop.AsNoTracking().AnyAsync(item => item.sale == 1 && item.name == shop, cancellationToken),
                _ => true
            };
            if (!supported) throw new InvalidOperationException(domain.label + "门店不支持");
        }
    }

    public sealed class RentalOrderQueryExecutor : OrderQueryExecutor
    {
        public RentalOrderQueryExecutor(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor accessor)
            : base(db, config, accessor) { }

        public override string actionType => "rental_order.query";
    }

    public sealed class CareOrderQueryExecutor : OrderQueryExecutor
    {
        public CareOrderQueryExecutor(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor accessor)
            : base(db, config, accessor) { }

        public override string actionType => "care_order.query";
    }

    public sealed class RetailOrderQueryExecutor : OrderQueryExecutor
    {
        public RetailOrderQueryExecutor(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor accessor)
            : base(db, config, accessor) { }

        public override string actionType => "retail_order.query";
    }
}
