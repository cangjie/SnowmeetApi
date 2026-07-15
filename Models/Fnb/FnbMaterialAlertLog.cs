using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.Fnb
{
    /// <summary>
    /// 食材过期提醒发送记录：每次企微推送、每个被提醒的批次一行（同次推送多批次共享 msgid）。
    /// 用途：批次维度「提醒过几次/最后提醒时间」+ 当天已提醒去重（防重复骚扰）+ 推送失败排查。
    /// </summary>
    [Table("fnb_material_alert_log")]
    public class FnbMaterialAlertLog
    {
        [Key]
        public int id { get; set; }
        public int batch_id { get; set; }                       // fnb_material_batch.id
        public string alert_status { get; set; }                // 提醒时点状态快照：临期 / 今日 / 已过期
        public DateTime expire_date { get; set; }               // 到期日快照（批次事后被改/删仍可追溯当时依据）
        public string touser { get; set; } = "@all";            // 企微接收人（@all 或 userid|userid…）
        public string? msgid { get; set; } = null;              // 企微返回 msgid（同次推送多批次共享）
        public int success { get; set; } = 0;                   // 1=企微返回 errcode=0
        public string? err_msg { get; set; } = null;            // 失败时记 errcode+errmsg
        public string? send_userid { get; set; } = null;        // 触发人企微 UserId（定时任务触发=NULL）
        public DateTime create_date { get; set; } = DateTime.Now;
    }
}
