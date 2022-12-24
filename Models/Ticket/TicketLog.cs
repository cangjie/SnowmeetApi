using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Ticket
{
    [Table("ticket_log")]
    public class TicketLog
	{
		[Key]
		public int id { get; set; }

		public string code { get; set; }
		public string sender_open_id { get; set; }
		public string accepter_open_id { get; set; }
		public string memo { get; set; }
		public DateTime transact_time { get; set; }
	}
}

