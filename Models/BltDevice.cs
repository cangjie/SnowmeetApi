using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("blt_device")]
    public class BltDevice
    {
        [Key]
        public int id { get; set; }

        public string name { get; set; }
        public string device_name { get; set; }
        public string device_name_2 { get; set; }
        public int admin_only { get; set; }
        public int need_points { get; set; }
    }
}
