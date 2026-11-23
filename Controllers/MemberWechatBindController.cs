using System;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class MemberWechatBindController(ApplicationDBContext db) : ControllerBase
{
    private const string Scene = "会员绑定微信";
    private const string SessionType = "wechat_mini_openid";

    private static ApiResult<object> Fail(string message) => new() { code = 1, message = message };
    private static ApiResult<object> OkData(object data) => new() { code = 0, message = "", data = data };

    private async Task<Staff?> StaffFor(string sessionKey)
    {
        var staff = await Util.GetStaffBySessionKey(db, Util.UrlDecode(sessionKey ?? ""), SessionType);
        return staff != null && staff.valid == 1 && staff.title_level >= 100 ? staff : null;
    }

    /// <summary>
    /// 绑定二维码有效期（分钟）。2026-11-23 用户：由 2 分钟改成 30 分钟（2 分钟来不及：顾客扫码、等小程序加载、授权手机号）。
    /// 二维码一次性，绑定成功或店员重新生成后旧码作废；店员页的提示文字（member_detail.js）要和这个数一致。
    /// </summary>
    public const int QrValidMinutes = 30;

    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> Create(int memberId, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return Ok(Fail("没有权限"));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await db.member.AnyAsync(m => m.id == memberId && m.valid == 1))
            return Ok(Fail("会员不存在"));
        if (!await db.memberSocialAccount.AnyAsync(a => a.member_id == memberId && a.valid == 1
            && a.type == MemberSocialAccount.TYPE_CELL && a.num != null && a.num.Trim() != ""))
            return Ok(Fail("会员未绑定手机号，请先完善手机号"));
        if (await db.memberSocialAccount.AnyAsync(a => a.member_id == memberId && a.valid == 1
            && a.type == SessionType && a.num != null && a.num.Trim() != ""))
            return Ok(Fail("会员已绑定微信"));
        var oldCodes = await db.scanQrCode.AsTracking().Where(q => q.staff_id == staff.id
            && q.scene == Scene && q.purpose == memberId.ToString() && q.authed == 0 && q.stoped == 0).ToListAsync();
        foreach (var old in oldCodes) old.stoped = 1;
        var id = (await db.scanQrCode.MaxAsync(q => (int?)q.id) ?? 0) + 1;
        var qr = new ScanQrCode
        {
            id = id, code = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),
            staff_id = staff.id, platform = "wechat_mini", scene = Scene, purpose = memberId.ToString(),
            expire_time = DateTime.Now.AddMinutes(QrValidMinutes)
        };
        db.scanQrCode.Add(qr);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        // token：绑定码本身，只返回给创建它的店员，用来拼「转发给客人」的小程序卡片路径（二维码里装的也是它）
        return Ok(OkData(new { id = qr.id, expireTime = qr.expire_time, token = qr.code }));
    }

    private async Task<ScanQrCode?> StaffQr(int id, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return null;
        return await db.scanQrCode.AsNoTracking().FirstOrDefaultAsync(q =>
            q.id == id && q.staff_id == staff.id && q.scene == Scene);
    }

    [HttpGet]
    public async Task<IActionResult> Image(int id, string sessionKey, [FromServices] MiniStaffBookingQrService qrService,
        string envVersion = "release")
    {
        var qr = await StaffQr(id, sessionKey);
        if (qr == null || qr.authed != 0 || qr.stoped != 0 || qr.expire_time <= DateTime.Now)
            return BadRequest(new { message = "二维码无效或已过期" });
        Response.Headers.CacheControl = "no-store";
        return File(await qrService.GetForMemberWechatBindAsync(qr.code, envVersion), "image/png");
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<object>>> Status(int id, string sessionKey)
    {
        var qr = await StaffQr(id, sessionKey);
        if (qr == null) return Ok(Fail("二维码不存在或没有权限"));
        string stage = qr.authed == 1 ? "bound" :
            qr.stoped == 1 || qr.expire_time <= DateTime.Now ? "expired" : "waiting";
        return Ok(OkData(new { stage }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> Cancel(int id, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return Ok(Fail("没有权限"));
        var qr = await db.scanQrCode.AsTracking().FirstOrDefaultAsync(q =>
            q.id == id && q.staff_id == staff.id && q.scene == Scene);
        if (qr == null) return Ok(Fail("二维码不存在或没有权限"));
        if (qr.authed == 0) { qr.stoped = 1; qr.update_date = DateTime.Now; await db.SaveChangesAsync(); }
        return Ok(OkData(new { stopped = true }));
    }

    public sealed class BindRequest
    {
        public string token { get; set; } = "";
        public string phoneCode { get; set; } = "";
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> Bind([FromBody] BindRequest body, string sessionKey,
        [FromServices] IMiniTicketTokenProvider tokens, [FromServices] IHttpClientFactory clients)
    {
        if (body == null || !Regex.IsMatch(body.token ?? "", "^[0-9a-f]{24}$")
            || string.IsNullOrWhiteSpace(body.phoneCode))
            return Ok(Fail("二维码或手机号授权无效"));
        sessionKey = Util.UrlDecode(sessionKey ?? "");
        var session = await db.miniSession.AsNoTracking().FirstOrDefaultAsync(s =>
            s.session_key == sessionKey && s.session_type == SessionType && s.valid == 1 && s.expire_date > DateTime.Now);
        if (session == null || string.IsNullOrWhiteSpace(session.wechat_openid))
            return Ok(Fail("登录已失效，请重新扫码"));
        var candidate = await db.scanQrCode.AsNoTracking().FirstOrDefaultAsync(q =>
            q.code == body.token && q.scene == Scene && q.authed == 0 && q.stoped == 0 && q.expire_time > DateTime.Now);
        if (candidate == null) return Ok(Fail("二维码已失效，请让店员重新生成"));

        var accessToken = await tokens.GetTokenAsync();
        // 向微信换手机号。⚠️ 不能用 PostAsJsonAsync：它的请求体没有 Content-Length、按 chunked 发送，微信对 chunked 一律回 412
        // （2026-11-23 顾客授权手机号后绑定接口 500 的根因，用 curl 加 Transfer-Encoding: chunked 复现）；用 StringContent（带长度）。
        // 微信回非 2xx、网络出错、回的不是 JSON，都当作「手机号授权验证失败」返回，不能让它变成 500。
        JsonDocument result;
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { code = body.phoneCode }), Encoding.UTF8, "application/json");
            using var response = await clients.CreateClient().PostAsync(
                "https://api.weixin.qq.com/wxa/business/getuserphonenumber?access_token=" + Uri.EscapeDataString(accessToken), content);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("[绑定微信] 微信换手机号返回 HTTP " + (int)response.StatusCode);
                return Ok(Fail("手机号授权验证失败，请稍后重试"));
            }
            result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        catch (Exception err) when (err is HttpRequestException or JsonException or TaskCanceledException)
        {
            Console.WriteLine("[绑定微信] 微信换手机号出错 " + err.GetType().Name + " " + err.Message);
            return Ok(Fail("手机号授权验证失败，请稍后重试"));
        }
        using var _ = result;
        var root = result.RootElement;
        if (!root.TryGetProperty("errcode", out var error) || error.GetInt32() != 0
            || !root.TryGetProperty("phone_info", out var info)
            || !info.TryGetProperty("purePhoneNumber", out var number)
            || !info.TryGetProperty("countryCode", out var country))
            return Ok(Fail("手机号授权验证失败，请重新授权"));
        var phone = number.GetString()?.Trim();
        var countryCode = country.GetString()?.Trim();
        if (phone == null || countryCode == null || !Regex.IsMatch(phone, "^[0-9]{6,15}$")
            || !Regex.IsMatch(countryCode, "^[1-9][0-9]{0,3}$"))
            return Ok(Fail("手机号授权验证失败，请重新授权"));
        var international = "+" + countryCode + phone;
        var international00 = "00" + countryCode + phone;

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var qr = await db.scanQrCode.AsTracking().FirstOrDefaultAsync(q => q.id == candidate.id && q.code == body.token
            && q.scene == Scene && q.authed == 0 && q.stoped == 0 && q.expire_time > DateTime.Now);
        if (qr == null || !int.TryParse(qr.purpose, out int memberId))
            return Ok(Fail("二维码已失效，请让店员重新生成"));
        var currentSession = await db.miniSession.AsTracking().FirstOrDefaultAsync(s =>
            s.session_key == sessionKey && s.session_type == SessionType && s.valid == 1 && s.expire_date > DateTime.Now);
        if (currentSession == null || currentSession.wechat_openid != session.wechat_openid)
            return Ok(Fail("登录已失效，请重新扫码"));
        if (!await db.member.AnyAsync(m => m.id == memberId && m.valid == 1))
            return Ok(Fail("会员不存在"));
        if (!await db.memberSocialAccount.AnyAsync(a => a.member_id == memberId && a.valid == 1
            && a.type == MemberSocialAccount.TYPE_CELL && (a.num.Trim() == international
                || a.num.Trim() == international00 || (countryCode == "86" && a.num.Trim() == phone))))
            return Ok(Fail("授权手机号与会员手机号不一致"));
        var openid = currentSession.wechat_openid.Trim();
        var unionid = currentSession.wechat_unionid?.Trim();
        if ((currentSession.member_id != null && currentSession.member_id != memberId)
            || await db.memberSocialAccount.AnyAsync(a => a.valid == 1 && a.member_id != memberId
                && ((a.type == SessionType && a.num.Trim() == openid)
                    || (unionid != null && unionid != "" && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && a.num.Trim() == unionid)))
            || await db.socialAccountForJob.AnyAsync(a => a.wechat_mini_openid == openid && a.member_id != memberId))
            return Ok(Fail("该微信已绑定其他会员，请联系管理员核对"));
        if (await db.memberSocialAccount.AnyAsync(a => a.member_id == memberId && a.valid == 1
            && a.type == SessionType && a.num != null && a.num.Trim() != ""))
            return Ok(Fail("会员已绑定微信"));
        if (await db.memberSocialAccount.AnyAsync(a => a.member_id == memberId && a.valid == 1
            && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && a.num != null && a.num.Trim() != ""
            && (unionid == null || unionid == "" || a.num.Trim() != unionid)))
            return Ok(Fail("会员已有其他微信关联，请联系管理员核对"));
        db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId, type = SessionType, num = openid });
        if (!string.IsNullOrEmpty(unionid) && !await db.memberSocialAccount.AnyAsync(a => a.member_id == memberId
            && a.valid == 1 && a.type == MemberSocialAccount.TYPE_WECHAT_UNIONID && a.num.Trim() == unionid))
            db.memberSocialAccount.Add(new MemberSocialAccount { member_id = memberId, type = MemberSocialAccount.TYPE_WECHAT_UNIONID, num = unionid });
        currentSession.member_id = memberId;
        currentSession.cell = countryCode == "86" ? phone : international;
        qr.authed = 1;
        qr.scaned = 1;
        qr.scaner_member_id = memberId;
        qr.scan_time = DateTime.Now;
        qr.update_date = DateTime.Now;
        db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("member", "wechat_mini_openid", memberId,
            "会员扫码绑定微信", memberId, qr.staff_id, null, openid, "手机号授权核验"));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Ok(OkData(new { bound = true, memberId }));
    }
}
