using System;
using SnowmeetApi.Models.Ticket;
namespace SnowmeetApi.Models.Maintain
{
       
    public class MaintainOrder
    {
        public string shop { get; set; } = "";
        public string name { get; set; } = "";
        public string gender { get; set; } = "";
        public string payMethod { get; set; } = "";
        public string payOption { get; set; } = "";
        public float summaryPrice { get; set; } = 0;
        public float ticketDiscount { get; set; } = 0;
        public float discount { get; set; } = 0;
        public string ticketCode { get; set; } = "";
        public int orderId { get; set; } = 0;
        public string customerOpenId { get; set; } = "";
        public string cell { get; set; } = "";
        public MaintainLive[] items { get; set; } = new MaintainLive[0];
        public OrderOnline? order { get; set; }
        public Ticket.Ticket? ticket { get; set; }
    }
}

