using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("ticket")]
    public class Ticket
    {
        [Key]
        public string code { get; set; }
        public string name { get; set; }
        public string? biz_type {get; set;}
        public int? biz_id {get; set;}
        public string? memo { get; set; }
        public string open_id { get; set; }
        public string? oper_open_id { get; set; } = null;
        public int? member_id { get; set; }
        public int? staff_id {get; set;}
        public int shared { get; set; }
        public DateTime? shared_time { get; set; }
        public int printed { get; set; }
        public int used { get; set; }
        public DateTime? used_time { get; set; }
        public int template_id { get; set; }
        public string? miniapp_recept_path { get; set; } = null;
        public DateTime create_date { get; set; }
        public string channel { get; set; } = "";
        public DateTime? start_date { get; set; } = null;
        public DateTime? expire_date { get; set; } = null;
        public string? create_memo { get; set; } = "";
        public int? order_id { get; set; }
        public DateTime accepted_time { get; set; } = DateTime.Now;
        public string? use_memo { get; set; } = "";
        public int is_active { get; set; } = 1;
        public int valid { get; set; } = 0;
        //public int? staff_id {get; set;}

        [NotMapped]
        public string status
        {
            get
            {
                string status = "";
                if (used == 1)
                {
                    status = "已使用";
                }
                else if (shared == 1)
                {
                    status = "分享中";
                }
                else
                {
                    status = "未使用";
                }
                return status;

            }
        }
        // 转赠关注校验用的场景值：绑定在"这一次分享"（shared_time）上，不是绑定在券本身，
        // 避免同一张券换收件人转赠时，复用到别人之前留下的扫码/关注记录（2026-08-12 踩坑修复）
        [NotMapped]
        public string transfer_scene
        {
            get
            {
                if (shared_time == null)
                {
                    return null;
                }
                return "ticket_gift_" + code.Trim() + "_" + shared_time.Value.Ticks;
            }
        }
        // 「这张券已经转赠出去、不在我名下了」——只由 GetMySharedTickets 给"已分享"列表里
        // 那批已被对方接受的券置位，前端据此显示"对方已接受"。
        // 不能让前端拿 shared==1 反推"还在我名下、分享中"：shared 只描述券自己的状态，
        // 不描述归属。两种情况都会出现"券不是我的、shared 却是 1"：
        //   1. 对方接受后又转赠给了第三个人（他那次分享把 shared 置回了 1）；
        //   2. 2019~2023 旧转赠流程接受时没复位 shared 的历史数据。
        // 此时前端若显示成"分享中"并给出撤回按钮，点了必然被 CancelShare 的归属校验拒掉。
        [NotMapped]
        public bool transferredOut { get; set; } = false;

        // 卡片上那行时间，由各列表接口按所在 tab 算好下发（见 TicketTransferRules.ResolveDisplayTime）。
        // 后端直接给格式化好的字符串，前端不做任何日期解析——iOS 上
        // new Date('2026-08-16 10:30:00') 会得到 Invalid Date。
        [NotMapped]
        public string displayTimeLabel { get; set; } = "";
        [NotMapped]
        public string displayTimeText { get; set; } = "";

        // 券列表卡片用：左侧票根的面额（取自模板）、有效期文案、是否临期。
        // 有效期同样由服务端格式化好下发，前端不做日期解析。
        [NotMapped]
        public double currencyValue { get; set; } = 0;
        [NotMapped]
        public string expireText { get; set; } = "";
        [NotMapped]
        public bool expireUrgent { get; set; } = false;

        [ForeignKey("member_id")]
        public Member ownerMember { get; set; }
        [ForeignKey("template_id")]
        public TicketTemplate template{get; set;}
    }
}
