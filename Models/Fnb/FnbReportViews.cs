using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbMaterialStockView
{
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public string item_name { get; set; } = string.Empty;
    public int category_id { get; set; }
    public string base_unit_code { get; set; } = string.Empty;
    public decimal total_qty { get; set; }
    public decimal total_amount { get; set; }
    public decimal sealed_qty { get; set; }
    public decimal available_qty { get; set; }
    public decimal expired_qty { get; set; }
    public decimal? average_unit_cost { get; set; }
    public int inconsistent_batch_count { get; set; }
}

public sealed class FnbMaterialLossView
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long movement_id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long document_id { get; set; }
    public string document_no { get; set; } = string.Empty;
    public int shop_id { get; set; }
    public DateTime business_date { get; set; }
    public string? reason_code { get; set; }
    public int item_id { get; set; }
    public string item_name { get; set; } = string.Empty;
    public int batch_id { get; set; }
    public string batch_no { get; set; } = string.Empty;
    public string base_unit_code { get; set; } = string.Empty;
    public decimal delta_qty { get; set; }
    public decimal delta_amount { get; set; }
    public int? posted_by_staff_id { get; set; }
    public DateTime? posted_at { get; set; }
    public string? remark { get; set; }
}
