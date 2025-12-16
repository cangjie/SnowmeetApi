using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("order_share_relation")]
    public class OrderShareRelation
    {
        [Key]
        public int id {get; set;}
        public string type {get; set;}
        public int? staff_id {get; set;} = null;
        public string name {get; set;}
        public string? wepay_account_type {get; set;} = null; 
        public string? wepay_account_num {get; set;} = null;
        public string? wepay_account_name {get; set;} = null;
        public string? ali_account_type {get; set;} = null;
        public string? ali_account_num {get; set;} = null;
        public bool valid {get; set;}
        public DateTime create_date {get; set;} = DateTime.Now;
        [ForeignKey(nameof(ShareRelationBind.share_relation_id))]
        public List<ShareRelationBind> binds {get; set;}
    }
    [Table("share_relation_bind")]
    public class ShareRelationBind
    {
        [Key]
        public int id {get; set;}
        public int share_relation_id {get; set;}
        public string pay_method {get; set;}
        public int? wepay_key_id {get; set;} = null;
        public bool? ali_account_id {get; set;} = null;
        public bool valid {get; set;}
        public DateTime? update_date {get; set;}
        public DateTime create_date {get; set; } = DateTime.Now;
    }
}