namespace SnowmeetApi.Models;

// 查单响应的可选信息，不映射数据库表或字段。
public class CareCardSaleInfo
{
    public int careOrderId { get; set; }
    public int? retailOrderId { get; set; }
    public bool completed { get; set; }
    public decimal cardAmount { get; set; }
    public decimal careAmount { get; set; }
}
