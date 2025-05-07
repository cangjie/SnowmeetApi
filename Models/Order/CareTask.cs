using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("care_task")]
    public class CareTask
    {
        [Key]
        public int id {get; set;}
        public int care_id {get; set;}
        public string task_name {get; set;}
        public string memo {get; set;} = "";
        public DateTime? start_time {get; set;} = null;
        public DateTime? end_time {get; set;} = null;
        public string status {get; set;} = "未开始";
        public int? staff_id {get; set;}
        public int? terminate_staff_id {get; set;}
        public int? member_id {get; set;}
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
    }
}