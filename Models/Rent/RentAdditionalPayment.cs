using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using NuGet.Common;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Models.Rent
{
    [Table("rent_additional_payment")]
    public class RentAdditionalPayment
    {
        [Key]
        public int id {get; set;}
        //[ForeignKey(nameof(Models.Rent.RentOrder))]
        public int rent_list_id {get; set;}
        //[ForeignKey(nameof(Models.OrderOnline))]
        public int? order_id {get; set;}
        public double amount {get; set;}
        public string reason {get; set;}
        public int is_paid {get; set;}
        public string pay_method {get; set;}
        public DateTime? update_date {get; set;}
        public DateTime create_date {get; set;} 
        public string staff_open_id {get; set;}
        [ForeignKey("order_id")]
        public OrderOnline? order {get; set;}
        //[ForeignKey("rent_list_id")]
        public RentOrder rentOrder {get; set;}
        [NotMapped]
        public Member? staffMember {get; set;}

    }
}