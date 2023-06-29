using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SnowmeetApi.Models.Users
{
    [Table("mini_session")]
    public class MiniSession
    {
        [Key]
        public string session_key { get; set; }

        public string open_id { get; set; }

        public string session_type { get; set; } = "";
        public DateTime create_date { get; set; } = DateTime.Now;

        /*

        public static Data.ApplicationDBContext _context;

        public static string GetOpenId(string sessionKey)
        {
            MiniSession session = _context.MiniSessons.Find(sessionKey);
            if (session == null)
            {
                return "";
            }
            else
            {
                return session.open_id.Trim();
            }
            

        }
        */
    }
}
