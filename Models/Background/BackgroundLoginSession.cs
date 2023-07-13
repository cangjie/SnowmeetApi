using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SnowmeetApi.Models.Background
{
    [Table("background_login_session")]
    public class BackgroundLoginSession
    {
        [Key]
        public long timestamp { get; set; }

        public string session_key { get; set; } = "";

    }
}

