using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.Rent
{
	[Table("rent_list_detail")]
	public class RentOrderDetail
	{
        public enum RentStatus { 未领取, 已发放, 已暂存, 已归还 }
        public int id { get; set; } = 0;

        public int? rent_list_id { get; set; } = 0;

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
        public string? rent_status {get; set;}
        public int valid {get; set;} = 1;
        public DateTime? pick_date {get; set;} = null;
        public DateTime? return_date {get; set;} = null;
        public string? package_code {get; set;} = null;

        public DateTime? update_date {get; set;} = null;
        [ForeignKey("detail_id")]
        public List<RentOrderDetailLog> log { get; set; } = new List<RentOrderDetailLog>();

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
                if (rent_status != null){
                    return rent_status;
                }
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
                            if (start_date == null)
                            {
                                status = "未领取";
                            }
                            else
                            {
                                DateTime startDate = (DateTime)start_date;
                                status = "已发放";
                                if (startDate.Hour == 0 && startDate.Minute == 0 && startDate.Second == 0 && startDate.Microsecond == 0)
                                {
                                    status = "未领取";
                                }
                                else
                                {
                                    status = "已发放";
                                }
                            }
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
                                    status = "已发放";
                                }
                            }
                            break;
                    }
                }
                if (log != null && log.Count > 0)
                {
                    //log.OrderByDescending(l => l.id)
                    List<RentOrderDetailLog> logArr = log.OrderByDescending(l => l.id).ToList();
                    for (int i = 0; i < logArr.Count; i++)
                    {
                        RentOrderDetailLog l = logArr[i];
                        switch (l.status)
                        {
                            case "已发放":
                                status = RentOrderDetail.RentStatus.已发放.ToString();
                                break;
                            case "已归还":
                                status = RentOrderDetail.RentStatus.已归还.ToString();
                                break;
                            case "已暂存":
                                status = RentOrderDetail.RentStatus.已暂存.ToString();
                                break;
                            case "未领取":
                                status = RentOrderDetail.RentStatus.未领取.ToString();
                                break;
                            default:
                                break;
                        }
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
        
        public string GetLogStaffName(string status)
        {
            string name = "";
            for (int i = 0; i < log.Count; i++)
            {
                RentOrderDetailLog l = log[i];
                if (l.status.Trim().Equals(status.Trim()))
                {
                    name = (l.msa != null && l.msa.member != null && l.msa.member.real_name != null) ? l.msa.member.real_name : "";
                    break;
                }
            }
            return name.Trim();
        }
        [NotMapped]
        public MemberSocialAccount? returnMsa {get; set;} = null;

        public string pickStaffName
        {
            get
            {
                return GetLogStaffName("已发放");
            }
        }
        public string returnStaffName
        {
            get
            {
                string returnName = GetLogStaffName("已归还");
                if (returnName.Trim().Equals(""))
                {
                    returnName = (returnMsa != null && returnMsa.member != null) ? returnMsa.member.real_name.Trim() : "";
                }
                return returnName;
            }
        }
       

    }
}

