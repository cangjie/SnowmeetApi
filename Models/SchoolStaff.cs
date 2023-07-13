using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("school_staff")]
    public class SchoolStaff
    {
        public SchoolStaff()
        {
        }
        [Key]
        public string open_id { get; set; }
        public string name { get; set; }
        public string role { get; set; }
        public string head_image { get; set; }
        public DateTime create_date { get; }

    }
}
