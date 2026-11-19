using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Rent;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
public sealed class RentalBenefitController(ApplicationDBContext db) : ControllerBase
{
    public sealed class ModeRequest { public string mode { get; set; } = ""; }
    public sealed class CommitRequest { public string fingerprint { get; set; } = ""; }

    [HttpGet("{orderId}")]
    public async Task<ActionResult<ApiResult<object>>> Preview(int orderId, string sessionKey,
        string sessionType = "wechat_mini_openid", bool draftAuto = false)
    {
        var staff = await Util.GetStaffBySessionKey(db, sessionKey, sessionType);
        if (staff == null || staff.title_level < 100) return Ok(new ApiResult<object> { code = 1, message = "没有权限" });
        try
        {
            var quote = await new RentalBenefitQuoteService(db).QuoteAsync(orderId, draftAuto, DateTime.Now);
            return Ok(new ApiResult<object> { data = quote });
        }
        catch (InvalidOperationException e) { return Ok(new ApiResult<object> { code = 1, message = e.Message }); }
    }

    [HttpPost("{orderId}")]
    public async Task<ActionResult<ApiResult<object>>> SetMode(int orderId, [FromBody] ModeRequest req,
        string sessionKey, string sessionType = "wechat_mini_openid")
    {
        var staff = await Util.GetStaffBySessionKey(db, sessionKey, sessionType);
        if (staff == null || staff.title_level < 100) return Ok(new ApiResult<object> { code = 1, message = "没有权限" });
        if (req == null || (req.mode != "auto" && req.mode != "none"))
            return Ok(new ApiResult<object> { code = 1, message = "选择无效" });
        var result = await MiniTicketTransaction.Run(db, async () =>
        {
            await MiniTicketTransaction.Lock(db, "snowmeet:rental-benefit:commit");
            var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == orderId && o.type == "租赁");
            if (order == null) return new ApiResult<object> { code = 1, message = "未找到租赁订单" };
            if (order.closed == 1 || await db.orderPaymentRefund.AnyAsync(r => r.order_id == orderId)
                || await db.rentalBenefitSettlement.AnyAsync(s => s.order_id == orderId))
                return new ApiResult<object> { code = 1, message = "订单已结算或正在退款，无法修改" };
            var preference = await db.rentalBenefitPreference.FirstOrDefaultAsync(p => p.order_id == orderId);
            if (preference == null) { preference = new RentalBenefitPreference { order_id = orderId }; db.rentalBenefitPreference.Add(preference); }
            else db.Entry(preference).State = EntityState.Modified;
            preference.mode = req.mode; preference.staff_id = staff.id; preference.update_date = DateTime.Now;
            await db.SaveChangesAsync();
            return new ApiResult<object> { data = new { mode = preference.mode } };
        });
        return Ok(result);
    }

    [HttpPost("{orderId}")]
    public async Task<ActionResult<ApiResult<object>>> Commit(int orderId, [FromBody] CommitRequest req,
        string sessionKey, string sessionType = "wechat_mini_openid")
    {
        var staff = await Util.GetStaffBySessionKey(db, sessionKey, sessionType);
        if (staff == null || staff.title_level < 100) return Ok(new ApiResult<object> { code = 1, message = "没有权限" });
        try
        {
            var settlement = await new RentalBenefitSettlementService(db).CommitAsync(orderId, req?.fingerprint ?? "", staff.id, DateTime.Now);
            return Ok(new ApiResult<object> { data = settlement });
        }
        catch (InvalidOperationException e) { return Ok(new ApiResult<object> { code = 1, message = e.Message }); }
    }
}
