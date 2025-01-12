using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using NuGet.Common;

namespace SnowmeetApi.Models.Rent
{
    [Table("rent_additional_payment")]
    public class RentAdditionalPayment
    {
        [Key]
        public int id {get; set;}
        public int rent_list_id {get; set;}
        public int? order_id {get; set;}
        public double amount {get; set;}
        public string reason {get; set;}
        public int is_paid {get; set;}
        public DateTime? update_date {get; set;}
        public DateTime create_date {get; set;} 
        public string staff_open_id {get; set;}
        [NotMapped]
        public OrderOnline? order {get; set;}

    }
}