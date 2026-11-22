using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace wechat_miniapp_base.Models
{
    [Table("wepay_key")]
    public class WepayKey
    {
        public int id { get; set; }
        public string mch_id { get; set; }
        public string mch_name { get; set; }
        public string key_serial { get; set; }
        public string private_key { get; set; }
        public string api_key { get; set; }
        public int valid { get; set; }
        public string? cert {get; set;} = null;
        // JSON array: [{"serial_no":"...","certificate":"-----BEGIN CERTIFICATE-----..."}].
        // Keep overlapping platform certificates together during WeChat certificate rotation.
        public string? platform_certificates { get; set; } = null;
        public string? doc_id {get; set;} = null;
        // 微信支付公钥模式（商户平台「微信支付公钥」，新开的商户号只能用这个，没有平台证书）：
        // pub_key_id 形如 PUB_KEY_ID_xxx，回调头 Wechatpay-Serial 带的就是它；pub_key 是 PEM（BEGIN PUBLIC KEY）。
        // 两个都配了，回调序列号等于 pub_key_id 时用公钥验签；其它序列号仍按平台证书（platform_certificates / cert）。
        public string? pub_key_id { get; set; } = null;
        public string? pub_key { get; set; } = null;
    }
}
