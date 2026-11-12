using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SixLabors.ImageSharp;

namespace SnowmeetApi.Services.Tickets;

// 员工专属小程序码：员工身份由控制器的登录态提供，不接受客户端指定员工。
public sealed class MiniStaffBookingQrService(IMiniTicketTokenProvider tokens, HttpClient http)
{
    public async Task<byte[]> GetAsync(int staffId, string envVersion = "release")
    {
        if (staffId <= 0) throw new ArgumentException("员工编号无效");
        envVersion = (envVersion ?? "release").Trim();
        if (envVersion != "release" && envVersion != "trial" && envVersion != "develop")
            throw new ArgumentException("小程序版本无效");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await tokens.GetTokenAsync(attempt > 0);
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("获取小程序凭据失败");
            string body = JsonSerializer.Serialize(new { page = "pages/ski_pass/ski_pass_selector", scene = "s=" + staffId,
                env_version = envVersion, check_path = envVersion == "release", width = 430 });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await http.PostAsync("https://api.weixin.qq.com/wxa/getwxacodeunlimit?access_token=" + Uri.EscapeDataString(token), content);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("小程序码服务失败：HTTP " + (int)response.StatusCode);
            byte[] bytes = await response.Content.ReadAsByteArrayAsync();
            string text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            if (text.StartsWith("{"))
            {
                using var error = JsonDocument.Parse(text);
                int code = error.RootElement.TryGetProperty("errcode", out var c) ? c.GetInt32() : -1;
                if (attempt == 0 && (code == 40001 || code == 40014 || code == 42001)) continue;
                throw new InvalidOperationException("微信小程序码生成失败（" + code + "）");
            }
            try
            {
                using Image image = Image.Load(bytes);
                if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71) return bytes;
                using var output = new MemoryStream();
                image.SaveAsPng(output);
                return output.ToArray();
            }
            catch (Exception e) { throw new InvalidOperationException("小程序码图片无效", e); }
        }
        throw new InvalidOperationException("小程序码生成失败");
    }
}
