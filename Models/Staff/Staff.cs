using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("staff")]
    public class Staff
    {
        [Key]
        public int id { get; set; }
        public string name { get; set; } = "";
        public string gender {get; set;} = "";
        public int title_level { get; set; } = 0;
        public int valid {get; set;}
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}