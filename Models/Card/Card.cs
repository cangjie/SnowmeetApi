using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Card
{
    [Table("card")]
    public class Card
    {
        [Key]
        public string card_no { get; set; }
        public int is_ticket { get; set; } = 0;
        public string type { get; set; } = "";
        public string use_memo { get; set; } = "";

        public string owner_open_id { get; set; } = "";
        public int is_package { get; set; } = 0;
        public int product_id { get; set; } = 0;


    }
}
