using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class SkiPassStaffQrController(ApplicationDBContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetQrCode(string sessionKey, [FromServices] MiniStaffBookingQrService qr,
        string sessionType = "wechat_mini_openid", string envVersion = "release")
    {
        Staff staff = await Util.GetStaffBySessionKey(db, Util.UrlDecode(sessionKey ?? ""), sessionType);
        if (staff == null || staff.valid != 1 || staff.title_level < 100)
            return BadRequest(new { code = 1, message = "只有在职员工可以生成订票码" });
        try { return File(await qr.GetAsync(staff.id, envVersion), "image/png"); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        { return BadRequest(new { code = 1, message = e.Message }); }
        catch (Exception e) when (e is HttpRequestException || e is JsonException)
        { return BadRequest(new { code = 1, message = "小程序码服务暂时不可用，请重试" }); }
    }
}
