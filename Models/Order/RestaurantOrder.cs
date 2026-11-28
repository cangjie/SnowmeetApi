using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

[Table("restaurant_order")]
public class RestaurantOrder
{
    public const string OrderSource = "restaurant_reception";
    [Key] public int id { get; set; }
    public int order_id { get; set; }
    [MaxLength(30)] public string table_no { get; set; } = "";
    public decimal discount_amount { get; set; } = 0;
    [MaxLength(36)] public string request_id { get; set; } = "";
    public DateTime create_date { get; set; } = DateTime.Now;
}

public class RestaurantOrderRequest
{
    public string requestId { get; set; } = "";
    public string tableNo { get; set; } = "";
    public decimal amount { get; set; }
    public decimal discount { get; set; } = 0;
    public int? memberId { get; set; }
    public string? customerName { get; set; }
    public string? customerCell { get; set; }
    public string? gender { get; set; }
    public int? receptCheckinId { get; set; }
    public bool isTest { get; set; }
}
