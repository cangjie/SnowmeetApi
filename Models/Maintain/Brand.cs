using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Maintain
{
    [Table("brand_list")]
    public class Brand
    {
        public string brand_type { get; set; }
        public string brand_name { get; set; }
        public string chinese_name { get; set; } = "";
        public string origin { get; set; } = "";
    }
}

