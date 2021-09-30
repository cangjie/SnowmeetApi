using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.rfid
{
    [Table("rfid_command")]
    public class Command
    {
        public Command()
        {
        }
        [Key]
        public int id { get; set; }

        public string device_id { get; set; }
        public string command { get; set; }
        public string data { get; set; }
        public int deal { get; set; }

    }
}
