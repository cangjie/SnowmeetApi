using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models
{
    [Table("print_task")]
    public class PrintTask
    {
        [Key]
        public int id { get; set; }
        public string shop { get; set; }
        public string? biz_type { get; set; } = null;
        public int? biz_id { get; set; } = null;
        public string? color { get; set; } = null;
        public int fetched { get; set; } = 0;
        public int printed { get; set; } = 0;
        public DateTime? update_date { get; set; } = null;
        public DateTime create_date { get; set; } = DateTime.Now;
	}
}