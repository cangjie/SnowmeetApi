﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent_list_detail")]
	public class RentOrderDetail
	{
        public int id { get; set; }

        public int rent_list_id { get; set; }

        //public int rent_item_id { get; set; }

        public string rent_item_name { get; set; }

        public string rent_item_code { get; set; }

        public double deposit { get; set; }

        public string deposit_type { get; set; } = "立即租赁";

        public DateTime? start_date { get; set; }

        public double unit_rental { get; set; }

        public double real_rental { get; set; }

        public DateTime? real_end_date { get; set; }

	    public string memo { get; set; }

        public string images { get; set; }

        public double reparation { get; set; }

        public double overtime_charge { get; set; }

        [NotMapped]
        public string rentStatus
        {
            get
            {
                var status = "";
                if (real_end_date != null)
                {
                    status = "已归还";
                }
                else
                {
                    switch (deposit_type.Trim())
                    {
                        case "立即租赁":
                            status = "已领取";
                            break;
                        default:
                            if (start_date == null)
                            {
                                status = "未领取";
                            }
                            else
                            {
                                status = "已领取";
                            }
                            break;
                        
                    }
                }
                return status.Trim();
            }
        }

        [NotMapped]
        public bool overTime { get; set; } = false;

        [NotMapped]
        public string status
        {
            get
            {
                if (real_end_date == null)
                {
                    return rentStatus;
                }
                else
                {
                    return "已归还";
                }
            }
        }
        [NotMapped]
        public double suggestRental
        {
            get
            {
                return _suggestRental;
            }
        }

        [NotMapped]
        public string timeLength
        {
            get
            {
                return _timeLength;
            }
        }
        public string _timeLength = "";

        public double _suggestRental = 0;

    }
}

