using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;
namespace SnowmeetApi.Models
{
    [Table("order_online_temp")]
    public class OrderOnlineTemp
    {
        
        public OrderOnlineTemp()
        {
        }

        [Key]
        public int id { get; set; }

        public int? online_order_id { get; set; }
    }
}
