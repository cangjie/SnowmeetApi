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
    // 2026-09-24 起二级分类只保留建议储存方式；计量、临期、开封默认和保质期规则都在食材上
    public string? default_storage { get; set; }
    public int sort { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}

public sealed class FnbShelfLifeRule
{
    public int id { get; set; }
    public int? item_id { get; set; }        // 新规则只挂食材
    public int? category_id { get; set; }    // 仅 2026-09-24 前的历史分类规则（均已停用，旧批次仍引用）
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
    public int warn_days { get; set; } = 1;              // 临期提前提醒天数，入库时复制到批次
    public string? default_open_storage { get; set; }    // 封装品开封后默认储存
    public int? default_open_days { get; set; }          // 封装品开封后默认天数；空=入库时再填
    public int? image_id { get; set; }
    public string? remark { get; set; }
    public bool valid { get; set; }
    public DateTime created_at { get; set; }
    public DateTime? updated_at { get; set; }
}
