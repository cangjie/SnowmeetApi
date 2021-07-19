using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace SnowmeetApi.Models
{
    [Table("school_lesson")]
    public class SchoolLesson
    {
        [Key]
        public int id { get; }
        public string open_id { get; set; }
        public string cell_number { get; set; }
        public string name { get; set; }
        public string gender { get; set; }
        public string student_name { get; set; }
        public string student_cell_number { get; set; }
        public string student_gender { get; set; }
        public string student_relation { get; set; }
        public string demand { get; set; }
        public string resort { get; set; }
        public DateTime lesson_date { get; set; }
        
        public string training_plan { get; set; }
        public string pay_method { get; set; }
        public int order_id { get; set; }
        public int pay_state { get; set; }
        public string memo { get; set; }
        public DateTime create_date { get; }

        public string instructor_open_id { get; set; }

        public SchoolStaff instructor { get;  }

        public static explicit operator Task<object>(SchoolLesson v)
        {
            throw new NotImplementedException();
        }
    }
}
