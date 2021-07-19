using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models.Users
{
    [Table("m_token")]
    public class MToken
    {
        [Key]
        public string token { get; set; }
        public int isvalid { get; set; }
        public DateTime expire { get; set; }
        public string open_id { get; set; }


        public static string GetOpenId(string token)
        {
            return "";
        }
    }

    
}
