using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

[Table("rent_product_unavailability")]
public class RentProductUnavailability
{
    [Key] public int id { get; set; }
    public int rent_product_id { get; set; }
    public DateTime? start_time { get; set; }
    public DateTime? end_time { get; set; }
    public int valid { get; set; } = 1;
    public int? staff_id { get; set; }
    public DateTime create_date { get; set; } = DateTime.Now;
}
