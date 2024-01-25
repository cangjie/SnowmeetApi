using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent_list_detail")]
	public class RentOrderDetail
	{
        public int id { get; set; } = 0;

        public int rent_list_id { get; set; } = 0;

        //public int rent_item_id { get; set; }

        public string rent_item_name { get; set; } = "";

        public string rent_item_code { get; set; } = "";

        public string rent_item_class { get; set; } = "";

        public double deposit { get; set; } = 0;

        public string deposit_type { get; set; } = "立即租赁";

        public DateTime? start_date { get; set; } = null;

        public double unit_rental { get; set; } = 0;

        public double real_rental { get; set; } = 0;

        public double rental_discount { get; set; } = 0;

        public double rental_ticket_discount {get; set;} = 0;

        public int rental_count { get; set; } = 0;

        public DateTime? real_end_date { get; set; } = null;

        public string memo { get; set; } = "";

        public string images { get; set; } = "";

        public double reparation { get; set; } = 0;

        public double overtime_charge { get; set; } = 0;

        public string rent_staff { get; set; } = "";

        public string return_staff { get; set; } = "";

        [NotMapped]
        public List<RentOrderDetailLog>? log { get; set; } = null;

        [NotMapped]
        public RentItem _item;

        [NotMapped]
        public Models.Users.MiniAppUser? rentStaff { get; set; }

        [NotMapped]
        public Models.Users.MiniAppUser? returnStaff { get; set; }

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
                                DateTime startDate = (DateTime)start_date;
                                if (startDate.Hour == 0 && startDate.Minute == 0 && startDate.Second == 0 && startDate.Microsecond == 0)
                                {
                                    status = "未领取";
                                }
                                else
                                {
                                    status = "已领取";
                                }
                            }
                            if (log != null && log.Count > 0)
                            {
                                status = log[0].status.Trim();
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
        [NotMapped]
        public string _timeLength = "";
        [NotMapped]
        public double _suggestRental = 0;

        [NotMapped]
        public RentItem item
        {
            get
            {
                return _item;
            }
            set
            {
                _item = value;
            }
        }


    }
}

