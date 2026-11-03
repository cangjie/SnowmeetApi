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
    public sealed record SetShareInput(int orderId, int introducerId, double amount, string? memo);
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
                shares = await Service.ListAsync(orderId),
            };
        });

    [HttpPost]
    public Task<ApiResult<object>> SetShare(string sessionKey, [FromBody] SetShareInput input)
        => AsStaff(sessionKey, StaffAccountRules.IsManager, ManagerOnly, async op =>
        {
            await Service.SetIntroducerShareAsync(op, input.orderId, input.introducerId, input.amount, input.memo);
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
