using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aop.Api.Domain;
namespace SnowmeetApi.Models
{
    [Table("core_data_mod_log")]
    public class CoreDataModLog
    {
        [Key]
        public int id { get; set; }
        public string table_name {get; set;}
        public string field_name {get; set;}
        public string key_value {get; set;}
        public string scene {get; set;}
        public int? member_id {get; set;}
        public int? staff_id {get; set;}
        public string? prev_value {get; set;}
        public string? current_value {get; set;}
        public long trace_id {get; set;}
        public DateTime create_date {get; set;} = DateTime.Now;
        [ForeignKey("staff_id")]
        public Staff staff {get; set;}
        [NotMapped]
        public string simpleMemo 
        {
            get
            {
                switch(table_name)
                {
                    case "retail":
                        switch(field_name)
                        {
                            case "order_type":
                                return "修改订单类型";
                            case "mi7_code":
                                return "修改七色米订单号";
                            default:
                                break;
                        }
                        break;
                    default:
                        break;
                }
                return "";
            }
        }

    }
}