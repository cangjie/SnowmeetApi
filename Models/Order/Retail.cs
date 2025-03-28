using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using SnowmeetApi.Controllers.Order;

namespace SnowmeetApi.Models.Order
{
    public class Retail
    {
        public string mi7OrderId {get; set;}
        public double salePrie {get; set;}
        public double charge {get; set;}
        public int count {get; set;}
        public int? orderId {get; set;} = null;
        public List<OrderOnline>? orders {get; set;} = null;
        public OrderOnline? order {get; set;} = null;
        public List<OrderPayment> payments
        {
            get
            {
                List<OrderPayment> payments = new List<OrderPayment>();
                for(int i = 0; i < orders.Count; i++)
                {
                    OrderOnline order = orders[i];
                    for(int j = 0; j < order.paymentList.Count; j++)
                    {
                        if (payments.Where(p => p.id == order.paymentList[j].id).ToList().Count == 0)
                        {
                            payments.Add(order.paymentList[j]);
                        }
                    }
                }
                return payments;
            }
        }
        public List<Models.Order.OrderPaymentRefund> refunds
        {
            get
            {
                List<OrderPaymentRefund> refunds = new List<OrderPaymentRefund>();
                for(int i = 0; i < payments.Count; i++)
                {
                    for(int j = 0; j < payments[i].refunds.Count; j++)
                    {
                        refunds.Add(payments[i].refunds[j]);
                    }
                }
                return refunds;
            }
        }        
    }
}