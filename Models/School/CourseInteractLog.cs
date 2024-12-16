using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace wl_schoool_core.Models.School
{
    [Table("school_course_student_interact_log")]
    public class CourseInteractLog
    {
        [Key]
        public int id { get; set; }
        public int course_student_id { get; set; }
        public string act { get; set; }
        public string open_id { get; set; }
        public DateTime create_date { get; set; }
    }
}
