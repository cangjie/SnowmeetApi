using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("rental")]
    public class Rental
    {
        [Key]
        public int id { get; set; }
        public int? order_id { get; set; }
        public int? package_id { get; set; }
        public int? categroy_id { get; set; }
        public string? name { get; set; }
        public DateTime? start_date { get; set; }
        public DateTime? end_date { get; set; }
        //public double guaranty_amount { get; set; }
        public int valid { get; set; }
        public int settled { get; set; }
        public int hide { get; set; }
        public string memo { get; set; }
        public int? prev_id { get; set; }
        public int changed { get; set; }
        public int current_avaliable { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }
        public List<RentItem> rentItems { get; set; } = new List<RentItem>();
        public List<RentalDetail> details { get; set; } = new List<RentalDetail>();
        [ForeignKey("order_id")]
        public SnowmeetApi.Models.Order? order { get; set; }
        [ForeignKey(nameof(Guaranty.biz_id))]
        public List<Guaranty> guarantyList { get; set; } = new List<Guaranty>();
        [NotMapped]
        public double totalRentalAmount
        {
            get
            {
                double amount = 0;
                foreach (RentalDetail dtl in details)
                {
                    amount += ((dtl.valid == 1) ? dtl.amount : 0);
                }
                return amount;
            }
        }
    }
    [Table("rental_detail")]
    public class RentalDetail
    {
        [Key]
        public int id { get; set; }
        public int rental_id { get; set; }
        public int? rent_item_id { get; set; }
        public string charge_type { get; set; } = "租金";
        public DateTime rental_date { get; set; }
        public int? rent_price_id { get; set; }
        public double amount { get; set; }
        public string memo { get; set; }
        public int? staff_id { get; set; }
        public int valid { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }
        [ForeignKey("rental_id")]
        public Rental rental { get; set; }
    }
    [Table("rent_item")]
    public class RentItem
    {
        [Key]
        public int id { get; set; }
        public int rental_id { get; set; }
        public DateTime? pick_time { get; set; }
        public DateTime? return_time { get; set; }
        public string? name { get; set; }
        public int? rent_product_id { get; set; }
        public string? code { get; set; }
        public int? category_id { get; set; }
        public int? prev_id { get; set; }
        public string memo { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; }
        [ForeignKey("rental_id")]
        public Rental rental { get; set; }
    }
}