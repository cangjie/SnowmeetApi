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
        public string status { get; set; }
        public string source { get; set; } = "";
    }
}
