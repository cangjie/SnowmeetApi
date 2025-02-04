using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using Microsoft.DotNet.Scaffolding.Shared.Messaging;

namespace SnowmeetApi.Models.SkiPass
{
    [Table("ziwoyou_order")]
    public class ZiwoyouListOrder
    {
        public DateTime cancelDate { get; set; }
        public DateTime endTravelDate { get; set; }
        public int finishNum { get; set; }
        public int isConfirm { get; set; }
        public int isOnlinepay { get; set; }
        public string linkAddress { get; set; }
        public string linkCreditNo { get; set; }
        public int linkCreditType { get; set; }
        public string linkEmail { get; set; }
        public string linkMan { get; set; }
        public string linkPhone { get; set; }
        public double marketPrice { get; set; }
        public double memOrderMoney { get; set; }
        public int num { get; set; }
        public DateTime orderDate { get; set; }
        [Key]
        public string orderId { get; set; }
        public string orderMemo { get; set; }
        public string orderSourceId { get; set; }
        public int orderState { get; set; }
        public string orderState2 { get; set; }
        public string productName { get; set; }
        public int productNo { get; set; }
        public double salePrice { get; set; }
        public double settlementPrice { get; set; }
        public DateTime travelDate { get; set; }
        public DateTime? update_date{get; set;}
        public DateTime? create_date {get; set;}
        [ForeignKey(nameof(Models.SkiPass.SkiPass.reserve_no))]
        public List<Models.SkiPass.SkiPass> skipasses {get; set;}
    }

}