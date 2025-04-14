using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Aop.Api.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using SnowmeetApi.Models.Users;

namespace SnowmeetApi.Models
{
    [Table("maintain_in_shop_request")]
    public class MaintainLive
    {
        [Key]
        public int id { get; set; }
        public string shop { get; set; }
        public string open_id { get; set; }
        
        public string equip_type { get; set; }
        public string brand { get; set; } = "";
        public string scale { get; set; } = "";
        public int edge { get; set; } = 0;
        public int candle { get; set; } = 0;
        public int repair_more { get; set; } = 0;
        public DateTime? pick_date { get; set; } = DateTime.Now;

        public int task_id { get; set; } = 0;
        public int? order_id { get; set; } = 0;
        public string service_open_id { get; set; } = "";
        public string confirmed_equip_type { get; set; }
        public string confirmed_brand { get; set; }
        public string confirmed_serial { get; set; }
        public string confirmed_scale { get; set; }
        public string confirmed_year { get; set; }
        public int confirmed_edge { get; set; }
        public int confirmed_degree { get; set; }
        public int confirmed_candle { get; set; }
        public string confirmed_more { get; set; } = "";
        public string confirmed_memo { get; set; } = "";
        public DateTime confirmed_pick_date { get; set; } = DateTime.Now;
        public double confirmed_additional_fee { get; set; } = 0;
        public string confirmed_cell { get; set; } = "";
        public string confirmed_name { get; set; } = "";
        public string confirmed_gender { get; set; } = "";
        public int confirmed_product_id { get; set; } = 0;
        public string confirmed_images { get; set; } = "";
        public int confirmed_urgent { get; set; } = 0;
        public string confirmed_foot_length { get; set; } = "";
        public string confirmed_front { get; set; } = "";
        public string confirmed_height { get; set; } = "";
        public string confirmed_weight { get; set; } = "";
        public string confirmed_binder_gap { get; set; } = "";
        public string confirmed_front_din { get; set; } = "";
        public string confirmed_rear_din { get; set; } = "";
        public string confirmed_left_angle { get; set; } = "";
        public string confirmed_right_angle { get; set; } = "";
        public string confirmed_relation { get; set; } = "";
        public string confirmed_id { get; set; } = "";

        public int batch_id { get; set; } = 0;
        public int label_printed { get; set; } = 0;
        public string? task_flow_num { get; set; } = "";
        public int finish { get; set; } = 0;
        public string ticket_code { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;
        public string pay_method { get; set; } = "微信支付";
        public string pay_memo { get; set; } = "";
        public string? pick_veri_code { get; set;} = null;
        [ForeignKey(nameof(Maintain.MaintainLog.task_id))]
        public List<Maintain.MaintainLog> taskLog { get; set; }

        [NotMapped]
        public string status { get; set; }

        //[NotMapped]
        
        public OrderOnline? order { get; set; }

        [NotMapped]
        public string description { get; set; } = "";

        [NotMapped]
        public Maintain.MaintainLog[] log { get; set; }
        [NotMapped]
        public MemberSocialAccount staffMsa {get; set;}
        
        public string outStatus
        {
            get
            {
                string s = "未开始";
                if (log != null && log.Length > 0)
                {
                    s = "已开始";
                    if (log[log.Length - 1].step_name.Trim().Equals("发板")
                        || log[log.Length - 1].step_name.Trim().Equals("强行索回"))
                    {
                        s = log[log.Length - 1].step_name.Trim();
                    }
                    else
                    {
                        s = "进行中";
                    }
                }
                return s;

            }
        }

        //附加费用商品编号
        public int AddtionalFeeProductId
        {
            get
            {
                return 146;
            }
        }

        public string staffRecept
        {
            get
            {
                string name = "";
                if (staffMsa != null && staffMsa.member != null)
                {
                    name = staffMsa.member.real_name.Trim();
                }
                return name.Trim();
            }
        }
        public string staffSafe
        {
            get
            {
                return getStaffFromLog("安全检查");
            }
        }
        public string staffEdge
        {
            get
            {
                return getStaffFromLog("修刃");
            }
        }
        public string staffVax
        {
            get
            {
                return getStaffFromLog("打蜡");
            }
        }
        public string staffUnVax
        {
            get
            {
                return getStaffFromLog("刮蜡");
            }
        }
        public string staffRepair
        {
            get
            {
                return getStaffFromLog("维修");
            }
        }
        public string staffGiveOut
        {
            get
            {
                return getStaffFromLog("发板");
            }
        }
        private string getStaffFromLog(string step)
        {
            string name = "";
            if (taskLog != null)
            {
                List<Maintain.MaintainLog> l = taskLog.Where(l => l.step_name.Trim().Equals(step)).ToList();
                if (l.Count > 0)
                {
                    name = l[0].msa.member.real_name.Trim();
                }
            }
            return name.Trim();
        }


    }
}

