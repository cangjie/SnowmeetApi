using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.rfid
{
    [Table("rfid_scan")]
    public class Scan
    {
        public Scan()
        {
        }
        [Key]
        public int id { get; set; }
        public string device_id { get; set; }
        public string scan_data { get; set; }
        public int deal { get; set; }


    }
}
