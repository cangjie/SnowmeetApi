using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Ticket
{
    [Table("ticket_template")]
    public class TicketTemplate
    {
        [Key]
        public int id { get; set; }
        public string type { get; set; }
        public string name { get; set; }
        public string memo { get; set; }

        public string miniapp_recept_path { get; set; }

        public int hide { get; set; }
    }
}
