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
        public int is_ticket { get; set; }
        public string type {get;set;}
        public string use_memo { get; set; }

    }
}
