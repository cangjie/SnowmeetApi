using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.DD
{
	[Table("object_description")]
	public class ExtendedProperties
	{
		public int major_id { get; set; }
		public int minor_id { get; set; }
		public string name { get; set; }
		public string value { get; set; }
	}
}	

