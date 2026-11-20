using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Services.Tickets;

namespace SnowmeetApi.Controllers;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class StaffCouponBatchController(ApplicationDBContext db) : ControllerBase
{
    public sealed record CreateInput(int templateId, int total, string mode, string requestId);
    private static ApiResult<object> Error(string message) => new() { code = 1, message = message, data = null };
    private static ApiResult<object> Success(object data) => new() { code = 0, message = "", data = data };
    private async Task<Staff?> StaffFor(string sessionKey) => await Util.GetStaffBySessionKey(db, Util.UrlDecode(sessionKey ?? ""), "wechat_mini_openid");
    private static bool Allowed(Staff? staff) => staff != null && staff.valid == 1 && staff.title_level >= 100;

    [HttpGet]
    public async Task<ActionResult<ApiResult<object>>> Templates(string sessionKey)
    {
        if (!Allowed(await StaffFor(sessionKey))) return Ok(Error("没有权限"));
        var templates = await db.ticketTemplate.AsNoTracking().Where(t => t.valid == 1 && t.marketing_promotion == 1 && t.hide == 0)
            .OrderByDescending(t => t.id).Select(t => new { t.id, t.name, t.memo }).ToListAsync();
        return Ok(Success(new { templates }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> Create(string sessionKey, [FromBody] CreateInput input)
    {
        Staff? staff = await StaffFor(sessionKey);
        if (!Allowed(staff)) return Ok(Error("没有权限"));
        if (input == null || input.total < 1 || input.total > 500) return Ok(Error("批次张数须为 1～500 张"));
        if (input.mode != "single" && input.mode != "multiple") return Ok(Error("请选择发放方式"));
        if (!Guid.TryParse(input.requestId, out var requestId)) return Ok(Error("请求编号无效"));
        string key = requestId.ToString("N");
        var result = await MiniTicketTransaction.Run(db, async () => {
            var tpl = await db.ticketTemplate.AsNoTracking().FirstOrDefaultAsync(t => t.id == input.templateId && t.valid == 1 && t.marketing_promotion == 1 && t.hide == 0);
            if (tpl == null) return Error("该模板未开放市场推广或已停用");
            var prior = await db.staffCouponBatch.AsNoTracking().FirstOrDefaultAsync(x => x.staff_id == staff!.id && x.request_id == key);
            if (prior != null) {
                var original = await db.ticketShareBatch.AsNoTracking().FirstAsync(b => b.id == prior.batch_id);
                if (original.valid == TicketShareBatch.Deleted) return Error("该批次已删除，请重新创建");
                if (original.template_id != input.templateId || original.max_claims != input.total || prior.mode != input.mode || prior.per_person_limit != (input.mode == "single" ? input.total : 1))
                    return Error("请求编号已用于其他批次，请重新操作");
                return Success(new { batchId = original.id });
            }
            int open = await (from b in db.ticketShareBatch.AsNoTracking() join s in db.staffCouponBatch.AsNoTracking() on b.id equals s.batch_id
                where s.staff_id == staff!.id && b.valid == 1 && b.max_claims != null && b.claim_count < b.max_claims
                select b.max_claims!.Value - b.claim_count).SumAsync();
            if (open + input.total > 1000) return Error("尚未领取的批次余量合计不能超过 1000 张");
            var batch = new TicketShareBatch { template_id = tpl.id, staff_id = staff!.id, share_type = MiniTicketRules.StaffBatch,
                max_claims = input.total, claim_count = 0, valid = 1, create_date = DateTime.Now };
            db.ticketShareBatch.Add(batch); await db.SaveChangesAsync();
            db.staffCouponBatch.Add(new StaffCouponBatch { batch_id = batch.id, staff_id = staff.id,
                per_person_limit = input.mode == "single" ? input.total : 1, mode = input.mode, request_id = key });
            await db.SaveChangesAsync();
            return Success(new { batchId = batch.id });
        });
        return Ok(result);
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<object>>> Batches(int templateId, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (!Allowed(staff)) return Ok(Error("没有权限"));
        if (!await db.ticketTemplate.AsNoTracking().AnyAsync(t => t.id == templateId && t.valid == 1 && t.marketing_promotion == 1 && t.hide == 0)) return Ok(Error("该模板未开放市场推广或已停用"));
        var rows = await (from b in db.ticketShareBatch.AsNoTracking()
            join st in db.staff.AsNoTracking() on b.staff_id equals st.id into staffs
            from st in staffs.DefaultIfEmpty()
            where b.template_id == templateId && b.valid != TicketShareBatch.Deleted orderby b.id descending
            select new { batch = b, staffName = st == null ? "" : st.name }).ToListAsync();
        var ids = rows.Select(x => x.batch.id).ToList();
        var settings = await db.staffCouponBatch.AsNoTracking().Where(s => ids.Contains(s.batch_id)).ToListAsync();
        var claims = await db.ticketShareClaim.AsNoTracking().Where(c => ids.Contains(c.batch_id)).GroupBy(c => c.batch_id)
            .Select(g => new { id = g.Key, count = g.Select(x => x.member_id).Distinct().Count() }).ToListAsync();
        var issuedBatchIds = await db.staffCouponBatchTicket.AsNoTracking().Where(t => ids.Contains(t.batch_id))
            .Select(t => t.batch_id).Distinct().ToListAsync();
        var result = rows.Select(x => { var setting = settings.FirstOrDefault(s => s.batch_id == x.batch.id); return new {
            batchId = x.batch.id, total = x.batch.max_claims, claimed = x.batch.claim_count,
            people = claims.FirstOrDefault(c => c.id == x.batch.id)?.count ?? 0,
            remaining = x.batch.max_claims == null ? (int?)null : Math.Max(0, x.batch.max_claims.Value - x.batch.claim_count),
            mode = setting == null ? "legacy" : setting.mode, perPerson = setting?.per_person_limit,
            valid = x.batch.valid == 1, isMine = x.batch.staff_id == staff!.id, staffName = (x.staffName ?? "").Trim(),
            canDelete = x.batch.valid == 0 && x.batch.staff_id == staff!.id && x.batch.claim_count == 0
                && !claims.Any(c => c.id == x.batch.id && c.count > 0) && !issuedBatchIds.Contains(x.batch.id),
            created = x.batch.create_date.ToString("yyyy-MM-dd HH:mm"), sharePath = setting == null ? "" : "/" + MiniTicketRules.ClaimPage + "?batch=" + x.batch.id }; }).ToList();
        return Ok(Success(new { batches = result }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> Revoke(int batchId, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (!Allowed(staff)) return Ok(Error("没有权限"));
        return Ok(await MiniTicketTransaction.Run(db, async () => {
            var setting = await db.staffCouponBatch.AsNoTracking().FirstOrDefaultAsync(s => s.batch_id == batchId && s.staff_id == staff!.id);
            if (setting == null) return Error("只能撤回自己创建的批次");
            var batch = await db.ticketShareBatch.AsTracking().FirstOrDefaultAsync(b => b.id == batchId && b.share_type == MiniTicketRules.StaffBatch);
            if (batch == null) return Error("批次不存在");
            if (batch.valid == TicketShareBatch.Deleted) return Error("批次已删除");
            batch.valid = 0; batch.update_date = DateTime.Now; await db.SaveChangesAsync();
            return Success(new { batchId });
        }));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<object>>> Delete(int batchId, string sessionKey)
    {
        var staff = await StaffFor(sessionKey);
        if (!Allowed(staff)) return Ok(Error("没有权限"));
        return Ok(await MiniTicketTransaction.Run(db, async () => {
            var batch = await db.ticketShareBatch.AsTracking().FirstOrDefaultAsync(b => b.id == batchId);
            if (batch == null || batch.valid == TicketShareBatch.Deleted) return Error("批次不存在或已删除");
            if (batch.staff_id != staff!.id) return Error("只能删除自己创建的批次");
            if (batch.valid != 0) return Error("请先撤回批次");
            if (batch.claim_count != 0 || await db.ticketShareClaim.AsNoTracking().AnyAsync(c => c.batch_id == batchId)
                || await db.staffCouponBatchTicket.AsNoTracking().AnyAsync(t => t.batch_id == batchId))
                return Error("已有顾客领取，不能删除");
            var originalType = batch.share_type;
            batch.valid = TicketShareBatch.Deleted;
            // 旧公众号固定码的唯一索引不包含 valid；保留类型信息的同时释放旧索引，绝不恢复已删除链接。
            if (originalType == TicketShareBatch.ShareQrCode) batch.share_type = TicketShareBatch.DeletedQrCode;
            batch.update_date = DateTime.Now;
            db.coreDataModLog.Add(CoreDataModLog.CreateManualLog("ticket_share_batch", "valid", batch.id,
                "店员删除未领取的分享批次", null, staff.id, "valid=0;type=" + originalType,
                "valid=-1;type=" + batch.share_type, null));
            await db.SaveChangesAsync();
            return Success(new { batchId });
        }));
    }
}
