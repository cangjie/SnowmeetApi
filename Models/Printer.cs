using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("printer")]
    public class Printer
	{
		[Key]
		public int id { get; set; }
		public string name { get; set; }
		public string shop { get; set; }
		public string owner { get; set; }
		public string color { get; set; }
		public int sort {get; set;} = 0;
		public string? region {get; set;} = null;
		public bool valid {get; set;} = true;
	}
}

