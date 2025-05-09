using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("brand_series")]
    public class Series
    {
        [Key]
        public int id { get; set; }
        public string type { get; set; }
        public string brand_name { get; set; }
        public string serial_name { get; set; }
        public int? staff_id { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

