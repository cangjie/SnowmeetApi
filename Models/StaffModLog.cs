using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aop.Api.Domain;
namespace SnowmeetApi.Models
{
    [Table("staff_mod_log")]
    public class StaffModLog
    {
        [Key]
        public int id { get; set; }
        public string table_name {get; set;}
        public string field_name {get; set;}
        public string key_id {get; set;}
        public string scene {get; set;}
        public int staff_member_id {get; set;}
        public string? prev_value {get; set;}
        public string? current_value {get; set;}
        public DateTime create_date {get; set;}
        [ForeignKey("staff_member_id")]
        public Models.Users.Member? staffMember {get; set;}
    }
}