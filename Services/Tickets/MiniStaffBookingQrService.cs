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
        return await RenderAsync("s=" + staffId, envVersion);
    }

    // 分销商（教练 / 机构）专属码：scene 用 d= 与员工的 s= 区分，落地同一个雪票选购页。
    public async Task<byte[]> GetForDistributorAsync(int introducerId, string envVersion = "release")
    {
        if (introducerId <= 0) throw new ArgumentException("分销商编号无效");
        return await RenderAsync("d=" + introducerId, envVersion);
    }

    public const string BookingPage = "pages/ski_pass/ski_pass_selector";
    public const string CoachClaimPage = "pages/coach/claim/claim";
    public const string CouponClaimPage = "pages/coach/coupon_claim/coupon_claim";
    public const string MemberWechatBindPage = "pages/member/wechat_bind";

    public Task<byte[]> GetForMemberWechatBindAsync(string token, string envVersion = "release")
    {
        if (token == null || !System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9a-f]{24}$"))
            throw new ArgumentException("绑定码无效");
        return RenderAsync(MemberWechatBindPage, token, envVersion);
    }

    // 机构会籍卡分享码：顾客扫码落到领取页，scene = t=<卡 token>（见 CoachCardRules.ClaimScene）。
    // 每次分享一张新卡、一个新 token，所以每次分享二维码都不同；二维码本身不带任何身份，领取时才认登录的会员。
    public async Task<byte[]> GetForCoachCardAsync(string token, string envVersion = "release")
    {
        string t = (token ?? "").Trim();
        if (t.Length < 8 || t.Length > 30 || !System.Text.RegularExpressions.Regex.IsMatch(t, "^[0-9a-zA-Z]+$"))
            throw new ArgumentException("会籍卡分享码无效");
        return await RenderAsync(CoachClaimPage, SnowmeetApi.Helpers.CoachCardRules.ClaimScene(t), envVersion);
    }

    // 分销商优惠券批次的分享码：顾客扫码落到领取页，scene = c=<批次 token>（一个批次一个码，转发给个人或放进群里都用它）。
    public async Task<byte[]> GetForCouponBatchAsync(string token, string envVersion = "release")
    {
        string t = (token ?? "").Trim();
        if (t.Length < 8 || t.Length > 30 || !System.Text.RegularExpressions.Regex.IsMatch(t, "^[0-9a-zA-Z]+$"))
            throw new ArgumentException("优惠券分享码无效");
        return await RenderAsync(CouponClaimPage, "c=" + t, envVersion);
    }

    private Task<byte[]> RenderAsync(string scene, string envVersion) => RenderAsync(BookingPage, scene, envVersion);

    private async Task<byte[]> RenderAsync(string page, string scene, string envVersion)
    {
        envVersion = (envVersion ?? "release").Trim();
        if (envVersion != "release" && envVersion != "trial" && envVersion != "develop")
            throw new ArgumentException("小程序版本无效");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string token = await tokens.GetTokenAsync(attempt > 0);
            if (string.IsNullOrWhiteSpace(token)) throw new InvalidOperationException("获取小程序凭据失败");
            string body = JsonSerializer.Serialize(new { page, scene,
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
