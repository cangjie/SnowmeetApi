using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
namespace SnowmeetApi.Models
{
    [Table("biz_referee")]
    public class BizReferee
    {
        [Key]
        public int id {get; set;}
        public string biz_type {get; set;}
        public int? order_id {get; set;} = null;
        public int? biz_id {get; set;} = null;
        public int member_id {get; set;}
        public int? staff_id {get; set;}
        public bool valid {get; set;} = true;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
    }
    
}