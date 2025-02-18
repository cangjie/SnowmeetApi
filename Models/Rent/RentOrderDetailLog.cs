using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent_list_detail_log")]
	public class RentOrderDetailLog
	{
		public enum Status { 已发放, 开始计费, 已暂存, 设置计费开始时间, 设置计费结束时间, 已归还, 设置归还时间 }
		[Key]
		public int id { get; set; }
		public int detail_id { get; set; }
		public string status { get; set; }
		public string staff_open_id { get; set; }
		public string? prev_value {get; set; }
		public DateTime create_date { get; set; } = DateTime.Now;
	}
}