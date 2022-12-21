using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("oa_receive")]
    public class OAReceive
    {
        [Key]
        public int id { get; set; }

        public string ToUserName { get; set; } = "";
        public string FromUserName { get; set; } = "";
        public string CreateTime { get; set; } = "";
        public string MsgType { get; set; } = "";
        public string Event { get; set; } = "";
        public string EventKey { get; set; } = "";
        public string Content { get; set; } = "";
        public string MsgId { get; set; } = "";
    }
}
