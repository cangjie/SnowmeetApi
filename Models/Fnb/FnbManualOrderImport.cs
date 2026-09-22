using System;
using System.Text.Json.Serialization;

namespace SnowmeetApi.Models.Fnb;

// The existing import table also supplies a unique request key for manual kitchen orders.
public sealed class FnbOrderImport
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long id { get; set; }
    public int shop_id { get; set; }
    public string source_method { get; set; } = "internal";
    public string dedupe_key { get; set; } = string.Empty;
    [JsonNumberHandling(JsonNumberHandling.WriteAsString)]
    public long? order_id { get; set; }
    public string process_status { get; set; } = "accepted";
    public int? reviewed_by_staff_id { get; set; }
    public DateTime captured_at { get; set; }
    public DateTime? processed_at { get; set; }
}
