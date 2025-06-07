namespace SnowmeetApi.Models
{
    public class WebsocketPost<T>
    {
        public string command { get; set; }
        public int? id { get; set; }
        public string? code { get; set; }
        public string? sessionKey { get; set; } = null;
        public string? sessionType { get; set; } = null;
        public T? data { get; set; }

    }
}