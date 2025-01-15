using Aop.Api.Domain;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models.Rent
{
    [Table("rent_list_log")]
    public class RentOrderLog
    {
        public int id { get; set; }
        public int rent_list_id { get; set; }
        public string memo { get; set; }
        public string? field_name { get;set; }
        public string? prev_value { get; set; }
        public int? oper_member_id { get; set; }
        public DateTime create_date { get; set; }
        [NotMapped]
        public Models.Users.Member? member { get; set; }

    }
}
