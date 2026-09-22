// Generated from the applied 2026-09-22 VARCHAR schema; edit with the SQL contract in view.
using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbMaterialBatchStock
{
    public int batch_id { get; set; }
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public string stock_form { get; set; } = string.Empty;
    public string storage_type { get; set; } = string.Empty;
    public string? storage_location { get; set; }
    public decimal quantity { get; set; }
    public decimal stock_amount { get; set; }
    public decimal? pack_size { get; set; }
    public string? pack_unit_name { get; set; }
    public decimal? sealed_pack_count { get; set; }
    public int? parent_batch_id { get; set; }
    public DateTime? opened_date { get; set; }
    public DateTime original_expire_date { get; set; }
    public DateTime? opened_expire_date { get; set; }
    public string? open_storage_type { get; set; }
    public int? open_shelf_life_days { get; set; }
    public int? shelf_life_rule_id { get; set; }
    public DateTime? calculated_expire_date { get; set; }
    public string expiry_source { get; set; } = string.Empty;
    public string? expiry_note { get; set; }
    public bool is_destroyed { get; set; }
    public DateTime received_at { get; set; }
    public DateTime? updated_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}

public sealed class FnbStockDocument
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    public int shop_id { get; set; }
    public string document_no { get; set; } = string.Empty;
    public string document_type { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public Guid request_id { get; set; }
    public string source_client { get; set; } = string.Empty;
    public DateTime business_date { get; set; }
    public DateTime occurred_at { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? order_id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? recipe_id { get; set; }
    public string? reason_code { get; set; }
    public string? reference_no { get; set; }
    public string? remark { get; set; }
    public int? created_by_staff_id { get; set; }
    public int? posted_by_staff_id { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? posted_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}

public sealed class FnbStockDocumentLine
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long document_id { get; set; }
    public int shop_id { get; set; }
    public int line_no { get; set; }
    public int item_id { get; set; }
    public string item_name { get; set; } = string.Empty;
    public short direction { get; set; }
    public decimal input_qty { get; set; }
    public string input_unit_name { get; set; } = string.Empty;
    public decimal input_to_base { get; set; }
    public decimal planned_qty { get; set; }
    public decimal actual_qty { get; set; }
    public decimal shortage_qty { get; set; }
    public decimal? input_unit_price { get; set; }
    public decimal actual_amount { get; set; }
    public int? specified_batch_id { get; set; }
    public string? remark { get; set; }
}

public sealed class FnbStockMovement
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long document_line_id { get; set; }
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public int batch_id { get; set; }
    public short direction { get; set; }
    public decimal quantity { get; set; }
    public decimal amount { get; set; }
    public decimal delta_qty { get; set; }
    public decimal delta_amount { get; set; }
    public decimal balance_qty { get; set; }
    public decimal balance_amount { get; set; }
    public DateTime created_at { get; set; }
}

public sealed class FnbStocktakeLine
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long document_id { get; set; }
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public decimal system_qty { get; set; }
    public decimal? counted_qty { get; set; }
    public decimal? difference_qty { get; set; }
    public DateTime snapshot_at { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? snapshot_last_movement_id { get; set; }
    public byte[] snapshot_fingerprint { get; set; } = Array.Empty<byte>();
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? adjustment_line_id { get; set; }
    public int? counted_by_staff_id { get; set; }
    public DateTime? counted_at { get; set; }
    public string? remark { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}
