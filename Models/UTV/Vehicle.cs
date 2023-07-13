using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.UTV
{
    [Table("utv_vehicles")]
    public class Vehicle
    {
        [Key]
        public int id { get; set; }
        public string name { get; set; }
        public int valid { get; set; }
        public DateTime update_date { get; set; }

    }
}
