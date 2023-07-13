using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Maintain
{
    [Table("maintain_log")]
    public class MaintainLog
    {
        [Key]
        public int id { get; set; }
        public int task_id { get; set; }
        public string step_name { get; set; }
        public string memo { get; set; }
        public DateTime start_time { get; set; }
        public DateTime? end_time { get; set; }
        public string staff_open_id { get; set; }
        public string status { get; set; }
        public string stop_open_id { get; set; }

        [NotMapped]
        public bool isMine { get; set; } = true;
        [NotMapped]
        public string staffName { get; set; } = "";
       
    }
}
