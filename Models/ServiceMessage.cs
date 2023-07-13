using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
	[Table("service_message_log")]
	public class ServiceMessage
	{
		[Key]
		public int id { get; set; }

		public string type { get; set; }
		public string from { get; set; }
		public string to { get; set; }
		public string content { get; set; }
		public string return_code { get; set; }
		
	}
}

