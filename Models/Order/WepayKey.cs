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
    }
}
