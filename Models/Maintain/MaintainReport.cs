using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Maintain
{

	public class MaintainReport
	{
		public int order_id { get; set; }
		public string shop { get; set; }
		public double total_paid { get; set; }
		public string task_flow_num { get; set; }
		public string equip_type { get; set; }
		public string equip_brand { get; set; }
		public string equip_scale { get; set; }
		public string degree { get; set; }
		public string edge { get; set; }
		public string vax { get; set; }
		public string unvax { get; set; }
		public string more { get; set; }
		public string memo { get; set; }
		public string jishi { get; set; }
		public double additional_fee { get; set; }
		public string staff { get; set; }
		public DateTime create_date { get; set; }

	}
}

