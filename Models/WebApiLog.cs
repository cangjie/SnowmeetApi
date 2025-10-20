using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("web_api_log")]
    public class WebApiLog
    {
        [Key]
        public int id {get; set;}
        public string source {get; set;}
        public string purpose {get; set;}
        public string memo {get; set;}
        public string method {get; set;}
        public string request_url {get; set;}
        public string header {get; set;}
        public string payload {get; set;}
        public string response {get; set;}
        //public int status_code {get; set;}
        public int deal {get; set;}
        public DateTime? update_date {get; set;} = null;
        public DateTime create_date {get; set;} = DateTime.Now;
    }
}