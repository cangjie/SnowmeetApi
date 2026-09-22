// Generated from the applied 2026-09-22 VARCHAR schema; edit with the SQL contract in view.
using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbOrder
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    public int shop_id { get; set; }
    public string source_type { get; set; } = string.Empty;
    public int? sales_order_id { get; set; }
    public int? channel_shop_id { get; set; }
    public string? external_order_no { get; set; }
    public string display_no { get; set; } = string.Empty;
    public DateTime business_date { get; set; }
    public DateTime ordered_at { get; set; }
    public string? table_no { get; set; }
    public string order_status { get; set; } = string.Empty;
    public string? platform_status { get; set; }
    public string refund_status { get; set; } = "none";
    public decimal? total_amount { get; set; }
    public decimal? refund_amount { get; set; }
    public string review_status { get; set; } = string.Empty;
    public string? remark { get; set; }
    public DateTime? source_updated_at { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}

public sealed class FnbOrderLine
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long order_id { get; set; }
    public int shop_id { get; set; }
    public string line_key { get; set; } = string.Empty;
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? parent_line_id { get; set; }
    public int? legacy_fd_order_id { get; set; }
    public string option_key { get; set; } = string.Empty;
    public string item_name { get; set; } = string.Empty;
    public string? spec_name { get; set; }
    public string? options_text { get; set; }
    public decimal quantity { get; set; }
    public decimal cancelled_qty { get; set; }
    public bool is_inventory_line { get; set; }
    public int? dish_spec_id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? recipe_id { get; set; }
    public string? remark { get; set; }
}
