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
        [ForeignKey("member_id")]
        public Member ownerMember { get; set; }
        [ForeignKey("template_id")]
        public TicketTemplate template{get; set;}
    }
}
