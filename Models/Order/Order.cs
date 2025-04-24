using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("order")]
    public class Order
    {
        [Key]
        public int id { get; set; }
    }
}