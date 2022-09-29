using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Maintain
{
    [Table("brand_serial")]
    public class Serial
    {
        [Key]
        public int id { get; set; }

        public string type { get; set; }
        public string brand_name { get; set; }
        public string serial_name { get; set; }
    }
}

