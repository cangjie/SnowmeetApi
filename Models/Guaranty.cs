using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("guaranty")]
    public class Guaranty
    {
        [Key]
        public int id { get; set; }
        public int? order_id { get; set; }
        public string guaranty_type { get; set; } = "在线支付";
        public string biz_type { get; set; }
        public int? biz_id { get; set; }
        public string? sub_biz_type { get; set; }
        public int? sub_biz_id { get; set; }
        public double? amount { get; set; }
        public string memo { get; set; } = "";
        public int valid { get; set; }
        public int relieve { get; set; }
        public int? staff_id { get; set; }
        public int? member_id { get; set; }
        public DateTime? update_date { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        [ForeignKey("order_id")]
        public SnowmeetApi.Models.Order? order {get; set; }
        public List<GuarantyPayment> guarantyPayments { get; set; } = new List<GuarantyPayment>();
        [NotMapped]
        public string payStatus
        {
            get
            {
                string payStatus = "未支付";
                bool allPaid = true;
                foreach(GuarantyPayment gp in guarantyPayments)
                {
                    if (!gp.payment.status.Equals("支付成功"))
                    {
                        allPaid = false;
                    }
                    else
                    {
                        payStatus = "部分支付";
                    }
                }
                if (allPaid)
                {
                    return "支付完成";
                }
                else
                {
                    return payStatus;
                }

            }
        }
    }
    [Table("guaranty_payment")]
    public class GuarantyPayment
    {
        public int guaranty_id { get; set; }
        public int payment_id {get; set;}
        [ForeignKey("guaranty_id")]
        public Guaranty? guaranty { get; set; }
        [ForeignKey("payment_id")]
        public OrderPayment payment {get; set;}
    }

}