using System;
using SnowmeetApi.Models.Ticket;
namespace SnowmeetApi.Models.Maintain
{
       
    public class MaintainOrder
    {
        private MaintainLive[] _items;// = new MaintainLive[0];
        public string shop { get; set; } = "";
        public string name { get; set; } = "";
        public string gender { get; set; } = "";
        public string payMethod { get; set; } = "";
        public string payOption { get; set; } = "";
        public double summaryPrice { get; set; } = 0;
        public double ticketDiscount { get; set; } = 0;
        public double discount { get; set; } = 0;
        public string ticketCode { get; set; } = "";
        public int orderId { get; set; } = 0;
        public string customerOpenId { get; set; } = "";
        public string cell { get; set; } = "";
        public MaintainLive[] items
        {
            get
            {
                return _items;
            }
            set
            {
                _items = value;
            }
        }
        public OrderOnline? order { get; set; }
        public Ticket.Ticket? ticket { get; set; }
        public DateTime orderDate { get; set; }
    }
}

