using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
namespace SnowmeetApi.Models
{

	public class CareReport
	{
		[Key]
		public int id {get; set;}
		public string order_id { get; set; }
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
		public string orderMemo
		{
			get
			{
				string m = memo;
				if (order != null)
				{
					memo += (" " + order.memo);
				}
				return m;
			}
		}
		public string jishi { get; set; }
		public double additional_fee { get; set; }
		public string staff { get; set; }
		public DateTime create_date { get; set; }
		public string safeCheck
		{
			get
			{
				string name = "——";
				List<CareTask> l = logs.Where(l => l.task_name.Trim().Equals("安全检查"))
					.OrderByDescending(l => l.id).ToList();
				if (l.Count > 0 && l[0].staff != null )
				{
					name = l[0].staff.name.Trim();
				}
				return name;
			}
		}
		public string giveOut
		{
			get
			{
				string name = "——";

				List<CareTask> l = logs.Where(l => l.task_name.Trim().Equals("发板"))
					.OrderByDescending(l => l.id).ToList();
				if (l.Count > 0 && l[0].staff != null)
				{
					name = l[0].staff.name.Trim();
				}
				return name;
			}
		}
		public string outTradeNo
		{
			get
			{
				string no = "——";
				if (order!=null && order.availablePayments.Count > 0)
				{
					no = order.availablePayments[0].out_trade_no == null? "——" :   order.availablePayments[0].out_trade_no.Trim();
				}
				return no;
			}
		}
		public Models.Order? order {get; set;}
		public List<CareTask> logs {get; set;} = new List<CareTask>();
	}
}