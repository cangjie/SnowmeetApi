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
        public bool use_card {get; set;} = false;
        // 所选会员卡（2026-07-09 加 DB 列）：卡选择跟着单件装备（care）走，
        // 随 SaveCareRecept 草稿持久化，中断找回可还原
        public int? card_id { get; set; } = null;
        public string? card_name { get; set; } = null;
        // 取消发板（2026-07-14 加 DB 列）：详情页「取消」入口走发板同款核销流程，
        // 核验通过后 SetTaskStatus 置位（跳过完成赠券），供列表/报表识别本件未真正完成
        public bool is_cancel { get; set; } = false;
        public string? cancel_reason { get; set; } = null;
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
                // 任务链在 EffectCareOrder 生效时一次性全量创建（末条恒为发板），
                // 不能用「最后一条任务的名字」判进度（老语义=工序做一步插一行，已失效），
                // 必须看任务自身的执行状态：发板/强行索回 已完成 → 已完成；任一任务动过 → 进行中；否则 未开始
                var validTasks = tasks == null ? null : tasks.Where(t => t.valid == 1).ToList();
                if (validTasks == null || validTasks.Count == 0)
                {
                    return "未开始";
                }
                var finishTask = validTasks.FirstOrDefault(t => t.task_name != null
                    && (t.task_name.Trim().Equals("发板") || t.task_name.Trim().Equals("强行索回")));
                if (finishTask != null && finishTask.status != null && finishTask.status.Trim().Equals("已完成"))
                {
                    return "已完成";
                }
                if (validTasks.Any(t => t.status != null && !t.status.Trim().Equals("未开始")))
                {
                    return "进行中";
                }
                return "未开始";
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