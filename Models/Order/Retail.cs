using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("retail")]
    public class Retail
    {
        public static List<CoreDataModLog> GetUpdateDifferenceLog(Retail ori, Retail cur, int? memberId, int? staffId, string scene)
        {
            if (ori.id != cur.id)
            {
                return null;
            }
            List<CoreDataModLog> logs = new List<CoreDataModLog>();
            TimeSpan ts = DateTime.Now - DateTime.Parse("1970-1-1");
            if (ori.order_id != cur.order_id)
            {
                logs.Add(Util.CreateCoreDataModLog("retail", "order_id", ori.id.ToString(), ori.order_id, cur.order_id, memberId, staffId, scene, ts.Ticks));
                ori.order_id = cur.order_id;
            }
            if (ori.mi7_code != cur.mi7_code)
            {
                logs.Add(Util.CreateCoreDataModLog("retail", "mi7_code", ori.id.ToString(), ori.mi7_code, cur.mi7_code, memberId, staffId, scene, ts.Ticks));
                ori.mi7_code = cur.mi7_code;
            }
            if (ori.sale_price != cur.sale_price)
            {
                logs.Add(Util.CreateCoreDataModLog("retail", "sale_price", ori.id.ToString(), ori.sale_price, cur.sale_price, memberId, staffId, scene, ts.Ticks));
                ori.sale_price = cur.sale_price;
            }
            if (ori.deal_price != cur.deal_price)
            {
                logs.Add(Util.CreateCoreDataModLog("retail", "deal_price", ori.id.ToString(), ori.deal_price, cur.deal_price, memberId, staffId, scene, ts.Ticks));
                ori.deal_price = cur.deal_price;
            }
            if (ori.order_type != cur.order_type)
            {
                logs.Add(Util.CreateCoreDataModLog("retail", "order_type", ori.id.ToString(), ori.order_type, cur.order_type, memberId, staffId, scene, ts.Ticks));
                ori.order_type = cur.order_type;
            }
            if (ori.valid != cur.valid)
            {
                logs.Add(Util.CreateCoreDataModLog("retail", "valid", ori.id.ToString(), ori.valid, cur.valid, memberId, staffId, scene, ts.Ticks));
                ori.valid = cur.valid;
            }
            
            return logs;
        }
        [Key]
        public int id { get; set; }
        public int? order_id {get; set;} = null;
        public string? mi7_code {get; set;} = null;
        public double sale_price {get; set;} = 0;
        public double deal_price {get; set;} = 0;
        public string? order_type {get; set;} = null;
        public int valid {get; set;} = 1;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
        [NotMapped]
        public string textColor { get; set; } = "";
        [NotMapped]
        public string backgroundColor { get; set; } = "";
        [ForeignKey("order_id")]
        public Order? order {get; set;} = null;
        [NotMapped]
        public List<CoreDataModLog> logs {get; set;} = new List<CoreDataModLog>();
    }
}