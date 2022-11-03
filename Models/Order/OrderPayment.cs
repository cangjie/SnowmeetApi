using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace SnowmeetApi.Models.Order
{
    [Table("order_payment")]
    public class OrderPayment
    {

        public string staffRealName = "";

        [Key]
        public int id { get; set; }

        public int order_id { get; set; }
        public string pay_method { get; set; }
        public double amount { get; set; }
        public string status { get; set; } = "待支付";
        public string? out_trade_no { get; set; }
        public int? mch_id { get; set; }
        public string? open_id { get; set; }
        public string? app_id { get; set; }
        public string? notify { get; set; }
        public string? nonce { get; set; }
        public string? sign { get; set; }
        public string? timestamp { get; set; }
        public string? prepay_id { get; set; }
        public string? ssyn { get; set; }
        public string staff_open_id { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;

        [NotMapped]
        public string staffName
        {
            get
            {
                return staffRealName.Trim();
            }
        }
        /*
        public async Task<ActionResult<bool>> GetStaffRealName(SnowmeetApi.Data.ApplicationDBContext db)
        {
            if (!staff_open_id.Trim().Equals(""))
            {
                var staffUser = await db.MiniAppUsers.FindAsync(staff_open_id.Trim());
                if (staffUser != null)
                {
                    staffRealName = staffUser.real_name.Trim();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        */
    }
}

