using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.DD
{
	[Table("sysobjects")]
	public class SysObject
    {
		[Key]
		public int id { get; set; }
		public string name { get; set; }
		public string type { get; set; }
		[NotMapped]
		public string description { get; set; } = "";
	}
}