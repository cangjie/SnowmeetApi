using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
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
        public DateTime? use_date { get; set; }
        public int used { get; set; } = 0;
        // 生产库 card.create_date 是 NOT NULL 且无默认值（2025-07-09 迁移加的）。模型不映射这一列，
        // EF 的 INSERT 就不带它，所有往 card 表插券号的入口（会员详情页发券、教练批量发券等）都会 500。
        public DateTime create_date { get; set; } = DateTime.Now;

    }
}
