using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models
{
    [Table("mini_session")]
    public class MiniSession
    {
        public string session_key { get; set; }
        public string session_type { get; set; } = "";
        public int valid {get; set;} = 0;
        public DateTime expire_date { get; set; }
        public int? member_id {get; set;} = null;
        // 暂存当前 session 关联的微信 openid + unionid。
        // MemberLogin 不再自动建 stub member 后,未注册 user 的 openid/unionid 仍记录在这里,
        // 等点支付按钮时再由 PaymentIdentityController 用这俩字段建会员或绑给已有 phoneOwner。
        public string? wechat_openid { get; set; } = null;
        public string? wechat_unionid { get; set; } = null;
        public string? alipay_payerid { get; set; } = null;
        public string? cell { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("member_id")]
        public Member? member {get; set;}
    }
}
