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
        // 封板（2026-11-09 加 DB 列）：热蜡后不刮蜡，把蜡留在板面上封存过季。它不是养护工序——不生成任务、
        // 不计费——只是个标志：取板（发板完成）后给顾客发一张「开板刮蜡券」（ticket_template 20），开板时凭券刮蜡。
        // 与 need_unwax 二选一：有热蜡时「刮蜡」和「封板」必选其一（默认刮蜡）；没有热蜡、非雪季养护、机打蜡不能封板。
        // 规则集中在 Helpers/CareSealRules.cs。
        public int seal_board { get; set; } = 0;
        // 养板（2026-11-10 加 DB 列）：和热蜡同一类业务、与热蜡互斥、可互相替换。工序为
        // 养板打蜡1 → 养板刮蜡1 → 养板打蜡2 → 养板刮蜡2 → 养板打蜡3（EffectCareOrder 生效时生成）。
        // 和热蜡一样：选了就默认带刮蜡，刮蜡与封板必选其一（CareSealRules）。
        // 计费独立于单项/双项：单选养板 220、修刃+养板 280（商品「养板」「修刃养板」），老顾客券（模板 16）减 20 / 30；
        // 只认老顾客券，其余券卡、会员卡、学员卡不能用于养板（CarePricingRules.ClearBenefitsForMaintainBoard）。
        // 与机打蜡互斥，非雪季养护没有养板。
        public int maintain_board { get; set; } = 0;
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
        // 学员卡（某某教练的学员卡，2026-10-30 加 DB 列）：店员在「优惠券/卡」弹层里选的那张，
        // 与 ticket_code / card_id / use_card 互斥；下单时服务端校验并按卡内价格快照算优惠
        public int? coach_card_id { get; set; } = null;
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
                if (maintain_board == 1)
                {
                    desc += edge_degree != null ? "修刃养板" : "养板";
                }
                else if (edge_degree != null && need_wax == 1)
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

        // 列表展示用的金额 / 减免（不入库，由 GetOrdersByStaffPaged 填充，口径见 Helpers/CareAmountRules）：
        // 普通装备 = 库里的值；招待 / 质保装备 = 按所做项目算的标准价，减免到 0 元。没填（价目读取失败）时为 null，客户端退回显示库里的值
        [NotMapped]
        public double? displayCharge { get; set; } = null;
        [NotMapped]
        public double? displayDiscount { get; set; } = null;
        // 列表展示用的次卡信息（不入库，由 GetOrdersByStaffPaged 填充，口径见 Helpers/CareCardInfoRules）：
        // 用了次卡的装备显示「用的哪张卡、还剩几次」；季卡不限次（cardUnlimited），没绑上卡 / 读卡失败时没有剩余次数
        [NotMapped]
        public string? cardDisplayName { get; set; } = null;
        [NotMapped]
        public int? cardRemaining { get; set; } = null;
        [NotMapped]
        public bool cardUnlimited { get; set; } = false;
        // 浅拷贝（只用于按「去掉招待 / 质保标记」的副本重新计价，不会存库）
        public Care ShallowCopy()
        {
            return (Care)MemberwiseClone();
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