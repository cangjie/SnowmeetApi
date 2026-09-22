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
public sealed class FnbReportController(ApplicationDBContext db) : ControllerBase
{
    private readonly FnbAccess _access = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };

    [HttpGet]
    public async Task<ApiResult<object>> GetOverview(string sessionKey, int shopId, int? itemId = null, int page = 1, int pageSize = 30)
    {
        var staff = await _access.ResolveAsync(sessionKey);
        if (staff == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(staff, shopId, false)) return Result(3, "无门店权限");
        if (page < 1 || pageSize is < 1 or > 100) return Result(1, "分页参数无效");
        var q = db.fnbMaterialStockView.AsNoTracking().Where(x => x.shop_id == shopId);
        if (itemId.HasValue) q = q.Where(x => x.item_id == itemId.Value);
        int total = await q.CountAsync();
        var rows = await q.OrderBy(x => x.item_id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result(0, "", new { total, rows = rows.Select(x => new
        {
            x.item_id, x.item_name, x.category_id, x.base_unit_code, x.total_qty,
            x.sealed_qty, x.available_qty, x.expired_qty, x.inconsistent_batch_count,
            total_amount = staff.title_level >= 200 ? (decimal?)x.total_amount : null,
            average_unit_cost = staff.title_level >= 200 ? x.average_unit_cost : null
        }) });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetLossLedger(string sessionKey, int shopId, DateOnly? from = null,
        DateOnly? to = null, int? itemId = null, int page = 1, int pageSize = 30)
    {
        var staff = await _access.ResolveAsync(sessionKey);
        if (staff == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(staff, shopId, true)) return Result(3, "需要门店管理权限");
        if (page < 1 || pageSize is < 1 or > 100 || from > to) return Result(1, "查询参数无效");
        var q = db.fnbMaterialLossView.AsNoTracking().Where(x => x.shop_id == shopId);
        if (from.HasValue) { var date = from.Value.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.business_date >= date); }
        if (to.HasValue) { var date = to.Value.ToDateTime(TimeOnly.MinValue); q = q.Where(x => x.business_date <= date); }
        if (itemId.HasValue) q = q.Where(x => x.item_id == itemId.Value);
        int total = await q.CountAsync();
        var rows = await q.OrderByDescending(x => x.movement_id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result(0, "", new { total, rows = rows.Select(x => new { movementId = x.movement_id.ToString(),
            documentId = x.document_id.ToString(), x.document_no, x.business_date, x.reason_code,
            x.item_id, x.item_name, x.batch_id, x.batch_no, x.base_unit_code, x.delta_qty,
            x.delta_amount, x.posted_by_staff_id, x.posted_at, x.remark }) });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetExpirySummary(string sessionKey, int shopId, int page = 1, int pageSize = 30)
    {
        var staff = await _access.ResolveAsync(sessionKey);
        if (staff == null) return Result(2, "会话失效");
        if (!FnbAccess.CanAccess(staff, shopId, false)) return Result(3, "无门店权限");
        if (page < 1 || pageSize is < 1 or > 100) return Result(1, "分页参数无效");
        DateTime today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")).Date;
        var q = from s in db.fnbMaterialBatchStock.AsNoTracking()
                join b in db.fnbMaterialBatch.AsNoTracking() on s.batch_id equals b.id
                join i in db.fnbMaterialItem.AsNoTracking() on s.item_id equals i.id
                where s.shop_id == shopId && s.quantity > 0 && !s.is_destroyed && b.valid && b.dispose_status == null
                select new { s.batch_id, s.item_id, itemName = i.name, i.base_unit_code, s.stock_form,
                    s.quantity, s.stock_amount, b.batch_no, b.expire_date, b.warn_days };
        int total = await q.CountAsync();
        var rows = await q.OrderBy(x => x.expire_date).ThenBy(x => x.batch_id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result(0, "", new { total, rows = rows.Select(x => new { x.batch_id, x.item_id, x.itemName,
            x.base_unit_code, x.batch_no, x.expire_date, x.quantity, x.stock_form,
            status = x.expire_date < today ? "已过期" : x.expire_date == today ? "今日" :
                x.expire_date <= today.AddDays(x.warn_days) ? "临期" : "正常",
            stock_amount = staff.title_level >= 200 ? (decimal?)x.stock_amount : null }) });
    }
}
