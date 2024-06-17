using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("ali_download_flowbill")]
    public class AliDownloadFlowBill
    {
        [Key]
        public string id {get; set;}
        public string biz_num {get; set;}
        public string out_trade_num {get; set;}
        public string prod_name {get; set;}
        public DateTime trans_date {get; set;}
        public string receiver_ali_account {get; set;}
        public double income {get; set;}
        public double outcome {get; set;}

        public double remainder {get; set;}

        public string trans_channel {get; set;}

        public string biz_type {get; set;}

        public string memo {get; set;}

    }

}