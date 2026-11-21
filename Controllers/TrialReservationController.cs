using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Controllers.CoachPlatform;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.CoachPlatform;
using SnowmeetApi.Services.Rent;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class TrialReservationController(ApplicationDBContext db, IConfiguration config) : CoachApiBase(db)
{
    private TrialReservationService Service => new(db, config);
    public sealed record ReserveInput(int productId, string date);
    public sealed record IdInput(int id);
    public sealed record CheckinInput(string token);

    private async Task<Member?> MemberFor(string? key)
    {
        var session = await SessionAsync(key);
        return session == null ? null : await MemberOfAsync(session);
    }

    private async Task<Staff?> StaffFor(string? key)
    {
        var staff = await Util.GetStaffBySessionKey(db, Util.UrlDecode(key ?? ""), "wechat_mini_openid");
        return staff != null && staff.valid == 1 && staff.title_level >= 100 ? staff : null;
    }

    private static DateTime ParseDate(string? value)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new CoachException("预约日期无效");
        return date.Date;
    }

    [HttpGet]
    public async Task<ApiResult<object>> Options(string date, string sessionKey)
    {
        var member = await MemberFor(sessionKey);
        if (member == null) return Result(2, "请先登录并授权手机号");
        return await Run(async () =>
        {
            var day = ParseDate(date);
            var now = DateTime.Now;
            if (day < now.Date || day > CoachCardRules.SeasonEnd(now, config).Date)
                throw new CoachException("只能预约本雪季内当天或之后的日期");
            bool eligible = await Service.EligibleAsync(member.id, now);
            var boards = await (from b in db.trialBoard.AsNoTracking()
                                join p in db.rentProduct.AsNoTracking() on b.rent_product_id equals p.id
                                where b.valid == 1 && p.valid == 1
                                orderby p.barcode
                                select new { p.id, p.name, p.barcode }).ToListAsync();
            var result = new System.Collections.Generic.List<object>();
            foreach (var board in boards)
                result.Add(new { board.id, board.name, board.barcode, available = !await Service.BoardUnavailableAsync(board.id, day) });
            bool already = await db.trialReservation.AsNoTracking().AnyAsync(r => r.member_id == member.id && r.reservation_date == day && r.counts_for_day == 1);
            return new { eligible, already, seasonEnd = CoachCardRules.SeasonEnd(now, config).ToString("yyyy-MM-dd"), boards = result };
        });
    }

    [HttpPost]
    public async Task<ApiResult<object>> Reserve([FromBody] ReserveInput input, string sessionKey)
    {
        var member = await MemberFor(sessionKey);
        if (member == null) return Result(2, "请先登录并授权手机号");
        return await Run(async () => new { id = (await Service.ReserveAsync(member.id, input.productId, ParseDate(input.date), DateTime.Now)).id });
    }

    [HttpGet]
    public async Task<ApiResult<object>> Mine(string sessionKey)
    {
        var member = await MemberFor(sessionKey);
        if (member == null) return Result(2, "请先登录并授权手机号");
        var rows = await (from r in db.trialReservation.AsNoTracking()
                          join p in db.rentProduct.AsNoTracking() on r.rent_product_id equals p.id
                          where r.member_id == member.id
                          orderby r.reservation_date descending, r.id descending
                          select new { r.id, r.reservation_date, r.status, p.name, p.barcode, r.checkin_date, r.return_date }).Take(100).ToListAsync();
        return Result(0, "", new { reservations = rows });
    }

    [HttpPost]
    public async Task<ApiResult<object>> CancelMine([FromBody] IdInput input, string sessionKey)
    {
        var member = await MemberFor(sessionKey);
        if (member == null) return Result(2, "请先登录并授权手机号");
        return await Run(async () => { await Service.CancelAsync(input.id, member.id, null, DateTime.Now); return new { cancelled = true }; });
    }

    [HttpGet]
    public async Task<ApiResult<object>> RentalAvailability(int productId, string date, string sessionKey)
    {
        if (await StaffFor(sessionKey) == null) return Result(3, "没有权限");
        return await Run(async () =>
        {
            var start = ParseDate(date);
            bool blocked = await new RentProductAvailability(db).IsBlockedForOpenRentalAsync(productId, null, start, DateTime.Now);
            return new { available = !blocked };
        });
    }

    [HttpGet]
    public async Task<ApiResult<object>> StaffList(string date, string sessionKey)
    {
        if (await StaffFor(sessionKey) == null) return Result(3, "没有权限");
        return await Run(async () =>
        {
            var day = ParseDate(date);
            var rows = await (from r in db.trialReservation.AsNoTracking()
                              join p in db.rentProduct.AsNoTracking() on r.rent_product_id equals p.id
                              join m in db.member.AsNoTracking() on r.member_id equals m.id
                              where r.reservation_date == day || (r.status == TrialReservation.InUse && r.reservation_date < day)
                              orderby r.reservation_date descending, r.id descending
                              select new { r.id, r.reservation_date, r.status, r.checkin_date, r.return_date, memberName = m.real_name, p.name, p.barcode }).ToListAsync();
            return new { reservations = rows };
        });
    }

    [HttpPost]
    public async Task<ApiResult<object>> StaffCancel([FromBody] IdInput input, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return Result(3, "没有权限");
        return await Run(async () => { await Service.CancelAsync(input.id, null, staff.id, DateTime.Now); return new { cancelled = true }; });
    }

    [HttpPost]
    public async Task<ApiResult<object>> CreateQr([FromBody] IdInput input, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return Result(3, "没有权限");
        return await Run(async () =>
        {
            var row = await Service.CreateQrAsync(input.id, staff.id, DateTime.Now);
            return new { row.id, expiresAt = row.qr_expires_at };
        });
    }

    [HttpGet]
    public async Task<IActionResult> QrImage(int id, string sessionKey, string envVersion, [FromServices] MiniStaffBookingQrService qr)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return BadRequest(new { message = "没有权限" });
        var row = await db.trialReservation.AsNoTracking().FirstOrDefaultAsync(r => r.id == id && r.qr_staff_id == staff.id
            && r.status == TrialReservation.Pending && r.qr_token != null && r.qr_expires_at > DateTime.Now);
        if (row == null) return BadRequest(new { message = "二维码已失效" });
        Response.Headers.CacheControl = "no-store";
        return File(await qr.GetForTrialCheckinAsync(row.qr_token!, envVersion), "image/png");
    }

    [HttpGet]
    public async Task<ApiResult<object>> QrStatus(int id, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return Result(3, "没有权限");
        var row = await db.trialReservation.AsNoTracking().FirstOrDefaultAsync(r => r.id == id && r.qr_staff_id == staff.id);
        if (row == null) return Result(1, "二维码不存在");
        return Result(0, "", new { status = row.status, expired = row.qr_expires_at <= DateTime.Now });
    }

    [HttpGet]
    public async Task<ApiResult<object>> CheckinInfo(string token, string sessionKey)
    {
        var member = await MemberFor(sessionKey);
        if (member == null) return Result(2, "请先登录并授权手机号");
        if (!Regex.IsMatch(token ?? "", "^[0-9a-f]{24}$")) return Result(1, "二维码无效");
        var row = await (from r in db.trialReservation.AsNoTracking()
                         join p in db.rentProduct.AsNoTracking() on r.rent_product_id equals p.id
                         where r.qr_token == token && r.qr_expires_at > DateTime.Now && r.status == TrialReservation.Pending
                         select new { r.member_id, r.reservation_date, p.name, p.barcode }).FirstOrDefaultAsync();
        if (row == null) return Result(1, "二维码已失效");
        if (row.member_id != member.id) return Result(3, "请预约会员本人扫码确认");
        return Result(0, "", new { row.reservation_date, row.name, row.barcode });
    }

    [HttpPost]
    public async Task<ApiResult<object>> Checkin([FromBody] CheckinInput input, string sessionKey)
    {
        var member = await MemberFor(sessionKey);
        if (member == null) return Result(2, "请先登录并授权手机号");
        if (input == null || !Regex.IsMatch(input.token ?? "", "^[0-9a-f]{24}$")) return Result(1, "二维码无效");
        return await Run(async () => { await Service.CheckInAsync(input.token, member.id, DateTime.Now); return new { checkedIn = true }; });
    }

    [HttpPost]
    public async Task<ApiResult<object>> Return([FromBody] IdInput input, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (staff == null) return Result(3, "没有权限");
        return await Run(async () => { await Service.ReturnAsync(input.id, staff.id, DateTime.Now); return new { returned = true }; });
    }
}
