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
        public string? video_url {get; set;} = "";
        public string before_level {get; set;} = "";

        public string after_level {get; set;} = "";
        public string background {get; set;} = "";
        public string progress {get; set;} = "";
        public string emotion {get; set;} = "";
        public string next_plan {get; set;} = "";
        public string? images {get; set;} = "";
        public string student_comment {get; set;} = "";
        public int del {get; set;} = 0;
        public int share_times { get; set; } = 0;
        public int read_times { get; set;} = 0;
        public DateTime update_date {get; set;} = DateTime.Now;
        public DateTime create_date {get; set;} = DateTime.Now;

        [NotMapped]
        public Course course {get; set;} = null;

       
        public bool haveEvaluated
        {
            get
            {
                if (background.Trim().Equals("") || progress.Trim().Equals("")
                || emotion.Trim().Equals("") || next_plan.Trim().Equals(""))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        public bool haveImages
        {
            get
            {
                
                if (images != null && images.Trim().Equals(""))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        public bool haveVideo
        {
            get
            {
                if (video_url != null && video_url.Trim().Equals(""))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }

        
    }
}