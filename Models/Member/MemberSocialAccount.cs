using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("member_social_account")]
    public class MemberSocialAccount
    {
        // 第三方 ID 类型常量（与 type 字段对照）
        public const string TYPE_WECHAT_MINI_OPENID = "wechat_mini_openid";
        public const string TYPE_WECHAT_UNIONID = "wechat_unionid";
        public const string TYPE_CELL = "cell";
        public const string TYPE_ALIPAY_PAYERID = "alipay_payerid";
        public const string TYPE_WECOM = "wecom";

        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        public string type {get; set;}
        public string num { get; set; }
        public int valid {get; set;} = 1;
        public string memo {get; set; } = "";
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("member_id")]
        public Member member { get; set; }

        //will delete
        public List<OrderOnline> orders { get; set; } = new List<OrderOnline>();

    }
}