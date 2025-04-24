using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models
{
    [Table("mi7_order")]
    public class Mi7Order
    {
        [Key]
        public int id { get; set; }
        public int order_id { get; set; }
        public string? mi7_order_id { get; set; }
        public double sale_price { get; set; }
        public double real_charge { get; set; }
        public string barCode { get; set; } = "";
        public string order_type {get; set;} = "普通";
        public int? enterain_member_id {get; set;} = null;
        public string? enterain_cell {get; set;} = null;
        public string? enterain_real_name {get; set;} = null;
        public string? enterain_gender {get; set;} = null;
        public int valid {get; set;} = 1;
        public int supplement { get; set; } = 1;
        public DateTime? biz_date { get; set; } = null;
        public DateTime? enterain_date { get; set;} = null;
        [ForeignKey("order_id")]
        public OrderOnline? order {get; set;}
        public DateTime create_date { get; set; } = DateTime.Now;
        [NotMapped]
        public Member? member { get; set; }
        //public List<MemberSocialAccount>? msa {get; set;} = null;

    }
}

