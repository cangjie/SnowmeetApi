using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.UTV
{
    [Table("utv_reserve")]
    public class UTVReserve
    {
        [Key]
        public int id { get; set; }
        public int utv_user_id { get; set; }
        public int trip_id { get; set; }
        public int vehicle_num { get; set; }
        public string line_type { get; set; }

        public string cell { get; set; } = "";

        public string real_name { get; set; } = "";
        public string status { get; set; } = "待确认";
        public string source { get; set; } = "";

        public string memo { get; set; } = "";
        public int order_id { get; set; } = 0;

        [NotMapped]
        public DateTime trip_date { get; set; }
        [NotMapped]
        public string trip_name { get; set; }
    }
}
