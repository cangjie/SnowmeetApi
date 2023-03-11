using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
    public class RentalDetail
    {
        public string _name = "";
        public string _cell = "";
        public string _shop = "";
        public string _staff = "";
        public DateTime date { get; set; }
        public RentOrderDetail item { get; set; }
        public double rental { get; set; }
        public string type { get; set; }
        public string name
        {
            get
            {
                return _name.Trim();
            }
            set
            {
                _name = value;
            }
        }
        public string cell
        {
            get
            {
                return _cell;
            }
            set
            {
                _cell = value;
            }
        }
        public string shop
        {
            get
            {
                return _shop;
            }
            set
            {
                _shop = value;
            }
        }
        public string staff
        {
            get
            {
                return _staff;
            }
            set
            {
                _staff = value;
            }
        }
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
    /*
    public struct RentDetail
    {
        public int id { get; set; }
        public DateTime date { get; set; }
        public string code { get; set; }
        public string name { get; set; }
        public double rental { get; set; }
        public double reparation { get; set; }
        public double overTimeCharge { get; set; }
    }
    */
}

