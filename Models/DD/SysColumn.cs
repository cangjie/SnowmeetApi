using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.DD
{
    [Table("syscolumns")]
    public class SysColumn
	{
		public string name { get; set; }
		public int id { get; set; }
		public int xtype { get; set; }
	}
}

