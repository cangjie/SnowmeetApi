using System;
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
public sealed class FnbInventoryController(ApplicationDBContext db) : ControllerBase
{
    private readonly FnbAccess _access = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    private async Task<int> Permission(string? key, int shopId, bool manager = false)
    {
        var staff = await _access.ResolveAsync(key);
        return staff == null ? 2 : FnbAccess.CanAccess(staff, shopId, manager) ? 0 : 3;
    }

    [HttpGet]
    public async Task<ApiResult<object>> PreviewExpiry(string sessionKey, int shopId, int itemId, string storageType, DateOnly productionDate)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var item = await db.fnbMaterialItem.AsNoTracking().FirstOrDefaultAsync(x => x.id == itemId && x.valid);
        if (item == null) return Result(1, "食材不存在");
        var rule = await db.fnbShelfLifeRule.AsNoTracking().FirstOrDefaultAsync(x => x.item_id == item.id
            && x.storage_type == storageType && x.production_month == productionDate.Month && x.valid);
        if (rule == null) return Result(0, "", new { rule = (object?)null, expireDate = (string?)null });
        try
        {
            var date = FnbInventoryRules.CalculateExpiry(productionDate, rule.shelf_life_value, rule.shelf_life_unit);
            return Result(0, "", new { rule, expireDate = date.ToString("yyyy-MM-dd") });
        }
        catch (ArgumentOutOfRangeException) { return Result(1, "效期超出日期范围"); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> PostReceipt([FromQuery] string sessionKey, [FromBody] ReceiptInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            var result = await new FnbReceiptService(db).PostAsync(input, actor);
            return Result(0, "", new { documentId = result.DocumentId.ToString(), result.BatchId, result.Quantity,
                amount = actor.Staff.title_level >= 200 ? (decimal?)result.Amount : null, result.Replayed });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (UnauthorizedAccessException) { return Result(3, "无门店权限"); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }

    public sealed record DeleteReceiptInput(int ShopId, int BatchId);

    // 入库 10 分钟内、还没有开封使用等后续操作时，本人或店长可删除这次入库（误触、录错）
    [HttpPost]
    public async Task<ApiResult<object>> DeleteReceipt([FromQuery] string sessionKey, [FromBody] DeleteReceiptInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            await new FnbReceiptService(db).DeleteAsync(input.ShopId, input.BatchId, actor);
            return Result(0, "", new { input.BatchId });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> PostOpen([FromQuery] string sessionKey, [FromBody] OpenInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            var result = await new FnbStockPostingService(db).PostOpenAsync(input, actor);
            return Result(0, "", new { documentId = result.DocumentId.ToString(), result.BatchId, result.Quantity,
                amount = actor.Staff.title_level >= 200 ? (decimal?)result.Amount : null, result.Replayed });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> PostWaste([FromQuery] string sessionKey, [FromBody] WasteInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, true)) return Result(3, "需要门店管理权限");
        try
        {
            var result = await new FnbStockPostingService(db).PostWasteAsync(input, actor);
            return Result(0, "", new { documentId = result.DocumentId.ToString(), result.BatchId, result.Quantity, result.Amount, result.Replayed });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }

    [HttpPost]
    public async Task<ApiResult<object>> PostPreparation([FromQuery] string sessionKey, [FromBody] PreparationInput input)
    {
        var actor = await _access.ResolveActorAsync(sessionKey);
        if (actor == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(actor.Staff, input.ShopId, false)) return Result(3, "无门店权限");
        try
        {
            var result = await new FnbPreparationService(db).PostAsync(input, actor);
            return Result(0, "", new { documentId = result.DocumentId.ToString(), result.BatchId, result.Quantity,
                amount = actor.Staff.title_level >= 200 ? (decimal?)result.Amount : null, result.Replayed });
        }
        catch (ArgumentException ex) { return Result(1, ex.Message); }
        catch (InvalidOperationException ex) { return Result(4, ex.Message); }
        catch (DbUpdateConcurrencyException) { return Result(4, "库存已变化，请刷新后重试"); }
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListBatches(string sessionKey, int shopId, int? itemId = null, int page = 1, int pageSize = 30)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        if (page < 1 || pageSize is < 1 or > 100) return Result(1, "分页参数无效");
        var q = from s in db.fnbMaterialBatchStock.AsNoTracking()
                join b in db.fnbMaterialBatch.AsNoTracking() on s.batch_id equals b.id
                where s.shop_id == shopId && (!itemId.HasValue || s.item_id == itemId)
                select new { s, b };
        int total = await q.CountAsync();
        var rows = await q.OrderBy(x => x.b.expire_date).ThenBy(x => x.s.batch_id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        bool canSeeCost = (await _access.ResolveAsync(sessionKey))!.title_level >= 200;
        return Result(0, "", new { total, rows = rows.Select(x => new { batch = x.b,
            stock = new { x.s.batch_id, x.s.shop_id, x.s.item_id, x.s.stock_form, x.s.storage_type,
                x.s.storage_location, x.s.quantity, stock_amount = canSeeCost ? (decimal?)x.s.stock_amount : null,
                x.s.pack_size, x.s.pack_unit_name, x.s.sealed_pack_count, x.s.parent_batch_id,
                x.s.opened_date, x.s.original_expire_date, x.s.opened_expire_date,
                x.s.open_storage_type, x.s.open_shelf_life_days, x.s.expiry_source, x.s.expiry_note, x.s.is_destroyed } }) });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetBatch(string sessionKey, int shopId, int batchId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var stock = await db.fnbMaterialBatchStock.AsNoTracking().FirstOrDefaultAsync(x => x.batch_id == batchId && x.shop_id == shopId);
        if (stock == null) return Result(1, "批次不存在");
        var batch = await db.fnbMaterialBatch.AsNoTracking().FirstAsync(x => x.id == batchId);
        var staff = (await _access.ResolveAsync(sessionKey))!;
        bool canSeeCost = staff.title_level >= 200;
        // 入库 10 分钟内且还没有后续操作时给出剩余秒数，小程序据此显示「删除入库」
        int? deleteSecondsLeft = await new FnbReceiptService(db).DeleteSecondsLeftAsync(shopId, batchId, staff);
        return Result(0, "", new { batch, deleteSecondsLeft, stock = new { stock.batch_id, stock.shop_id, stock.item_id,
            stock.stock_form, stock.storage_type, stock.storage_location, stock.quantity,
            stock_amount = canSeeCost ? (decimal?)stock.stock_amount : null, stock.pack_size, stock.pack_unit_name,
            stock.sealed_pack_count, stock.parent_batch_id, stock.opened_date, stock.original_expire_date,
            stock.opened_expire_date, stock.open_storage_type, stock.open_shelf_life_days,
            stock.shelf_life_rule_id, stock.calculated_expire_date, stock.expiry_source, stock.expiry_note, stock.is_destroyed } });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetLabelData(string sessionKey, int shopId, int batchId)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var row = await (from s in db.fnbMaterialBatchStock.AsNoTracking()
                         join b in db.fnbMaterialBatch.AsNoTracking() on s.batch_id equals b.id
                         where s.shop_id == shopId && s.batch_id == batchId
                         select new { b.id, b.name, b.batch_no, b.produce_date, b.expire_date, b.warn_days, s.stock_form, s.storage_type })
                         .FirstOrDefaultAsync();
        return row == null ? Result(1, "批次不存在") : Result(0, "", new { row, scanUrl = FnbMaterialController.H5_BATCH_URL + "?id=" + batchId });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetStock(string sessionKey, int shopId, int? itemId = null)
    {
        int p = await Permission(sessionKey, shopId);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var rows = await (from s in db.fnbMaterialBatchStock.AsNoTracking()
                          join b in db.fnbMaterialBatch.AsNoTracking() on s.batch_id equals b.id
                          join i in db.fnbMaterialItem.AsNoTracking() on s.item_id equals i.id
                          where s.shop_id == shopId && (!itemId.HasValue || s.item_id == itemId)
                          select new { s.item_id, itemName = i.name, i.base_unit_code, s.stock_form, s.quantity, s.stock_amount,
                              s.is_destroyed, b.valid, b.dispose_status, b.expire_date }).ToListAsync();
        bool canSeeCost = (await _access.ResolveAsync(sessionKey))!.title_level >= 200;
        var summary = rows.GroupBy(x => new { x.item_id, x.itemName, x.base_unit_code }).Select(g => new
        {
            g.Key.item_id, g.Key.itemName, g.Key.base_unit_code,
            totalQty = g.Sum(x => x.quantity), totalAmount = canSeeCost ? (decimal?)g.Sum(x => x.stock_amount) : null,
            availableQty = g.Where(x => x.valid && !x.is_destroyed && x.dispose_status == null && x.expire_date >= today && x.stock_form != "sealed").Sum(x => x.quantity),
            sealedQty = g.Where(x => x.stock_form == "sealed").Sum(x => x.quantity),
            expiredQty = g.Where(x => x.expire_date < today).Sum(x => x.quantity)
        });
        return Result(0, "", summary);
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetDocument(string sessionKey, int shopId, long documentId)
    {
        int p = await Permission(sessionKey, shopId, true);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        var document = await db.fnbStockDocument.AsNoTracking().FirstOrDefaultAsync(x => x.id == documentId && x.shop_id == shopId);
        if (document == null) return Result(1, "单据不存在");
        var lines = await db.fnbStockDocumentLine.AsNoTracking().Where(x => x.document_id == documentId && x.shop_id == shopId).OrderBy(x => x.line_no).ToListAsync();
        var ids = lines.Select(x => x.id).ToArray();
        var movements = await db.fnbStockMovement.AsNoTracking().Where(x => ids.Contains(x.document_line_id)).OrderBy(x => x.id).ToListAsync();
        return Result(0, "", new { document, lines, movements });
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListMovements(string sessionKey, int shopId, int? itemId = null, int? batchId = null, int page = 1, int pageSize = 30)
    {
        int p = await Permission(sessionKey, shopId, true);
        if (p != 0) return Result(p, "会话失效或无门店权限");
        if (page < 1 || pageSize is < 1 or > 100) return Result(1, "分页参数无效");
        var q = db.fnbStockMovement.AsNoTracking().Where(x => x.shop_id == shopId);
        if (itemId.HasValue) q = q.Where(x => x.item_id == itemId.Value);
        if (batchId.HasValue) q = q.Where(x => x.batch_id == batchId.Value);
        int total = await q.CountAsync();
        var rows = await q.OrderByDescending(x => x.id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result(0, "", new { total, rows });
    }
}
