using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SnowmeetApi.Models
{
    [Table("admin_ai_request_log")]
    public class AdminAiRequestLog
    {
        [Key]
        public long id { get; set; }
        public int staff_id { get; set; }
        public string session_type { get; set; } = "";
        public string trace_id { get; set; } = "";
        public string provider { get; set; } = "reqai";
        public string operation { get; set; } = "";
        public string page_key { get; set; } = "";
        public string request_url { get; set; } = "";
        public string request_payload { get; set; } = "";
        public string? response_payload { get; set; }
        public string? response_headers { get; set; }
        public int? response_status_code { get; set; }
        public string? model { get; set; }
        public string? effort { get; set; }
        public string? reqai_invocation_id { get; set; }
        public string? usage { get; set; }
        public string? error_message { get; set; }
        public int? duration_ms { get; set; }
        public bool success { get; set; }
        public DateTime create_date { get; set; } = DateTime.Now;
        public DateTime? completed_date { get; set; }
    }
}