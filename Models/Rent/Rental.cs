using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;

namespace SnowmeetApi.Models
{
    [Table("rental")]
    public class Rental
    {
        [Key]
        public int id { get; set; }
        public int? order_id { get; set; } = null;
        public int? package_id { get; set; } = null;
        public int? category_id { get; set; } = null;
        public string? name { get; set; } = null;
        public DateTime? start_date { get; set; } = null;
        public DateTime? end_date { get; set; } = null;
        public int valid { get; set; } = 0;
        public int settled { get; set; } = 0;
        public int hide { get; set; } = 0;
        public string memo { get; set; } = "";
        public int? prev_id { get; set; } = null;
        public int changed { get; set; } = 0;
        public int current_avaliable { get; set; } = 0;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public List<RentItem> rentItems { get; set; } = new List<RentItem>();
        public List<RentalDetail> details { get; set; } = new List<RentalDetail>();
        [ForeignKey("order_id")]
        public SnowmeetApi.Models.Order? order { get; set; }
        [ForeignKey(nameof(Guaranty.biz_id))]
        public List<Guaranty> guaranties { get; set; } = new List<Guaranty>();
        [ForeignKey(nameof(Discount.biz_id))]
        public List<Discount> discounts { get; set; } = new List<Discount>();
        [ForeignKey("package_id")]
        public RentPackage? package { get; set; }
        public double GetDiscountAmount(bool ticket)
        {
            List<Discount> dList = discounts
                .Where(d => (ticket && d.ticket_code != null) || !ticket).ToList();
            return dList.Sum(d => d.amount);
        }
        [NotMapped]
        public double ticketDiscountAmount
        {
            get
            {
                return GetDiscountAmount(true);
            }
        }
        [NotMapped]
        public double othersDiscountAmount
        {
            get
            { 
                return GetDiscountAmount(false);
            }
        }
        [NotMapped]
        public bool isPackage
        {
            get
            {
                if (package_id != null)
                {
                    return true;
                }
                if (rentItems.Count > 1)
                {
                    return true;
                }
                return false;
            }
        }
        [NotMapped]
        public double totalGuarantyAmount
        {
            get
            {
                double amount = 0;
                foreach (Guaranty g in guaranties)
                {
                    amount += g.guaranty_type.Trim().Equals("在线支付") ? (double)g.amount : 0;
                }
                return amount;
            }
        }
        [NotMapped]
        public double totalRentalAmount
        {
            get
            {
                return GetTotalAmountByType("租金");
            }
        }
        [NotMapped]
        public double totalOvertimeAmount
        {
            get
            {
                return GetTotalAmountByType("超时费");
            }
        }
        [NotMapped]
        public double totalRepairationAmount
        {
            get
            {
                return GetTotalAmountByType("赔偿金");
            }
        }
        public double GetTotalAmountByType(string type)
        {
            double amount = 0;
            List<RentalDetail> dtlList = details
                .Where(d => d.charge_type.Trim().Equals(type.Trim()) && d.valid == 1)
                .ToList();
            return dtlList.Sum(d => d.amount); 
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
        public string memo { get; set; } = "";
        public int? staff_id { get; set; }
        public int valid { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("rental_id")]
        public Rental rental { get; set; }
    }
    [Table("rent_item")]
    public class RentItem
    {
        [Key]
        public int id { get; set; }
        public int rental_id { get; set; } = 0;
        public string class_name { get; set; } = "";
        public DateTime? pick_time { get; set; } = null;
        public DateTime? return_time { get; set; } = null;
        public string? name { get; set; } = null;
        public int? rent_product_id { get; set; } = null;
        public string? code { get; set; } = null;
        public int? category_id { get; set; } = null;
        public int? prev_id { get; set; } = null;
        public string memo { get; set; } = "";
        public int valid { get; set; } = 0;
        public int? repairation_id { get; set; } = null;
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("rental_id")]
        public Rental rental { get; set; }
        [ForeignKey("repairation_id")]
        public RentalDetail? repairationCharge { get; set; } = null;
        [ForeignKey(nameof(CoreDataModLog.key_value))]
        public List<CoreDataModLog> logs { get; set; } = new List<CoreDataModLog>();
        [NotMapped]
        public Staff? pickStaff
        {
            get
            {
                Staff? staff = null;
                foreach (CoreDataModLog log in logs)
                {
                    if (log.current_value.Trim().Equals("已发放"))
                    {
                        staff = log.staff;
                    }
                }
                return staff;
            }
        }
        [NotMapped]
        public Staff? returnStaff
        {
            get
            {
                Staff? staff = null;
                foreach (CoreDataModLog log in logs)
                {
                    if (log.current_value.Trim().Equals("已归还"))
                    {
                        staff = log.staff;
                    }
                }
                return staff;
            }
        }
       
    }
}