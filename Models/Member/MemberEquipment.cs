using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("member_equipment")]
    public class MemberEquipment
    {
        [Key]
        public int id { get; set; }
        public int member_id { get; set; }
        [MaxLength(20)]
        public string equipment { get; set; } = "";
        [MaxLength(100)]
        public string? brand { get; set; }
        [MaxLength(50)]
        public string? scale { get; set; }
        [MaxLength(200)]
        public string? serials { get; set; }
        public int? image_id { get; set; }
        public int? source_care_id { get; set; }
        public int valid { get; set; } = 1;
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? update_date { get; set; }

        [ForeignKey("image_id")]
        public UploadFile? image { get; set; }
    }
}
