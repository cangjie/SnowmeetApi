using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
using SnowmeetApi.Models.School;
namespace SnowmeetApi.Models.School
{
    [Table("school_course")]
    public class Course
    {
        [Key]
        public int id {get; set;}
        public int oper_member_id {get; set;}
        public string oper_name {get; set;}
        public string oper_cell {get; set;}
        public int trainer_member_id {get; set;}
        public string trainer_name {get; set;}
        public string trainer_cell {get; set;}
        public string name {get; set;}
        public string type {get; set;}
        public DateTime course_date {get; set;}
        public string time_length {get; set;}
        public string course_content {get; set;}
        public string? wanlong_no {get; set;}
        public DateTime update_date {get; set;} = DateTime.Now;
        public DateTime create_date {get; set;} = DateTime.Now;

        [NotMapped]
        public List<CourseStudent> courseStudents  {get; set;}
    }
}