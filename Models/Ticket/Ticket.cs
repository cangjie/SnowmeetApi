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

        public int printed { get; set; }

        public int used { get; set; }
        public DateTime? used_time { get; set; }

        public int template_id { get; set; }
        public string miniapp_recept_path { get; set; }
        public DateTime create_date { get; set; }
    }
}
