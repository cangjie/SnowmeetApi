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
using SnowmeetApi.Services;

namespace SnowmeetApi.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public class CareCardSaleController : ControllerBase
{
    private readonly ApplicationDBContext db;
    private readonly IConfiguration config;
    private readonly CareCardSaleService service;
    public CareCardSaleController(ApplicationDBContext db, IConfiguration config, IHttpContextAccessor http)
    { this.db = db; this.config = config; service = new(db, config, http); }

    private bool Enabled => config.GetValue<bool>("CareCardSale:Enabled");
    private async Task<Staff> AuthorizeAsync(string sessionKey, string sessionType, int? orderId = null)
    {
        var staff = await Util.GetStaffBySessionKey(db, Util.UrlDecode(sessionKey), sessionType);
        if (staff == null || staff.title_level < 100) throw new InvalidOperationException("没有权限");
        if (orderId != null)
        {
            var order = await db.order.AsNoTracking().FirstOrDefaultAsync(o => o.id == orderId && o.type == "养护");
            if (order == null || (order.staff_id != staff.id && staff.title_level < 200)) throw new InvalidOperationException("无权操作该养护订单");
        }
        return staff;
    }

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new ApiResult<object> { code = 0, message = "", data = await action() }); }
        catch (InvalidOperationException e) { return Ok(new ApiResult<object> { code = 1, message = e.Message, data = null }); }
    }

    [HttpGet]
    public Task<IActionResult> GetProducts(string sessionKey, string sessionType = "wechat_mini_openid") => Run(async () =>
    {
        await AuthorizeAsync(sessionKey, sessionType);
        if (!Enabled) return new { enabled = false, products = Array.Empty<object>() };
        var products = await (from p in db.product.AsNoTracking()
            join c in db.category.AsNoTracking() on p.category_code equals c.code
            where p.valid == 1 && p.on_shelves == 1 && c.valid == 1 && c.biz_type == "养护" && (c.name == "次卡" || c.name == "季卡")
            orderby p.sort, p.id
            select new { p.id, p.name, price = p.sale_price, total = c.name == "季卡" ? null : p.punch_total,
                projectCount = p.care_project_count, p.intro, p.usage_rules }).ToListAsync();
        return new { enabled = true, products = products.Select(p => new { p.id, p.name, p.price, p.total, p.projectCount,
            p.intro, p.usage_rules, requiresBinding = CareCardRules.RequiresEquipmentBinding(p.total, p.name) }) };
    });

    [HttpGet]
    public Task<IActionResult> GetDraft(int orderId, string sessionKey, string sessionType = "wechat_mini_openid") => Run(async () =>
    { await AuthorizeAsync(sessionKey, sessionType, orderId); return await service.GetDraftAsync(orderId); });

    [HttpPost]
    public Task<IActionResult> Preview([FromBody] CareCardSaleRequest request, string sessionKey, string sessionType = "wechat_mini_openid") => Run(async () =>
    {
        if (!Enabled) throw new InvalidOperationException("养护开单购卡尚未启用");
        if (request == null) throw new InvalidOperationException("缺少请求数据");
        await AuthorizeAsync(sessionKey, sessionType, request.orderId); return await service.PreviewAsync(request);
    });

    [HttpPost]
    public Task<IActionResult> SaveDraft([FromBody] CareCardSaleRequest request, string sessionKey, string sessionType = "wechat_mini_openid") => Run(async () =>
    {
        if (!Enabled) throw new InvalidOperationException("养护开单购卡尚未启用");
        if (request == null) throw new InvalidOperationException("缺少请求数据");
        await AuthorizeAsync(sessionKey, sessionType, request.orderId); return await service.SaveDraftAsync(request);
    });

    [HttpPost]
    public Task<IActionResult> RetryCompletion(int orderId, string sessionKey, string sessionType = "wechat_mini_openid") => Run(async () =>
    {
        await AuthorizeAsync(sessionKey, sessionType, orderId);
        var retail = await CareCardSaleService.GetLinkedRetailAsync(db, orderId);
        if (retail == null) throw new InvalidOperationException("未找到关联购卡订单");
        await service.CompletePaidAsync(retail.id, null);
        return new { completed = true, careOrderId = orderId, retailOrderId = retail.id };
    });

    [HttpPost]
    public Task<IActionResult> Submit([FromBody] CareCardSaleRequest request, string sessionKey, string sessionType = "wechat_mini_openid") => Run(async () =>
    {
        if (!Enabled) throw new InvalidOperationException("养护开单购卡尚未启用");
        if (request == null) throw new InvalidOperationException("缺少请求数据");
        var staff = await AuthorizeAsync(sessionKey, sessionType, request.orderId);
        int orderId = await service.SubmitAsync(request.orderId, staff.id);
        return new { orderId, careOrderId = request.orderId };
    });
}
