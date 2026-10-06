#nullable enable
using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbArea
{
    public int id { get; set; }
    public int shop_id { get; set; }
    public int? parent_id { get; set; }
    public string name { get; set; } = "";
    public string area_type { get; set; } = "other";
    public bool valid { get; set; }
    public int sort { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}
public sealed class FnbAreaImage
{
    public int area_id { get; set; }
    public int upload_id { get; set; }
    public int staff_id { get; set; }
    public DateTime created_at { get; set; }
}
// Extension table keeps the already published batch schema intact.
public sealed class FnbBatchDetail
{
    public int batch_id { get; set; }
    public int? area_id { get; set; }
    public string? open_storage { get; set; }
    public int? open_days { get; set; }
}
public sealed class FnbRequest
{
    public int shop_id { get; set; }
    public string action { get; set; } = "";
    public Guid request_id { get; set; }
    public string payload_hash { get; set; } = "";
    public string response_json { get; set; } = "";
    public int staff_id { get; set; }
    public DateTime created_at { get; set; }
}
public sealed class FnbSupply
{
    public int id { get; set; }
    public int shop_id { get; set; }
    public string name { get; set; } = "";
    public string supply_type { get; set; } = "disposable";
    public string? spec { get; set; }
    public string pack_label { get; set; } = "包";
    public int pack_size { get; set; }
    public int? area_id { get; set; }
    public decimal quantity { get; set; }
    public decimal last_receipt_qty { get; set; }
    public decimal? low_stock_ratio { get; set; }
    public decimal? low_stock_qty { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}
public sealed class FnbSupplyMovement
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long id { get; set; }
    public int supply_id { get; set; }
    public Guid request_id { get; set; }
    public string movement_type { get; set; } = "";
    public decimal input_qty { get; set; }
    public decimal quantity { get; set; }
    public decimal balance_qty { get; set; }
    public string? reason { get; set; }
    public bool cancelled { get; set; }
    public int staff_id { get; set; }
    public DateTime created_at { get; set; }
}
public sealed class FnbTool
{
    public int id { get; set; }
    public int shop_id { get; set; }
    public string name { get; set; } = "";
    public string asset_no { get; set; } = "";
    public string? spec { get; set; }
    public int quantity { get; set; }
    public int? area_id { get; set; }
    public string status { get; set; } = "normal";
    public int? owner_staff_id { get; set; }
    public bool daily_check { get; set; }
    public DateTime? last_check_date { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}
public sealed class FnbToolLog
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long id { get; set; }
    public int tool_id { get; set; }
    public string from_status { get; set; } = "";
    public string to_status { get; set; } = "";
    public int? from_area_id { get; set; }
    public int? to_area_id { get; set; }
    public string? remark { get; set; }
    public int staff_id { get; set; }
    public DateTime created_at { get; set; }
}
public sealed class FnbCheckItem
{
    public int id { get; set; }
    public int area_id { get; set; }
    public string name { get; set; } = "";
    public string kind { get; set; } = "environment";
    public string method { get; set; } = "yes_no";
    public bool required { get; set; }
    public bool photo_suggested { get; set; }
    public string? unit { get; set; }
    public decimal? minimum { get; set; }
    public decimal? maximum { get; set; }
    public int? tool_id { get; set; }
    public int? supply_id { get; set; }
    public bool valid { get; set; }
    public DateTime updated_at { get; set; }
}
public sealed class FnbCheckSheet
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long id { get; set; }
    public int shop_id { get; set; }
    public DateTime business_date { get; set; }
    public string status { get; set; } = "in_progress";
    public string fingerprint { get; set; } = "";
    public int started_by { get; set; }
    public DateTime started_at { get; set; }
    public DateTime? saved_at { get; set; }
    public int? submitted_by { get; set; }
    public DateTime? submitted_at { get; set; }
    public int? confirmed_by { get; set; }
    public DateTime? confirmed_at { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}
public sealed class FnbCheckLine
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long sheet_id { get; set; }
    public int item_id { get; set; }
    public string snapshot_json { get; set; } = "";
    public bool active { get; set; }
    public string? result { get; set; }
    public decimal? value { get; set; }
    public string? reason { get; set; }
    public int? upload_id { get; set; }
    public bool bulk { get; set; }
    public int? staff_id { get; set; }
    public DateTime? updated_at { get; set; }
}
public sealed class FnbCheckHandling
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long line_id { get; set; }
    public string remark { get; set; } = "";
    public int staff_id { get; set; }
    public DateTime created_at { get; set; }
}
public sealed class FnbAlertDelivery
{
    public int batch_id { get; set; }
    public DateTime business_date { get; set; }
    public string status { get; set; } = "pending";
    public DateTime attempted_at { get; set; }
    public string receivers { get; set; } = "";
    public string? message_id { get; set; }
    public string? error { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}
