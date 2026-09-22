using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class FnbKitchenController(ApplicationDBContext db) : ControllerBase
{
    private readonly FnbAccess _access = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    private async Task<int> Permission(string? key, int shopId, bool manager = false)
    {
        var staff = await _access.ResolveAsync(key);
        return staff == null ? 2 : FnbAccess.CanAccess(staff, shopId, manager) ? 0 : 3;
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListOrders(string sessionKey, int shopId, DateOnly? businessDate = null, int page = 1, int pageSize = 30)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        if (page < 1 || pageSize is < 1 or > 100) return Result(1, "分页参数无效");
        var q = db.fnbOrder.AsNoTracking().Where(x => x.shop_id == shopId);
        if (businessDate.HasValue)
        {
            var date = businessDate.Value.ToDateTime(TimeOnly.MinValue);
            q = q.Where(x => x.business_date == date);
        }
        int total = await q.CountAsync();
        var rows = await q.OrderByDescending(x => x.ordered_at).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result(0, "", new { total, rows });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetOrder(string sessionKey, int shopId, long orderId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var order = await db.fnbOrder.AsNoTracking().FirstOrDefaultAsync(x => x.id == orderId && x.shop_id == shopId);
        if (order == null) return Result(1, "厨房订单不存在");
        var lines = await db.fnbOrderLine.AsNoTracking().Where(x => x.order_id == orderId && x.shop_id == shopId).OrderBy(x => x.id).ToListAsync();
        bool served = await db.fnbStockDocument.AnyAsync(x => x.order_id == orderId && x.status == "posted");
        return Result(0, "", new { order, lines, served });
    }

    public sealed record ReviewInput(int ShopId, long OrderId);

    [HttpPost]
    public async Task<ApiResult<object>> CreateManualOrder([FromQuery] string sessionKey, [FromBody] ManualKitchenOrderInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            var created = await new FnbManualOrderService(db).CreateAsync(input, actor);
            return Result(0, "", new { orderId = created.OrderId.ToString(), created.DisplayNo,
                created.ReviewStatus, created.LineCount, created.Replayed });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "订单已变化，请刷新后重试"); }
        catch (DbUpdateException) { return Result(4, "订单提交冲突，请用原请求号重试"); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> CancelManualOrder([FromQuery] string sessionKey, [FromBody] ReviewInput input)
    {
        int p = await Permission(sessionKey, input.ShopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        try
        {
            await new FnbManualOrderService(db).CancelAsync(input.ShopId, input.OrderId);
            return Result(0, "", new { orderId = input.OrderId.ToString(), status = "cancelled" });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "订单已变化，请刷新后重试"); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> ReviewOrder([FromQuery] string sessionKey, [FromBody] ReviewInput input)
    {
        int p = await Permission(sessionKey, input.ShopId, true);
        if (p != 0) return Result(p, "会话失效或需要门店管理权限");
        var order = await db.fnbOrder.AsTracking().FirstOrDefaultAsync(x => x.id == input.OrderId && x.shop_id == input.ShopId);
        if (order == null) return Result(1, "厨房订单不存在");
        if (order.order_status == "cancelled") return Result(1, "已取消订单不能核对");
        var lines = await db.fnbOrderLine.AsNoTracking().Where(x => x.order_id == order.id).ToListAsync();
        if (lines.Count == 0 || lines.Any(x => x.is_inventory_line && x.dish_spec_id == null)) return Result(1, "仍有未匹配的菜品规格");
        int[] specIds = lines.Where(x => x.is_inventory_line).Select(x => x.dish_spec_id!.Value).Distinct().ToArray();
        int readyCount = await db.fnbRecipe.CountAsync(x => x.shop_id == input.ShopId && x.recipe_type == "dish" &&
            x.status == "published" && x.dish_spec_id != null && specIds.Contains(x.dish_spec_id.Value));
        if (readyCount != specIds.Length) return Result(1, "仍有菜品未配置已发布配方");
        order.review_status = "verified"; order.updated_at = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Result(0, "", new { orderId = order.id.ToString(), order.review_status });
    }

    [HttpGet]
    public async Task<ApiResult<object>> PreviewServe(string sessionKey, int shopId, long orderId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        try { return Result(0, "", await new FnbServeService(db).PreviewAsync(shopId, orderId)); }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
    }

    public sealed record ServeInput(int ShopId, Guid RequestId, long OrderId);

    [HttpPost]
    public async Task<ApiResult<object>> PostServe([FromQuery] string sessionKey, [FromBody] ServeInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            var result = await new FnbServeService(db).PostAsync(input.ShopId, input.OrderId, input.RequestId, actor);
            return Result(0, "", new { documentId = result.DocumentId.ToString(), result.Replayed, result.Needs });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }
}
