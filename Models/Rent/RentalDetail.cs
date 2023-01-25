using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
    public class RentalDetail
    {
        public DateTime date { get; set; }
        public RentOrderDetail item { get; set; }
        public double rental { get; set; }
        public string type { get; set; }
    }

    public struct RentOrderCollection
    {
        public DateTime date { get; set; }
        public string type { get; set; }
        public int count { get; set; }
        public double unRefundDeposit { get; set; }
        public double unSettledRental { get; set; }

        public double totalDeposit { get; set; }
        public double totalRental { get; set; }

        public RentOrder[] orders { get; set; }
    }
}

