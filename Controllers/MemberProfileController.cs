using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Storage;

namespace SnowmeetApi.Controllers
{
    /// <summary>
    /// 顾客验证手机号之后授权头像和昵称（2026-11-14 用户要求）。顾客会话即可，不要求店员身份。
    ///   GET  GetMyProfile   要不要弹「完善资料」，以及已有的昵称、头像
    ///   POST SaveMyProfile  保存昵称（可选）和头像文件（可选，存 S3），写 member 的三列并刷新更新时间
    /// 会话没绑会员（还没验证手机号）时 GetMyProfile 只是返回「不用弹」，SaveMyProfile 返回 code 1。
    /// 规则（要不要弹、昵称清洗、头像类型）在 MemberProfileRules；三列在 MemberProfile 实体里，只用直接 UPDATE 写。
    /// </summary>
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MemberProfileController : ControllerBase
    {
        private readonly ApplicationDBContext _db;
        private readonly IFileStorage _storage;

        public MemberProfileController(ApplicationDBContext db, IFileStorage storage)
        {
            _db = db;
            _storage = storage;
        }

        [HttpGet]
        public async Task<ActionResult<ApiResult<MemberProfileView>>> GetMyProfile(string sessionKey, string sessionType = "wechat_mini_openid")
        {
            (bool _, int? memberId) = await FindSessionAsync(sessionKey, sessionType);
            if (memberId == null)
            {
                return Ok(Result(0, "", new MemberProfileView { needProfile = false }));
            }
            MemberProfile? profile = await _db.memberProfile.AsNoTracking().FirstOrDefaultAsync(p => p.member_id == memberId.Value);
            return Ok(Result(0, "", ToView(profile)));
        }

        public sealed class NicknameClickRequest
        {
            public string? sessionKey { get; set; }
            public string? sessionType { get; set; }
            public string? sourcePage { get; set; }
        }

        // 记录顾客实际聚焦昵称输入框，不代表已经选择微信昵称或保存成功。
        [HttpPost]
        public async Task<ActionResult<ApiResult<MemberProfileView>>> LogNicknameClick([FromBody] NicknameClickRequest? request)
        {
            try
            {
                (bool sessionOk, int? memberId) = await FindSessionAsync(request?.sessionKey, request?.sessionType);
                if (!sessionOk || memberId == null) return Ok(Result(1, "会话无效或未绑定会员"));
                string page = (request?.sourcePage ?? "").Trim();
                if (page.Length > 120 || !Regex.IsMatch(page, @"^pages/[a-zA-Z0-9_/-]+$")) page = "";
                var log = CoreDataModLog.CreateManualLog("member", "", memberId.Value,
                    "点击获取昵称", memberId.Value, null, null, null, page);
                await _db.coreDataModLog.AddAsync(log);
                await _db.SaveChangesAsync();
                return Ok(Result(0, ""));
            }
            catch (Exception ex)
            {
                Console.WriteLine("昵称点击记录失败：" + ex.Message);
                return Ok(Result(1, "记录失败"));
            }
        }

        // 选了头像：小程序用 wx.uploadFile 发 multipart；只改昵称：用 wx.request 发表单编码。
        // [ApiController] 看到 IFormFile 参数会把请求类型推断成只收 multipart（表单编码会 415），所以这里明确两种都收。
        [HttpPost]
        [Consumes("multipart/form-data", "application/x-www-form-urlencoded")]
        public async Task<ActionResult<ApiResult<MemberProfileView>>> SaveMyProfile(
            [FromQuery] string sessionKey,
            [FromQuery] string sessionType = "wechat_mini_openid",
            [FromForm] string? nickName = null,
            IFormFile? avatar = null)
        {
            (bool sessionOk, int? memberId) = await FindSessionAsync(sessionKey, sessionType);
            if (!sessionOk)
            {
                return Ok(Result(1, "登录信息失效，请重新进入小程序"));
            }
            if (memberId == null)
            {
                return Ok(Result(1, "请先验证手机号"));
            }
            string? nick = MemberProfileRules.CleanNickName(nickName);
            bool hasAvatar = avatar != null && avatar.Length > 0;
            if (nick == null && !hasAvatar)
            {
                return Ok(Result(1, "请选择头像或填写昵称"));
            }

            // 头像先验证再存，任何一步不行整次保存都不生效（不会出现昵称写进去了、头像没写的半截成功）
            string? avatarPath = null;
            if (hasAvatar)
            {
                if (avatar!.Length > MemberProfileRules.AvatarMaxBytes)
                {
                    return Ok(Result(1, "头像图片太大，请换一张小一点的"));
                }
                byte[] head = new byte[12];
                int read;
                await using (Stream probe = avatar.OpenReadStream())
                {
                    read = await probe.ReadAsync(head, 0, head.Length);
                }
                string? ext = MemberProfileRules.DetectImageExtension(head.AsSpan(0, read));
                if (ext == null)
                {
                    return Ok(Result(1, "头像图片格式不支持，请换一张 JPG、PNG 或 WEBP 图片"));
                }
                try
                {
                    await using Stream content = avatar.OpenReadStream();
                    avatarPath = await _storage.SaveAsync(content, "avatar" + ext, true, "avatar_");
                }
                catch (Exception err)
                {
                    Console.WriteLine($"MemberProfile avatar save failed, member {memberId}: {err.Message}");
                    return Ok(Result(1, "头像保存失败，请稍后重试"));
                }
            }

            // 只改传了的列；更新时间每次保存都刷新。直接 UPDATE：不走 SaveChanges，也不碰 member 的其它列。
            DateTime now = DateTime.Now;
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE member SET nick_name = COALESCE({nick}, nick_name), avatar = COALESCE({avatarPath}, avatar), nick_avatar_update_date = {now} WHERE id = {memberId.Value}");

            MemberProfile? saved = await _db.memberProfile.AsNoTracking().FirstOrDefaultAsync(p => p.member_id == memberId.Value);
            return Ok(Result(0, "", ToView(saved)));
        }

        /// <summary>登录会话是否有效，以及它绑定的会员（没验证手机号时为 null）。</summary>
        private async Task<(bool sessionOk, int? memberId)> FindSessionAsync(string? sessionKey, string? sessionType)
        {
            string key = Util.UrlDecode(sessionKey ?? "").Trim();
            string type = Util.UrlDecode(string.IsNullOrWhiteSpace(sessionType) ? "wechat_mini_openid" : sessionType).Trim();
            if (key.Length == 0)
            {
                return (false, null);
            }
            DateTime now = DateTime.Now;
            var rows = await _db.miniSession.AsNoTracking()
                .Where(s => s.session_key.Trim() == key && s.session_type == type && s.valid == 1 && s.expire_date >= now)
                .OrderByDescending(s => s.expire_date)
                .Select(s => new { s.member_id })
                .Take(1).ToListAsync();
            if (rows.Count == 0)
            {
                return (false, null);
            }
            return (true, rows[0].member_id);
        }

        private MemberProfileView ToView(MemberProfile? p)
        {
            return new MemberProfileView
            {
                needProfile = MemberProfileRules.NeedPrompt(p?.nick_avatar_update_date, DateTime.Now, p?.nick_name, p?.avatar),
                nickName = string.IsNullOrWhiteSpace(p?.nick_name) ? null : p!.nick_name,
                avatarUrl = string.IsNullOrWhiteSpace(p?.avatar) ? null : _storage.PublicUrl(p!.avatar!),
                updateDate = p?.nick_avatar_update_date
            };
        }

        private static ApiResult<MemberProfileView> Result(int code, string message, MemberProfileView? data = null)
        {
            return new ApiResult<MemberProfileView> { code = code, message = message, data = data };
        }
    }

    /// <summary>GetMyProfile / SaveMyProfile 的返回：要不要弹窗，以及已有的昵称、头像（完整网址）、更新时间。</summary>
    public class MemberProfileView
    {
        public bool needProfile { get; set; }
        public string? nickName { get; set; }
        public string? avatarUrl { get; set; }
        public DateTime? updateDate { get; set; }
    }
}
