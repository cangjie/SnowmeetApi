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
        public int id { get; set; }
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
        public string videos { get; set; }
        public DateTime create_date { get; set; }
        public string instructor_open_id { get; set; }
        public string assistant { get; set; }
        public double training_fee { get; set; }
        public double rent_fee { get; set; }
        public double ticket_fee { get; set; }
        public double others_fee { get; set; }

        public string status
        {
            get
            {
                string state = "";
                if (open_id.Trim().Equals(""))
                {
                    state = "未打开";
                }
                else if (order_id == 0)
                {
                    state = "未支付";
                }
                else if (pay_state == 0)
                {
                    state = "支付未成功";
                }
                else
                {
                    state = "已支付";
                }
                return state.Trim();
            }
        }
        

        public static explicit operator Task<object>(SchoolLesson v)
        {
            throw new NotImplementedException();
        }
    }
}
