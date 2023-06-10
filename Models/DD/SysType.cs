using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.DD
{
	[Table("systypes")]
	public class SysType
	{
		[Key]
		public string name { get; set; }

		public int xtype { get; set; }
	}
}

