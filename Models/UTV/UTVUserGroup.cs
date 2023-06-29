using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.UTV
{
    [Table("utv_user_group")]
    public class UTVUserGroup
    {
        [Key]
        public int id { get; set; }
        public int host_id { get; set; }
        public int guest_id { get; set; }
    }
}
