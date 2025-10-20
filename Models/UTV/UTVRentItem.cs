using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.UTV
{
    [Table("utv_rent_item")]
    public class UTVRentItem
    {
        [Key]
        public int id { get; set; }
        public int schedule_id { get; set; }
        public string name { get; set; }
        public int confirm_rent { get; set; } = 0;
        public int returned { get; set; } = 0;
        public string rent_staff { get; set; }
        public string return_staff { get; set; }

    }
}
