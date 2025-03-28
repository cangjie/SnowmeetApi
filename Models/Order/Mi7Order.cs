﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.Order
{
    [Table("mi7_order")]
    public class Mi7Order
    {
        [Key]
        public int id { get; set; }
        public int order_id { get; set; }
        public string mi7_order_id { get; set; }
        public double sale_price { get; set; }
        public double real_charge { get; set; }
        public string barCode { get; set; } = "";
        [ForeignKey("order_id")]
        public OrderOnline? order {get; set;}
        public DateTime create_date { get; set; } = DateTime.Now;
        [NotMapped]
        public Member? member { get; set; }
        //public List<MemberSocialAccount>? msa {get; set;} = null;

    }
}

