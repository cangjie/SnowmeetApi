using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using SnowmeetApi.Models.Users;
namespace SnowmeetApi.Models.School
{
    [Table("school_staff")]
    public class Staff
    {
        [Key]
        public int id {get; set;}
	    public int? member_id {get; set;}
	    public string school_name {get; set;}
	    public string? sub_school_name {get; set;}
	    public string? team {get; set;}

        public string? temp_filled_name {get; set;} 
        public string? temp_filled_cell {get; set;}
        public string? temp_filled_gender {get; set;}

        public string avatar {get; set;}
	    public string role {get; set;}
	    public DateTime create_date {get; set;}

        public string? grade {get; set;}

        public string? ski_level {get; set;}

        public string? board_level {get; set;}

        [NotMapped]
        public Member member {get; set;}
    }
}