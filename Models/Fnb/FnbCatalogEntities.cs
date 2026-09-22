// Generated from the applied 2026-09-22 VARCHAR schema; edit with the SQL contract in view.
using System;

namespace SnowmeetApi.Models.Fnb;

public sealed class FnbUnit
{
    public string code { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public byte dimension { get; set; }
    public decimal factor_to_base { get; set; }
    public bool valid { get; set; }
    public int sort { get; set; }
}

public sealed class FnbMaterialCategory
{
    public int id { get; set; }
    public int? parent_id { get; set; }
    public byte level { get; set; }
    public string name { get; set; } = string.Empty;
    public string? default_storage { get; set; }
    public string? default_unit_code { get; set; }
    public int? warn_days { get; set; }
    public string? default_open_storage { get; set; }
    public int? default_open_days { get; set; }
    public int sort { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbShelfLifeRule
{
    public int id { get; set; }
    public int category_id { get; set; }
    public string storage_type { get; set; } = string.Empty;
    public byte production_month { get; set; }
    public int shelf_life_value { get; set; }
    public string shelf_life_unit { get; set; } = string.Empty;
    public string? remark { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbMaterialItem
{
    public int id { get; set; }
    public string code { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public int category_id { get; set; }
    public string item_type { get; set; } = string.Empty;
    public string base_unit_code { get; set; } = string.Empty;
    public string default_input_unit_code { get; set; } = string.Empty;
    public int? image_id { get; set; }
    public string? remark { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}
