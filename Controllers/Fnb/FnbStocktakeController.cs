using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class FnbStocktakeController(ApplicationDBContext db) : ControllerBase
{
    private readonly FnbAccess _access = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    private async Task<int> Permission(string? key, int shopId, bool manager = false)
    {
        var staff = await _access.ResolveAsync(key);
        return staff == null ? 2 : FnbAccess.CanAccess(staff, shopId, manager) ? 0 : 3;
    }

    public sealed record SnapshotInput(int ShopId, Guid RequestId, IReadOnlyList<int> ItemIds);

    [HttpPost]
    public async Task<ApiResult<object>> CreateSnapshot([FromQuery] string sessionKey, [FromBody] SnapshotInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, true)) return Result(3, "需要门店管理权限");
        try
        {
            long id = await new FnbStocktakeService(db).CreateSnapshotAsync(input.ShopId, input.RequestId, input.ItemIds, actor);
            return Result(0, "", new { documentId = id.ToString() });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetSnapshot(string sessionKey, int shopId, long documentId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.id == documentId && x.shop_id == shopId && x.document_type == "stocktake");
        if (document == null) return Result(1, "盘点单不存在");
        var rows = await db.fnbStocktakeLine.AsNoTracking().Where(x => x.document_id == documentId && x.shop_id == shopId).ToListAsync();
        return Result(0, "", new { documentId = document.id.ToString(), document.status,
            rows = rows.ConvertAll(x => new { x.item_id, x.system_qty, x.counted_qty, x.difference_qty,
                rowVersion = Convert.ToBase64String(x.row_version), x.counted_at }) });
    }

    [HttpPost]
    public async Task<ApiResult<object>> SaveCount([FromQuery] string sessionKey, [FromBody] CountInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            var row = await new FnbStocktakeService(db).SaveCountAsync(input, actor);
            return Result(0, "", new { row.item_id, row.counted_qty, row.difference_qty,
                rowVersion = Convert.ToBase64String(row.row_version) });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "盘点行已变化，请刷新"); }
    }

    [HttpGet]
    public async Task<ApiResult<object>> PreviewAdjustment(string sessionKey, int shopId, long documentId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        try { return Result(0, "", await new FnbStocktakeService(db).PreviewAsync(shopId, documentId)); }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> PostStocktake([FromQuery] string sessionKey, [FromBody] PostStocktakeInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, true)) return Result(3, "需要门店管理权限");
        try
        {
            long id = await new FnbStocktakeService(db).PostAsync(input, actor);
            return Result(0, "", new { documentId = id.ToString() });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "盘点期间库存已变化，请重新盘点"); }
    }
}
