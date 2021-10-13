using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace wechat_miniapp_base.Models
{
    [Table("wepay_order")]
    public class WepayOrder
    {
        [Key]
        public string out_trade_no { get; set; }
        public int order_id { get; set; }
        public string open_id { get; set; }
        public int amount { get; set; }
        public string description { get; set; }
        public string app_id { get; set; }
        public string notify { get; set; }
        public string nonce { get; set; }
        public string sign { get; set; }
        public string timestamp { get; set; }
        public int state { get; set; }

        public string prepay_id { get; set; }
    }
}
