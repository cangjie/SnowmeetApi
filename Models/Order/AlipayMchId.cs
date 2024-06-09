using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Order
{
    [Table("alipay_mch_id")]
    public class AlipayMchId
    {
        [Key]
        public int id { get; set; }

        
        public string app_id { get; set; }  
    }
}
