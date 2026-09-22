// Generated from the applied 2026-09-22 VARCHAR schema; edit with the SQL contract in view.
using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbDishSpec
{
    public int id { get; set; }
    public int shop_id { get; set; }
    public int product_id { get; set; }
    public string spec_code { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public decimal? sale_price { get; set; }
    public int? legacy_product_id { get; set; }
    public bool is_default { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbRecipe
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    public int shop_id { get; set; }
    public string recipe_type { get; set; } = string.Empty;
    public int? dish_spec_id { get; set; }
    public int? output_item_id { get; set; }
    public decimal output_qty { get; set; }
    public int version_no { get; set; }
    public string status { get; set; } = string.Empty;
    public string? remark { get; set; }
    public int? created_by_staff_id { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? published_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}

public sealed class FnbRecipeLine
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long recipe_id { get; set; }
    public int item_id { get; set; }
    public decimal quantity { get; set; }
    public int sort { get; set; }
    public string? remark { get; set; }
}
