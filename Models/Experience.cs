using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("expierence_list")]
    public class Experience
    {
        [Key]
        public int id { get; set; }

        public string shop { get; set; }
        public double guarantee_cash { get; set; }
        public int guarantee_order_id { get; set; }
        public string cell_number { get; set; }
        public string open_id { get; set; }
        
    }
}
