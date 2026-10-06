#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Models.Fnb;
namespace SnowmeetApi.Services.Fnb;

public sealed partial class FnbV4Service
{
    public async Task<object> ListAreas(bool includeDisabled = false)
    {
        var areas = await db.fnbArea.Where(x => x.shop_id == shopId && (includeDisabled || x.valid)).OrderBy(x => x.sort).ThenBy(x => x.id).ToArrayAsync();
        var ids = areas.Select(x => x.id).ToArray(); var images = await (from i in db.fnbAreaImage join u in db.UploadFile on i.upload_id equals u.id where ids.Contains(i.area_id) select new { i.area_id, i.upload_id, i.staff_id, i.created_at, u.file_path_name }).ToArrayAsync();
        var checks = await db.fnbCheckItem.Where(x => ids.Contains(x.area_id)).ToArrayAsync();
        var details = await (from d in db.fnbBatchDetail join b in db.fnbBatch on d.batch_id equals b.id where b.shop_id == shopId && b.quantity > 0 select d.area_id).ToArrayAsync();
        var supplies = await db.fnbSupply.Where(x => x.shop_id == shopId).Select(x => x.area_id).ToArrayAsync();
        var tools = await db.fnbTool.Where(x => x.shop_id == shopId && x.status != "disposed").Select(x => x.area_id).ToArrayAsync();
        return areas.Select(a => new { area = a, effectiveValid = a.valid && (a.parent_id == null || areas.Any(p => p.id == a.parent_id && p.valid)),
            photos = images.Where(x => x.area_id == a.id).ToArray(), checks = checks.Where(x => x.area_id == a.id).ToArray(),
            bindings = new { food = details.Count(x => x == a.id), supplies = supplies.Count(x => x == a.id), tools = tools.Count(x => x == a.id), checks = checks.Count(x => x.area_id == a.id && x.valid) } }).ToArray();
    }
    public async Task<object> SaveArea(FnbAreaSaveRequest input)
    {
        string name = Text(input.Name, 100, "区域名称");
        if (input.AreaType is not ("other" or "kitchen" or "front" or "warehouse" or "cold")) throw new ArgumentException("区域类型无效");
        if (input.ParentId != null)
        {
            var parent = await db.fnbArea.SingleOrDefaultAsync(x => x.id == input.ParentId && x.shop_id == shopId && x.valid && x.parent_id == null) ?? throw new ArgumentException("上级须为本店启用的一级区域");
            if (input.Id == parent.id) throw new ArgumentException("区域不能挂到自身");
        }
        if (await db.fnbArea.AnyAsync(x => x.shop_id == shopId && x.parent_id == input.ParentId && x.name == name && x.valid && x.id != input.Id) && input.Valid) throw new ArgumentException("已有同名区域");
        var a = input.Id == 0 ? new FnbArea { shop_id = shopId, created_at = Now, valid = true } : await db.fnbArea.SingleOrDefaultAsync(x => x.id == input.Id && x.shop_id == shopId) ?? throw new ArgumentException("本店区域不存在");
        if (input.Id != 0 && a.parent_id != input.ParentId && (await db.fnbArea.AnyAsync(x => x.parent_id == a.id) || await AreaBound(a.id))) throw new ArgumentException("已绑定或有下级的区域不能调整层级");
        a.parent_id = input.ParentId; a.name = name; a.area_type = input.AreaType; a.valid = input.Valid; a.sort = input.Sort; a.updated_at = Now;
        if (input.Id == 0) db.fnbArea.Add(a); else Modified(a); await db.SaveChangesAsync(); return a;
    }
    private async Task<bool> AreaBound(int id) => await db.fnbBatchDetail.AnyAsync(x => x.area_id == id) || await db.fnbSupply.AnyAsync(x => x.area_id == id) ||
        await db.fnbTool.AnyAsync(x => x.area_id == id) || await db.fnbCheckItem.AnyAsync(x => x.area_id == id) || await db.fnbAreaImage.AnyAsync(x => x.area_id == id) ||
        await db.fnbToolLog.AnyAsync(x => x.from_area_id == id || x.to_area_id == id);
    public async Task<object> DeleteArea(int id)
    {
        var a = await db.fnbArea.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId) ?? throw new ArgumentException("本店区域不存在");
        if (await db.fnbArea.AnyAsync(x => x.parent_id == id) || await AreaBound(id)) throw new ArgumentException("区域有绑定或下级，只能停用");
        db.fnbArea.Remove(a); await db.SaveChangesAsync(); return new { id };
    }
    public async Task<object> AreaImage(FnbAreaImageRequest input, bool remove)
    {
        if (!await db.fnbArea.AnyAsync(x => x.id == input.AreaId && x.shop_id == shopId)) throw new ArgumentException("本店区域不存在");
        var image = await db.fnbAreaImage.SingleOrDefaultAsync(x => x.area_id == input.AreaId && x.upload_id == input.UploadId);
        if (remove) { if (image != null) db.fnbAreaImage.Remove(image); }
        else
        {
            await Area(input.AreaId, false); await Photo(input.UploadId);
            if (image == null)
            {
                if (await db.fnbAreaImage.CountAsync(x => x.area_id == input.AreaId) >= 6) throw new ArgumentException("每个区域最多 6 张照片");
                db.fnbAreaImage.Add(new FnbAreaImage { area_id = input.AreaId, upload_id = input.UploadId, staff_id = StaffId, created_at = Now });
            }
        }
        await db.SaveChangesAsync(); return new { areaId = input.AreaId, uploadId = input.UploadId };
    }
    private async Task<FnbSupply> Supply(int id, bool active = true) => await db.fnbSupply.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId && (!active || x.valid)) ?? throw new ArgumentException("本店物资不存在或已停用");
    public async Task<object> SaveSupply(FnbSupplySaveRequest input)
    {
        await Area(input.AreaId); if (input.SupplyType is not ("disposable" or "reusable") || input.PackSize <= 0) throw new ArgumentException("物资类型或包装数量无效");
        string name = Text(input.Name, 200, "物资名称");
        if (input.Valid && await db.fnbSupply.AnyAsync(x => x.shop_id == shopId && x.name == name && x.valid && x.id != input.Id)) throw new ArgumentException("本店已有同名物资");
        var s = input.Id == 0 ? new FnbSupply { shop_id = shopId, valid = true, created_at = Now, low_stock_ratio = .1m } : await Supply(input.Id, false);
        if (s.id != 0 && s.supply_type != input.SupplyType && await db.fnbSupplyMovement.AnyAsync(x => x.supply_id == s.id)) throw new ArgumentException("已有流水的物资不能更换类型");
        s.name = name; s.supply_type = input.SupplyType; s.pack_label = Text(input.PackLabel, 40, "包装名称"); s.pack_size = input.PackSize; s.area_id = input.AreaId;
        s.spec = Optional(input.Spec, 200, "规格"); s.valid = input.Valid;
        if (input.Id == 0) db.fnbSupply.Add(s); else Modified(s); await db.SaveChangesAsync(); return s;
    }
    public async Task<object> ListSupplies(string? type = null)
    {
        var rows = await db.fnbSupply.Where(x => x.shop_id == shopId && x.valid && (type == null || x.supply_type == type)).OrderBy(x => x.id).ToArrayAsync();
        return rows.Select(s => new { supply = s, threshold = s.low_stock_qty ?? Round(s.last_receipt_qty * (s.low_stock_ratio ?? .1m)),
            lowStock = s.quantity < (s.low_stock_qty ?? Round(s.last_receipt_qty * (s.low_stock_ratio ?? .1m))), packQuantity = Round(s.quantity / s.pack_size) }).ToArray();
    }
    public async Task<object> PostSupply(FnbSupplyPostRequest input)
    {
        var s = await Supply(input.SupplyId); Qty(input.Quantity);
        if (input.Quantity != decimal.Truncate(input.Quantity)) throw new ArgumentException("物资数量须为整数");
        if (input.Type is not ("in" or "out" or "waste")) throw new ArgumentException("物资流水类型无效");
        if (input.Type == "out" && s.supply_type == "reusable") throw new ArgumentException("可重复使用餐具只登记补充及报损");
        decimal n = Qty(input.Type == "in" ? input.Quantity * s.pack_size : input.Quantity);
        if (input.Type != "in" && n > s.quantity) throw new ArgumentException("物资库存不足");
        string? reason = input.Type == "in" ? Optional(input.Reason, 600, "说明") : Text(input.Reason, 600, "领用或报损原因");
        s.quantity += input.Type == "in" ? n : -n; if (input.Type == "in") s.last_receipt_qty = n; Modified(s);
        var m = new FnbSupplyMovement { supply_id = s.id, request_id = input.RequestId, movement_type = input.Type, input_qty = input.Quantity, quantity = n,
            balance_qty = s.quantity, reason = reason, staff_id = StaffId, created_at = Now, cancelled = false };
        db.fnbSupplyMovement.Add(m); await db.SaveChangesAsync(); return new { supply = s, movement = m };
    }
    public async Task<object> UndoSupply(FnbSupplyUndoRequest input)
    {
        var m = await (from m0 in db.fnbSupplyMovement join s in db.fnbSupply on m0.supply_id equals s.id where m0.id == input.MovementId && s.shop_id == shopId select m0).SingleOrDefaultAsync() ?? throw new ArgumentException("流水不存在");
        if (m.cancelled || m.movement_type != "in" || Now - m.created_at > TimeSpan.FromMinutes(10) || await db.fnbSupplyMovement.AnyAsync(x => x.supply_id == m.supply_id && !x.cancelled && x.id > m.id)) throw new ArgumentException("只能撤销 10 分钟内且无后续流水的入库");
        var s0 = await Supply(m.supply_id, false); if (s0.quantity < m.quantity) throw new FnbConflictException("物资库存已变化");
        s0.quantity -= m.quantity; s0.last_receipt_qty = await db.fnbSupplyMovement.Where(x => x.supply_id == m.supply_id && x.id != m.id && !x.cancelled && x.movement_type == "in").OrderByDescending(x => x.id).Select(x => x.quantity).FirstOrDefaultAsync();
        m.cancelled = true; Modified(m); Modified(s0); await db.SaveChangesAsync(); return new { supply = s0, movement = m };
    }
    public async Task<object> SupplyLow(FnbSupplyLowRequest input)
    { ValidateLow(input.Ratio, input.Quantity); var s = await Supply(input.SupplyId); s.low_stock_ratio = input.Ratio; s.low_stock_qty = input.Quantity; Modified(s); await db.SaveChangesAsync(); return s; }
    public async Task<object> SupplyLog(int id)
    { await Supply(id, false); return await db.fnbSupplyMovement.Where(x => x.supply_id == id).OrderByDescending(x => x.id).Take(200).ToArrayAsync(); }
    private async Task<FnbTool> Tool(int id) => await db.fnbTool.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId) ?? throw new ArgumentException("本店工具不存在");
    public async Task<object> SaveTool(FnbToolSaveRequest input)
    {
        await Area(input.AreaId); if (input.Quantity <= 0) throw new ArgumentException("工具数量须为正整数");
        if (input.OwnerStaffId != null && !await db.staff.AnyAsync(x => x.id == input.OwnerStaffId && x.base_shop_id == shopId && x.valid == 1)) throw new ArgumentException("责任人须为本店在职员工");
        var t = input.Id == 0 ? new FnbTool { shop_id = shopId, created_at = Now, valid = true, status = "normal" } : await Tool(input.Id);
        int? oldArea = t.area_id; string oldStatus = t.status;
        t.name = Text(input.Name, 200, "工具名称"); t.spec = Optional(input.Spec, 200, "规格"); t.quantity = input.Quantity; t.area_id = input.AreaId;
        t.owner_staff_id = input.OwnerStaffId; t.daily_check = input.DailyCheck; t.valid = input.Valid;
        t.asset_no = Text(input.AssetNo ?? (input.Id == 0 ? "K-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() : t.asset_no), 64, "工具编号");
        if (input.DailyCheck && input.AreaId == null) throw new ArgumentException("每日检查工具须配置区域");
        if (input.Id == 0) db.fnbTool.Add(t); else Modified(t); await db.SaveChangesAsync();
        if (input.Id == 0 || oldArea != t.area_id) db.fnbToolLog.Add(new FnbToolLog { tool_id = t.id, from_status = oldStatus, to_status = t.status, from_area_id = oldArea, to_area_id = t.area_id, remark = input.Id == 0 ? "建档" : "修改档案位置", staff_id = StaffId, created_at = Now });
        var ci = await db.fnbCheckItem.FirstOrDefaultAsync(x => x.tool_id == t.id);
        if (t.daily_check && t.valid && t.status != "disposed")
        {
            if (ci == null) { ci = new FnbCheckItem { valid = true, tool_id = t.id }; db.fnbCheckItem.Add(ci); }
            else Modified(ci);
            ci.area_id = t.area_id!.Value; ci.name = t.name + "状态"; ci.kind = "tool"; ci.method = "yes_no"; ci.required = true; ci.valid = true; ci.updated_at = Now;
        }
        else if (ci != null) { ci.valid = false; ci.updated_at = Now; Modified(ci); }
        await db.SaveChangesAsync(); return t;
    }
    public async Task<object> ChangeTool(FnbToolChangeRequest input)
    {
        var t = await Tool(input.ToolId); if (!t.valid || t.status == "disposed") throw new ArgumentException("工具已停用或报废");
        string old = t.status; int? oldArea = t.area_id; string? remark = Optional(input.Remark, 600, "说明");
        if (input.Status != null && input.Status != old)
        {
            string[] allowed = old switch { "normal" => ["damaged", "missing"], "damaged" => ["repairing", "disposed"], "repairing" => ["normal"], "missing" => ["normal"], _ => [] };
            if (!allowed.Contains(input.Status)) throw new ArgumentException("工具状态转换无效");
            if (input.Status == "disposed") Manager();
            if ((input.Status == "disposed" || old == "missing" && input.Status == "normal") && remark == null) throw new ArgumentException("报废或找回须填写说明");
            t.status = input.Status;
        }
        if (input.AreaId != null) { await Area(input.AreaId); t.area_id = input.AreaId; }
        if (old == t.status && oldArea == t.area_id) throw new ArgumentException("状态与位置没有变化");
        Modified(t); db.fnbToolLog.Add(new FnbToolLog { tool_id = t.id, from_status = old, to_status = t.status, from_area_id = oldArea, to_area_id = t.area_id, remark = remark, staff_id = StaffId, created_at = Now });
        var checks = await db.fnbCheckItem.Where(x => x.tool_id == t.id).ToArrayAsync();
        foreach (var c in checks) { if (t.area_id != null) c.area_id = t.area_id.Value; if (t.status == "disposed") c.valid = false; c.updated_at = Now; Modified(c); }
        await db.SaveChangesAsync(); return t;
    }
    public async Task<object> ListTools(string? status = null) => await db.fnbTool.Where(x => x.shop_id == shopId && x.valid && (status == null || x.status == status || status == "abn" && (x.status == "missing" || x.status == "damaged"))).OrderBy(x => x.id).ToArrayAsync();
    public async Task<object> ToolLog(int id) { await Tool(id); return await db.fnbToolLog.Where(x => x.tool_id == id).OrderByDescending(x => x.id).Take(200).ToArrayAsync(); }
}
