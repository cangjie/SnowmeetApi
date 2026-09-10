using System;
using System.Collections.Generic;
using System.Linq;
using SnowmeetApi.Models;

namespace SnowmeetApi.Helpers
{
    public static class OrderQueryRules
    {
        public static List<Order> FilterByCustomerCellSuffix(IEnumerable<Order> orders, string? cellSuffix)
        {
            string? normalized = string.IsNullOrWhiteSpace(cellSuffix) ? null : cellSuffix.Trim();
            if (normalized == null)
            {
                return orders.ToList();
            }
            return orders.Where(order => order.customerCell.EndsWith(normalized, StringComparison.Ordinal)).ToList();
        }
    }
}
