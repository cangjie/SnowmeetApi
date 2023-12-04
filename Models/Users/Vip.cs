using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Users
{
    [Table("vip_info")]
    public class Vip
	{
		[Key]
		public int id { get; set; }
		public string cell { get; set; }
		public string name { get; set; }
		public string memo { get; set; }
	}
}

