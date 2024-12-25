using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Ticket
{
    [Table("ticket")]
    public class Ticket
    {
        [Key]
        public string code { get; set; }

        public string name { get; set; }
        public string memo { get; set; }

        public string open_id { get; set; }

        public string oper_open_id { get; set; }

        public int shared { get; set; }

        public DateTime? shared_time { get; set; }

        public int printed { get; set; }

        public int used { get; set; }
        public DateTime? used_time { get; set; }

        public int template_id { get; set; }
        public string miniapp_recept_path { get; set; }
        public DateTime create_date { get; set; }

        public string channel { get; set; } = "";

        public DateTime expire_date { get; set; } = DateTime.MaxValue;

        public string create_memo {get; set;} = "";
        public int? order_id {get; set;}
        public DateTime accepted_time {get; set;} = DateTime.Now;
        public string use_memo {get; set;} = "";
        

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
    }
}
