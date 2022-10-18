using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("mini_upload")]
    public class UploadFile
    {
        [Key]
        public int id { get; set; }
        public string owner { get; set; }
        public string file_path_name { get; set; }
        public int is_web { get; set; } = 1;
        public string purpose { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}

