using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
	[Table("wxreceivemsg")]
	public class OldWeixinReceive
	{
		public string wxreceivemsg_from { get; set; }
		public string wxreceivemsg_eventkey { get; set; }
		public DateTime wxreceivemsg_crt { get; set; }

    }
}

