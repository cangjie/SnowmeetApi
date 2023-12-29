using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("printer")]
    public class Printer
	{
		public int id { get; set; }
		public string name { get; set; }
		public string shop { get; set; }
		public string owner { get; set; }
		public string color { get; set; }
	}
}

