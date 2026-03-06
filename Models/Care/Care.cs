using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Drawing;
using System.Linq;

namespace SnowmeetApi.Models
{
    [Table("care")]
    public class Care
    {
        public enum SkiService { 修底刃, 补板底, 贴板面, 前固定器, 后固定器, 雪耙 };
        public enum BoardService { 修底刃, 补板底, 贴板面, 固定器, 罗盘, 绑带, 扒扣, 螺丝 };

       
        [Key]
        public int id { get; set; }
        public int? order_id { get; set; }
        public string? biz_type { get; set; }
        public string equipment { get; set; }
        public string? brand { get; set; }
        public string? series { get; set; }
        public string? scale { get; set; }
        public string? year { get; set; }
        public int urgent { get; set; }
        public string? boot_length { get; set; }
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
        public string? others_associates { get; set; } = null;
        public int need_edge { get; set; } = 0;
        public int need_wax { get; set; } = 0;
        public int free_wax {get; set;} = 0;
        public int need_unwax { get; set; } = 0;
        public int need_repair { get; set; } = 0;
        public string? repair_memo { get; set; }
        public double repair_charge { get; set; }
        public double common_charge { get; set; }
        public string? ticket_code { get; set; }
        public double ticket_discount { get; set; }
        public double discount { get; set; }
        public bool? with_pole { get; set; } = null;
        public int finish { get; set; }
        public bool warranty {get; set;} = false;
        public bool entertain {get; set;} = false;
        public DateTime? member_pick_date { get; set; }
        public string? veri_code { get; set; }
        public DateTime? veri_code_time { get; set; }
        public string? memo { get; set; }
        public string? task_flow_code { get; set; }
        public int valid { get; set; }
        public string? summer {get; set;} = null;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("order_id")]
        public Order? order { get; set; }
        [NotMapped]
        public string description
        {
            get
            {
                string desc = "";
                if (edge_degree != null && need_wax == 1)
                {
                    desc += "双项";
                }
                else if (edge_degree != null || need_wax == 1)
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
        public List<CareImage> careImages { get; set; } = new List<CareImage>();
        public int? pick_image_id {get; set;} = null;
        [ForeignKey("pick_image_id")]
        public UploadFile? pickImage {get; set;} = null;
    }
    [Table("care_image")]
    public class CareImage
    {
        [Key]
        public int id { get; set; }
        public int image_id { get; set; }
        public int care_id { get; set; }
        public string title { get; set; } = null;
        public bool valid { get; set; } = true;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("care_id")]
        public Care care { get; set; }
        [ForeignKey("image_id")]
        public UploadFile image { get; set; }
    }
}