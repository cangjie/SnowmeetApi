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

        public int del {get; set;} = 0;

        public DateTime update_date {get; set;} = DateTime.Now;
        public DateTime create_date {get; set;} = DateTime.Now;

        [NotMapped]
        public string? oper_role {get; set;} = null;

        [NotMapped]
        public Staff oper {get; set;} = null;

        [NotMapped]
        public Staff staff {get; set;} = null;

        [NotMapped]
        public List<CourseStudent> courseStudents  {get; set;}
        
        [NotMapped]
        public int studentCount {get; set;} = 0;

        public bool haveEvaluated
        {
            get
            {
                bool ret = true;
                foreach(CourseStudent student in courseStudents)
                {
                    if (!student.haveEvaluated)
                    {
                        ret = false;
                        break;
                    }
                }
                return ret;
            }
        }

        public bool haveImages
        {
            get
            {
                bool ret = true;
                foreach(CourseStudent student in courseStudents)
                {
                    if (!student.haveImages)
                    {
                        ret = false;
                        break;
                    }
                }
                return ret;
            }
        }

        public bool haveVideo
        {
            get
            {
                bool ret = true;
                foreach(CourseStudent student in courseStudents)
                {
                    if (!student.haveVideo)
                    {
                        ret = false;
                        break;
                    }
                }
                return ret;
            }
        }
    }
}