#nullable enable
using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbCategory
{
    public int id { get; set; }
    public int? parent_id { get; set; }
    public byte level { get; set; }
    public string name { get; set; } = "";
    public string? batch_code { get; set; }
    public string? measure_type { get; set; }
    public string? default_storage { get; set; }
    public int? warn_days { get; set; }
    public int? open_days { get; set; }
    public bool is_prepared { get; set; }
    public int sort { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbItem
{
    public int id { get; set; }
    public int category_id { get; set; }
    public string name { get; set; } = "";
    public string item_type { get; set; } = "raw";
    public string base_unit_code { get; set; } = "";
    public int? warn_days { get; set; }
    public int? open_days { get; set; }
    public decimal? low_stock_ratio { get; set; }
    public decimal? low_stock_qty { get; set; }
    public int? image_id { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbItemForm
{
    public int id { get; set; }
    public int item_id { get; set; }
    public int seq { get; set; }
    public string name { get; set; } = "";
    public string unit_name { get; set; } = "";
    public decimal per_base { get; set; }
    public string storage_type { get; set; } = "";
    public int? shelf_after_op_days { get; set; }
    public string form_code { get; set; } = "";
    public string? in_op_name { get; set; }
    public decimal? in_op_ratio { get; set; }
    public decimal? in_op_yield { get; set; }
    public decimal? in_op_hours { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbPurchaseSpec
{
    public int id { get; set; }
    public int item_id { get; set; }
    public int entry_form_id { get; set; }
    public string name { get; set; } = "";
    public string? brand { get; set; }
    public string? pack_desc { get; set; }
    public string? barcode { get; set; }
    public bool valid { get; set; }
    public int sort { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbBatch
{
    public int id { get; set; }
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public int form_id { get; set; }
    public string batch_no { get; set; } = "";
    public string state { get; set; } = "";
    public decimal quantity { get; set; }
    public decimal amount { get; set; }
    public decimal? pack_size { get; set; }
    public string? pack_label { get; set; }
    public string storage_type { get; set; } = "";
    public DateTime? production_date { get; set; }
    public DateTime expire_date { get; set; }
    public DateTime? op_date { get; set; }
    public DateTime? op_expire_date { get; set; }
    public DateTime effective_expire { get; set; }
    public string expiry_source { get; set; } = "manual";
    public int? parent_batch_id { get; set; }
    public DateTime? ready_at { get; set; }
    public int? spec_id { get; set; }
    public DateTime received_at { get; set; }
    public string? dispose_status { get; set; }
    public byte[] row_version { get; set; } = Array.Empty<byte>();
}

public sealed class FnbBatchImage
{
    public int batch_id { get; set; }
    public int upload_id { get; set; }
}

public sealed class FnbStockOperation
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long id { get; set; }
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long document_id { get; set; }
    public int item_id { get; set; }
    public int from_form_id { get; set; }
    public int to_form_id { get; set; }
    public int source_batch_id { get; set; }
    public int output_batch_id { get; set; }
    public string op_name { get; set; } = "";
    public decimal input_qty { get; set; }
    public decimal std_ratio { get; set; }
    public decimal std_yield { get; set; }
    public decimal expected_qty { get; set; }
    public decimal actual_qty { get; set; }
    public decimal loss_base_qty { get; set; }
    public decimal duration_hours { get; set; }
    public DateTime? ready_at { get; set; }
    public string status { get; set; } = "";
    public DateTime? completed_at { get; set; }
    public int staff_id { get; set; }
}

public sealed class FnbStockView
{
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public decimal available_qty { get; set; }
    public decimal staged_qty { get; set; }
    public decimal sealed_qty { get; set; }
    public decimal total_qty { get; set; }
    public decimal total_amount { get; set; }
    public decimal? average_unit_cost { get; set; }
}

public sealed class FnbLossView
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)] public long source_id { get; set; }
    public string source_type { get; set; } = "";
    public int shop_id { get; set; }
    public int item_id { get; set; }
    public DateTime business_date { get; set; }
    public decimal loss_qty { get; set; }
    public decimal loss_amount { get; set; }
}
