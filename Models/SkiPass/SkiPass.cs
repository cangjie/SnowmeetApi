using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Microsoft.DotNet.Scaffolding.Shared.Messaging;

namespace SnowmeetApi.Models.SkiPass
{
    [Table("ski_pass")]
    public class SkiPass
    {
        public int id {get; set;}
        public int member_id {get; set;}
        public string? wechat_mini_openid {get; set;}
        public int product_id {get; set;}
        public string resort {get; set;}
        public string product_name {get; set;}
        public int count {get; set;}
       
        public int? order_id {get; set;}
        public double? deal_price {get; set;}
        public double? ticket_price {get; set;}
        public double? deposit {get; set;}
        public double? fee {get; set;} = null;
        public double? refund_amount {get; set;} = null;
        public int? valid {get; set;} = 0;
        public int? have_refund {get; set;} = null;

        public string? card_no {get; set;} = null;

        public string? card_image_url {get; set;} = null;
        public DateTime? card_member_pick_time {get; set;} = null;
        public DateTime? card_member_return_time {get; set;} = null;

        public int? card_lost {get; set;} = null;
        public string? reserve_no {get; set;} = null;
        public string? qr_code_url {get; set;} = null;
        public DateTime? reserve_date {get; set;} = null;
        
        public string? contact_name {get; set;} = null;
        public string? contact_cell {get; set;} = null;
        public string? contact_id_type {get; set;} = null;
        public string? contact_id_no {get; set;} = null;

        /*
        0:未取消
        1:已取消
        2:取消中
        3:人工申请取消
        -1:取消失败
        -2:出票失败，自动取消
        -3:轮训发现已经取消

        */
        public int is_cancel { get; set; } = 0;
        public string? send_content { get; set; } = null;
        public int? cancel_member_id {get; set;}
        public string memo {get; set;} = "";
        public DateTime update_date {get; set;} = DateTime.Now;
        public DateTime create_date {get; set;} = DateTime.Now;
        public int is_used {get; set;} = 0;
        [ForeignKey("order_id")]
        public OrderOnline? order {get;set;}
        [NotMapped]
        public string cancelStatus
        {
            get
            {
                string status = "未知状态";
                switch(is_cancel)
                {
                    case 0:
                        status = "未取消";
                        break;
                    case 1:
                        status = "已取消";
                        break;
                    case 2:
                        status = "取消中";
                        break;
                    case 3:
                        status = "人工申请取消";
                        break;
                    case -1:
                        status = "取消失败";
                        break;
                    case -2:
                        status = "出票失败，自动取消";
                        break;
                    default:
                        break;
                }
                return status;
            }
        }
        public double cardFee
        {
            get
            {
                if (card_lost==1 && resort.Trim().Equals("南山")){
                    return 20;
                }
                else
                {
                    return 0;
                }
            }
        }

        public double needRefund
        {
            get
            {
                if (resort.Trim().Equals("南山"))
                {
                    return (double)deposit  - (fee==null?0:(double)fee) - cardFee;
                }
                else
                {
                    return 0;
                }
            }
        }

        public string status
        {
            get
            {
                string status = "";
                if (resort.Trim().Equals("南山"))
                {
                    status = "未付款";
                    if (valid == 1)
                    {
                        status = "已付款";
                        
                        if (card_no != null)
                        {
                            status = "已出票";
                            if (card_member_pick_time != null)
                            {
                                status = "已取卡";
                                if (card_member_return_time != null)
                                {
                                    status = "已还卡";
                                    if (have_refund == 1)
                                    {
                                        status = "已退押金";
                                    }
                                }
                            }
                        }

                    }
                    if (is_cancel == 1)
                    {
                        return "已退票";
                    }
                }
                else
                {
                    if (valid == 1)
                    {
                        status = "已付款";
                        if (reserve_no != null)
                        {
                            status = "已确认";
                            if (card_member_pick_time != null)
                            {
                                status = "已出票";
                                if (is_used == 1)
                                {
                                    status = "已取卡";
                                }
                            }
                        }
                        switch(is_cancel)
                        {
                            case 1:
                                status = "已取消";
                                break;
                            case 2:
                                status = "取消中";
                                break;
                            case 3:
                                status = "申请取消";
                                break;
                            case -1:
                                status = "取消失败";
                                break;
                            case -2:
                                status = "出票失败";
                                break;
                            default:
                                break;
                        }
                        if (have_refund == 1)
                        {
                            status = "已申请退款";
                        }
                        if (refund_amount != null)
                        {
                            status = "退款成功";
                        }

                    }
                }

                return status;
            }

        }

        public bool CanRefund
        {
            get
            {
                bool ret = true;
                if (resort.Trim().Equals("南山"))
                {
                    if (!status.Trim().Equals("已付款"))
                    {
                        ret = false;
                    }
                }
                else 
                {
                    if (status.Trim().Equals("已取卡"))
                    {
                        ret = false;
                    }
                }
                return ret;
            }
        }
    }
}