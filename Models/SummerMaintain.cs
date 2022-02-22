using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("summer_maintain")]
    public class SummerMaintain
    {
        [Key]
        public int id { get; set; }

        public string open_id { get; set; } = "";

        public string equip_type { get; set; } = "";
        public string brand { get; set; } = "";
        public string scale { get; set; } = "";
        public string binder_brand { get; set; } = "";
        public string binder_color { get; set; } = "";
        public string send_item { get; set; } = "";
        public string wanlong_no { get; set; } = "";
        public string associates { get; set; } = "";
        public string keep { get; set; } = "";
        public string contact_name { get; set; } = "";
        public string address { get; set; } = "";
        public string cell { get; set; } = "";
        public string service { get; set; } = "";
        public string code { get; set; } = "";
        public int order_id { get; set; } = 0;

        public string source { get; set; } = "";
        public string waybill_no { get; set; } = "";
        public string state { get; set; } = "";
        public string images { get; set; } = "";
        public string owner_name { get; set; } = "";
        public string owner_cell { get; set; } = "";
        public string oper_open_id { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}
