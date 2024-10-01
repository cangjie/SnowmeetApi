using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
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
	    public string role {get; set;}
	    public DateTime create_date {get; set;}
    }
}