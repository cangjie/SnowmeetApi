using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models;

[Table("card_order_refund_card")]
public class CardOrderRefundCard
{
    [Key] public int id { get; set; }
    public int batch_id { get; set; }
    public int card_id { get; set; }
}
