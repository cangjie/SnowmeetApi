using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Collections.Generic;
namespace SnowmeetApi.Models.School
{
    [Table("school_course_student")]
    public class CourseStudent
    {
        [Key]
        public int id {get; set;}
        public int course_id {get; set;}
        public int? member_id {get; set;}
        public string cell {get; set;}
        public string name {get; set;}
        public string gender {get; set;}
        public string adult_type {get; set;}
        public string video_url {get; set;} = "";
        public string trainer_remark {get; set;} = "";
        public string student_comment {get; set;} = "";
        public DateTime update_date {get; set;} = DateTime.Now;
        public DateTime create_date {get; set;} = DateTime.Now;

        
    }
}