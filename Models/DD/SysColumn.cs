using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.DD
{
    [Table("dd_fields")]
    public class SysColumn
	{
		public int table_id { get; set; }
		public string column_name { get; set; }
		public string data_type { get; set; }
		public short type_length { get; set; }

		public short colid { get; set; }
	}
}

