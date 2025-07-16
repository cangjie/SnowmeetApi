using System;

namespace SnowmeetApi.Models
{
    public class WebsocketPost<T>
    {
        public string command { get; set; }
        public int? id { get; set; } = null;
        public string? code { get; set; } = null;
        public string? sessionKey { get; set; } = null;
        public string? sessionType { get; set; } = null;
        public T? data { get; set; }

    }
    public class ShopPrintTask
    {
        public string shop { get; set; }
        public DateTime startDate { get; set; }
    }
}