using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SnowmeetApi.Data;
using SnowmeetApi.Helpers;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Share;
using SnowmeetApi.Services.StaffAccounts;

namespace SnowmeetApi.Controllers.IntroducerShare;

// 订单详情页里的「介绍人分账」（小程序组件 order_share_editor）。店长以上（title_level ≥ 200）可看、可设置。
[ApiController]
[Route("api/[controller]/[action]")]
public sealed class OrderIntroducerShareController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http) : ShareApiBase(db)
{
    /// <param name="planDate">预计分账日期 yyyy-MM-dd；旧版小程序不传，按默认规则</param>
    /// <param name="autoShare">是否自动分账；旧版小程序不传，按自动</param>
    public sealed record SetShareInput(int orderId, int introducerId, double amount, string? memo, string? planDate = null, bool? autoShare = null);
    public sealed record RemoveShareInput(int orderShareId);

    private const string ManagerOnly = "只有店长以上可以设置介绍人分账";

    private OrderShareService Service => new(Db, config, http);

    [HttpGet]
    public Task<ApiResult<object>> GetOrderShares(string sessionKey, int orderId)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, ManagerOnly, async _ =>
        {
            Models.Order? order = await new OrderController(Db, config, http).GetOrder(orderId);
            if (order == null) throw new ShareException("订单不存在");
            string? reason = ShareTriggerRules.ReasonCannotSet(order);
            return new
            {
                canEdit = reason == null,
                reason,
                suggested = reason == null ? await Service.SuggestedAmountAsync(orderId) : 0,
                // 小程序据此知道后端支持预计分账日期（旧后端没有这个字段，就不显示日期）
                autoShareSupported = true,   // 小程序据此显示「自动分账」开关
                suggestedPlanDate = reason == null ? ShareDateRules.EarliestPlanDate(order.close_date!.Value, DateTime.Now).ToString("yyyy-MM-dd") : null,
                shares = await Service.ListAsync(orderId),
            };
        });

    [HttpPost]
    public Task<ApiResult<object>> SetShare(string sessionKey, [FromBody] SetShareInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, ManagerOnly, async op =>
        {
            DateTime? planDate = null;
            if (!string.IsNullOrWhiteSpace(input.planDate))
            {
                if (!ShareDateRules.TryParse(input.planDate, out DateTime parsed)) throw new ShareException("预计分账日期格式不对");
                planDate = parsed;
            }
            await Service.SetIntroducerShareAsync(op, input.orderId, input.introducerId, input.amount, input.memo, planDate, input.autoShare ?? true);
            return new { shares = await Service.ListAsync(input.orderId) };
        });

    [HttpPost]
    public Task<ApiResult<object>> RemoveShare(string sessionKey, [FromBody] RemoveShareInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, ManagerOnly, async op =>
        {
            int orderId = (await Db.orderShare.AsNoTracking().FirstOrDefaultAsync(s => s.id == input.orderShareId))?.order_id ?? 0;
            await Service.RemoveIntroducerShareAsync(op, input.orderShareId);
            return new { shares = await Service.ListAsync(orderId) };
        });
}
