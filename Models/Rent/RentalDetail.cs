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
}

