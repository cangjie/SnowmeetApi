using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;

namespace SnowmeetApi.Models
{
    [Table("care")]
    public class Care
    {
        [Key]
        public int id { get; set; }
        public int? order_id { get; set; }
        public string? biz_type { get; set; }
        public string equipment { get; set; }
        public string brand { get; set; }
        public string? series { get; set; }
        public string scale { get; set; }
        public string? year { get; set; }
        public string? images { get; set; }
        public int urgent { get; set; }
        public string? foot_length { get; set; }
        public string? height { get; set; }
        public string? weight { get; set; }
        public string? gap { get; set; }
        public string? board_front { get; set; }
        public string? front_din { get; set; }
        public string? rear_din { get; set; }
        public string? left_angle { get; set; }
        public string? right_angle { get; set; }
        public string? serials { get; set; }
        public string? edge_degree { get; set; }
        public int need_edge { get; set; }
        public int need_vax { get; set; }
        public int need_unvax { get; set; }
        public int need_repair { get; set; }
        public string? repair_memo { get; set; }
        public double repair_charge { get; set; }
        public double common_charge { get; set; }
        public string? ticket_code { get; set; }
        public double ticket_discount { get; set; }
        public double discount { get; set; }
        public int finish { get; set; }
        public DateTime? member_pick_date { get; set; }
        public string? veri_code { get; set; }
        public DateTime? veri_code_time { get; set; }
        public string? memo { get; set; }
        public string? task_flow_code { get; set; }
        public int valid { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }
        [ForeignKey("order_id")]
        public Order? order {get; set;}
        [NotMapped]
        public string description
        {
            get
            {
                string desc = "";
                if (edge_degree != null && need_vax == 1)
                {
                    desc += "双项";
                }
                else if (edge_degree != null || need_vax == 1)
                {
                    desc += "单项";
                }
                if (repair_memo != null)
                {
                    desc += repair_memo.Trim();
                }
                if (desc.Trim().Equals(""))
                {
                    return "无";
                }
                else
                {
                    return desc.Trim();
                }
            }
        }
        
        public List<CareTask> tasks { get; set; } = new List<CareTask>();
        [NotMapped]
        public string? currentStep
        {
            get
            {
                if (tasks == null || tasks.Count == 0)
                {
                    return null;
                }
                return tasks[tasks.Count - 1].task_name.Trim();
            }
        }
        [NotMapped]
        public string? status
        {
            get 
            {
                if (currentStep == null)
                {
                    return "未开始";
                }
                if (currentStep.Trim().Equals("发板") || currentStep.Trim().Equals("强行索回"))
                {
                    return "已完成";
                }
                else
                {
                    return "进行中";
                }
            }
        }
    }
}