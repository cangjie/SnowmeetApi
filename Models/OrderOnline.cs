using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
namespace SnowmeetApi.Models
{
    [Table("order_online")]
    public class OrderOnline
    {
        [Key]
        public int id { get; set; }
        public int pay_state { get; set; }
    }
}
