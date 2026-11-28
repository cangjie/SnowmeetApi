using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services;

namespace SnowmeetApi.Controllers;

[Route("api/[controller]/[action]")]
[ApiController]
public class RestaurantOrderController(ApplicationDBContext db) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ApiResult<Models.Order?>>> Create([FromBody] RestaurantOrderRequest request, string sessionKey, string sessionType = "wechat_mini_openid")
    {
        var staff = await Util.GetStaffBySessionKey(db, sessionKey, sessionType);
        if (staff == null || staff.title_level < 100) return Ok(new ApiResult<Models.Order?> { code = 1, message = "没有权限" });
        try
        {
            var order = await new RestaurantOrderService(db).CreateAsync(request, staff.id);
            return Ok(new ApiResult<Models.Order?> { code = 0, message = "", data = order });
        }
        catch (InvalidOperationException ex)
        {
            return Ok(new ApiResult<Models.Order?> { code = 1, message = ex.Message });
        }
    }
}
