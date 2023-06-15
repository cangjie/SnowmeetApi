using System;
using Microsoft.Extensions.Configuration;
namespace SnowmeetApi.Models
{
    public class Settings
    {
        public string? appId { get; set; }
        public string? appSecret { get; set; }
        public string? originalId { get; set; }
        public string? token { get; set; }
        public string? mediaRoot { get; set; }
        public string? tiktokDomain { get; set;}
        public string? tiktokAppId { get; set; }
        public string? tiktokAppSecret { get; set; }


        public static Settings GetSettings(IConfiguration config)
        {
            IConfiguration settings = config.GetSection("Settings");
            string appId = settings.GetSection("AppId").Value.Trim();
            string appSecret = settings.GetSection("AppSecret").Value.Trim();
            string originalId = settings.GetSection("OriginalId").Value.Trim();
            string token = settings.GetSection("token").Value.Trim();
            string tiktokDomain = settings.GetSection("TiktokDomain").Value.Trim();
            string tiktokAppId = settings.GetSection("TiktokAppId").Value.Trim();
            string tiktokAppSecret = settings.GetSection("TiktokAppSecret").Value.Trim();
            //string mediaRoot = settings.GetSection("MediaRoot").Value.Trim();

            return new Settings()
            {
                appId = appId,
                appSecret = appSecret,
                originalId = originalId,
                token = token,
                tiktokDomain = tiktokDomain,
                tiktokAppId = tiktokAppId,
                tiktokAppSecret = tiktokAppSecret
                //mediaRoot = mediaRoot
            };
        }
    }
}
