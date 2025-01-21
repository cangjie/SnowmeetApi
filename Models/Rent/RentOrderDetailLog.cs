using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent_list_detail_log")]
	public class RentOrderDetailLog
	{
		[Key]
		public int id { get; set; }

		public int detail_id { get; set; }
		public string status { get; set; }
		public string staff_open_id { get; set; }
		public DateTime create_date { get; set; } = DateTime.Now;

		public MemberSocialAccount msa {get; set;}
	}
}