using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace SnowmeetApi.Models
{
    [Table("order_share")]
    public class OrderShare
    {
        [Key]
        public int id {get; set;}
        public int order_id {get; set;}
        public int relation_id {get; set;}
        public double amount {get; set;}
        public bool valid {get; set; } = true;
        public bool dealed {get; set;} = false;
        public DateTime create_date {get; set;}
        /// <summary>预计分账日期（介绍人分账用，只存日期）；null = 按默认规则，关单次日起的第一个夜间任务。见 Helpers/ShareDateRules。</summary>
        public DateTime? plan_date {get; set;}
        /// <summary>是否自动分账（介绍人分账用）：false = 店长设成手动，只记账，夜间任务不提交，自己去转账。默认 true。</summary>
        public bool auto_share {get; set;} = true;
        /// <summary>分销商分账方式：auto、manual、bank；null 为旧记录或系统分账。</summary>
        [MaxLength(10)] public string? share_method {get; set;}
        /// <summary>手动分账批次（share_batch.id）：空 = 没走过手动批次。</summary>
        public int? batch_id {get; set;}
        [ForeignKey("relation_id")]
        public OrderShareRelation relation {get; set;}
        public List<PaymentShare> paymentShares {get; set;}
        [NotMapped]
        public double successSharedAmount
        {
            get
            {
                try
                {
                    return paymentShares.Where(s => s.valid && (s.success == true)).Sum(s=>s.amount);
                }
                catch
                {
                    return 0;
                }
            }
        }
        public Models.Order order {get; set;} = null;
    }
    [Table("payment_share")]
    public class PaymentShare
    {
        [Key]
        public int id {get; set;}
        public int payment_id {get; set;}
        public int share_id {get; set;}
        public string out_trade_no {get; set;}
        public double amount {get; set;}
        public bool? success {get; set;} = null;
        public bool valid {get; set;} = true;
        public DateTime? submit_time {get; set;} = null;
        public DateTime? response_time {get; set;} = null;
        public string? response_content {get; set;} = null;
        public string? memo {get; set;} = null;
        public bool can_not_share {get; set;} = false;
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
        public OrderPayment payment {get; set;}
        [ForeignKey("share_id")]
        public OrderShare orderShare {get; set;}

    }
}