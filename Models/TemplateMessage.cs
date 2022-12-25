using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
	[Table("template_message_log")]
	public class TemplateMessage
	{
		[Key]
		public int id { get; set; }

		public string template_id { get; set; }
		public string from { get; set; }
		public string to { get; set; }
		public string first { get; set; }
		public string keywords { get; set; }
		public string remark { get; set; }
		public string url { get; set; }
		public string ret_message { get; set; }
	}
}

