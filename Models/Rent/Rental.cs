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
        public int expectDays { get; set; } = 0;
        public double? guaranty { get; set; }
        public bool noGuaranty { get; set; } = false;
        public double? guaranty_discount { get; set; } = 0;
        public bool entertain { get; set; } = false;
        public int? staff_id { get; set; } = null;
        public bool experience { get; set; } = false;
        public bool? appending {get; set;} = null;
        public DateTime? append_commit_time {get; set;} = null;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
        public List<RentItem> rentItems { get; set; } = new List<RentItem>();
        public List<RentalDetail> details { get; set; } = new List<RentalDetail>();
        [NotMapped]
        public double? _filledOverTimeCharge = null;
        public List<RentalPricePreset> pricePresets { get; set; } = null;
        [NotMapped]
        public DateTime? realStartDate
        {
            get
            {
                DateTime? startDate = null;
                if (availabelRentDetails != null && availabelRentDetails.Count > 0)
                {
                    return availabelRentDetails[0].rental_date.Date;
                }

                return startDate;
            }
        }
        [NotMapped]
        public DateTime? realEndDate
        {
            get
            {
                if (settled == 1)
                {
                    if (availabelRentDetails != null && availabelRentDetails.Count > 0)
                    {
                        return availabelRentDetails[availabelRentDetails.Count - 1].rental_date.Date;
                    }
                    else
                    {
                        return null;
                    }
                }
                else
                {
                    return null;
                }

            }
        }
        [NotMapped]
        public List<RentalDetail> availabelRentDetails
        {
            get
            {
                if (details != null)
                {
                    return details.Where(d => d.valid == 1).OrderBy(d => d.rental_date).ToList();
                }
                return new List<RentalDetail>(); ;
            }
        }
        [NotMapped]
        public double totalPaidGuarantyAmount
        {
            get
            {
                double amount = 0;
                for (int i = 0; guaranties != null && i < guaranties.Count; i++)
                {
                    Guaranty guaranty = guaranties[i];
                    if (guaranty.payStatus == "支付完成")
                    {
                        amount += (double)guaranty.amount;
                    }
                }
                return amount;
            }
        }

        [ForeignKey("staff_id")]
        public Staff staff { get; set; }
        [NotMapped]
        public List<RentPrice> priceList { get; set; } = new List<RentPrice>();
        [ForeignKey("order_id")]
        public SnowmeetApi.Models.Order? order { get; set; }
        [ForeignKey(nameof(Guaranty.biz_id))]
        public List<Guaranty> guaranties { get; set; } = new List<Guaranty>();






        [ForeignKey("package_id")]
        public RentPackage? package { get; set; } = null;



        [ForeignKey(nameof(Discount.biz_id))]
        public List<Discount> discounts { get; set; } = new List<Discount>();



        [NotMapped]
        public List<Discount> availableDiscounts
        {
            get
            {
                if (discounts == null)
                {
                    return new List<Discount>();
                }
                else
                {
                    return discounts.Where(d => d.valid == 1 && d.order_id == order_id).ToList();
                }
            }
        }

        public double GetDiscountAmount(bool ticket)
        {
            List<Discount> dList = availableDiscounts
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
        public double totalDiscountAmount
        {
            get
            {
                return ticketDiscountAmount + othersDiscountAmount;
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
                if (rentItems != null && rentItems.Count > 1)
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
                if (experience)
                {
                    return 0;
                }
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
            set
            {
                _filledOverTimeCharge = value;
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
        [NotMapped]
        public double totalSummary
        {
            get
            {
                return (entertain ? 0 : totalRentalAmount) + totalOvertimeAmount + totalRepairationAmount - totalDiscountAmount;
            }
        }
        public double GetTotalAmountByType(string type)
        {
            // double amount = 0;
            if (details == null)
            {
                return 0;
            }
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
        [NotMapped]
        public double? _filledDiscountAmount { get; set; } = null;
        public int? staff_id { get; set; }
        public int valid { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [NotMapped]
        public double discountTotalAmount
        {
            get
            {
                double amount = 0;
                for (int i = 0; availableDiscounts != null && i < availableDiscounts.Count; i++)
                {
                    amount += availableDiscounts[i].amount;
                }
                return amount;
            }
            set
            {
                _filledDiscountAmount = value;
            }
        }


        [ForeignKey(nameof(Discount.sub_biz_id))]
        public List<Discount> discounts { get; set; } = new List<Discount>();


        [NotMapped]
        public List<Discount> availableDiscounts
        {
            get
            {
                if (discounts == null)
                {
                    return new List<Discount>();
                }
                else
                {
                    return discounts.Where(d => d.valid == 1 && d.biz_id == rental_id).ToList();
                }
            }
        }

        public double GetDiscountAmount(bool ticket)
        {
            List<Discount> dList = availableDiscounts
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
        public double totalDiscountAmount
        {
            get
            {
                return ticketDiscountAmount + othersDiscountAmount;
            }
        }


        [ForeignKey("rental_id")]
        public Rental rental { get; set; }
        [ForeignKey("rent_price_id")]
        public RentPrice? rentPrice { get; set; }
    }
    [Table("rent_item")]
    public class RentItem
    {
        public enum RentItemStatus { 未发放, 已发放, 暂存, 已归还, 已更换 }
        public enum RentItemBusinessStatus { 上架, 维修, 保养, 丢失, 下架, 异地归还 }
        [Key]
        public int id { get; set; }
        public int? rental_id { get; set; } = null;
        public string class_name { get; set; } = "";
        public DateTime? pick_time { get; set; } = null;
        public DateTime? return_time { get; set; } = null;
        public string? name { get; set; } = null;
        public int? rent_product_id { get; set; } = null;
        public string? code { get; set; } = null;
        public int? category_id { get; set; } = null;
        public int? prev_id { get; set; } = null;
        public int? next_id { get; set; } = null;
        public string memo { get; set; } = "";
        public int valid { get; set; } = 0;
        public int? repairation_id { get; set; } = null;
        public bool noCode { get; set; } = false;
        public bool noNeed { get; set; } = false;
        public bool atOnce { get; set; } = false;
        [NotMapped]
        public Staff? pickStaff
        {
            get
            {
                Staff? staff = null;
                if (availableLog == null)
                {
                    return null;
                }
                for (int i = 0; availableLog != null && i < availableLog.Count; i++)
                {
                    if (availableLog[i].status == "已发放")
                    {
                        staff = availableLog[i].staff;
                        break;
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
                if (status != "已归还" || availableLog == null || availableLog.Count == 0)
                {
                    return null;
                }
                Staff? staff = null;
                for (int i = availableLog.Count - 1; i >= 0; i--)
                {
                    if (availableLog[i].status == "已归还")
                    {
                        staff = availableLog[i].staff;
                        break;
                    }
                }
                return staff;
            }
        }
        [NotMapped]
        public string status
        {
            get
            {
                if (logs == null || logs.Count == 0)
                {
                    return RentItemStatus.未发放.ToString();
                }
                else
                {
                    RentItemLog log = logs.OrderByDescending(l => l.id).FirstOrDefault();
                    if (log == null)
                    {
                        return RentItemStatus.未发放.ToString();
                    }
                    else
                    {
                        return log.status.Trim();
                    }
                }
            }
        }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("category_id")]
        public RentCategory category { get; set; } = null;
        [ForeignKey("rental_id")]
        public Rental? rental { get; set; } = null;
        [ForeignKey(nameof(Models.RentItemLog.rent_item_id))]
        public List<Models.RentItemLog> logs { get; set; } = new List<Models.RentItemLog>();
        [NotMapped]
        public List<Models.RentalDetail> repairationCharges = new List<Models.RentalDetail>();
        [NotMapped]
        public List<RentalDetail> availableRepairationCharges
        {
            get
            {
                if (repairationCharges == null)
                {
                    return new List<RentalDetail>();
                }
                else
                {
                    return repairationCharges.Where(d => d.valid == 1 && d.charge_type == "赔偿金").ToList();
                }
            }
        }
        [NotMapped]
        public double totalRepairationAmount
        {
            get
            {
                if (availableRepairationCharges == null)
                {
                    return 0;
                }
                else
                {
                    double amount = 0;
                    for (int i = 0; i < availableRepairationCharges.Count; i++)
                    {
                        amount += availableRepairationCharges[i].amount;
                    }
                    return amount;
                }
            }
        }
        [NotMapped]
        public List<RentItemLog> availableLog
        {
            get
            {
                if (logs == null)
                {
                    return new List<RentItemLog>();
                }
                else
                {
                    return logs.Where(l => l.valid == 1).OrderBy(l => l.id).ToList();
                }
            }
        }
        [NotMapped]
        public DateTime? pickDate
        {
            get
            {
                DateTime? pickDate = null;
                for (int i = 0; availableLog != null && i < availableLog.Count; i++)
                {
                    if (availableLog[i].status == "已发放")
                    {
                        pickDate = availableLog[i].create_date;
                        break;
                    }
                }
                return pickDate;
            }
        }
        [NotMapped]
        public DateTime? returnDate
        {
            get
            {
                DateTime? returnDate = null;
                if (availableLog != null && availableLog.Count > 0 && availableLog[availableLog.Count - 1].status == "已归还")
                {
                    returnDate = availableLog[availableLog.Count - 1].create_date;
                }
                return returnDate;
            }
        }
        [NotMapped]
        public DateTime? changeDate
        {
            get
            {
                DateTime? changeDate = null;
                if (availableLog != null && availableLog.Count > 0 && availableLog[availableLog.Count - 1].status == "已更换")
                {
                    changeDate = availableLog[availableLog.Count - 1].create_date;
                }
                return changeDate;
            }
        }
        [NotMapped]
        public Staff? changeStaff
        {
            get
            {
                if (availableLog != null && availableLog.Count > 0 && availableLog[availableLog.Count - 1].status == "已更换")
                {
                    return availableLog[availableLog.Count - 1].staff;
                }
                return null;
            }
        }
    }
    [Table("rent_item_log")]
    public class RentItemLog
    {
        [Key]
        public int id { get; set; }
        public int rent_item_id { get; set; }
        public string status { get; set; }
        public int? staff_id { get; set; }
        public int? member_id { get; set; }
        public int valid { get; set; } = 1;
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("staff_id")]
        public Staff staff { get; set; }
    }
    [Table("rental_price_preset")]
    public class RentalPricePreset
    {
        [Key]
        public int id { get; set; }
        public int rental_id { get; set; }
        public string rent_type { get; set; } = "日场";
        public DateTime rent_date { get; set; } = DateTime.Now.Date;
        public double price { get; set; } = 0;
        public double discount { get; set; } = 0;
        public string day_type { get; set; } = "";
        public string scene { get; set; } = "";
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("rental_id")]
        public Rental rental { get; set; } = null;

    }
    public class CategoryRentItem
    {
        public int category_id { get; set; }
        public RentCategory? category { get; set; } = null;
        public List<RentItem> items { get; set; } = new List<RentItem>();
    }
}