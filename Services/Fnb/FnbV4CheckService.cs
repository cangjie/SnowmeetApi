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
    public async Task<object> SaveCheckItem(FnbCheckItemRequest r)
    {
        await Area(r.AreaId, false);
        if (r.Method is not ("yes_no" or "number" or "photo") || r.Kind is not ("environment" or "safety" or "tool" or "supply")) throw new ArgumentException("检查方式或类型无效");
        if (r.Minimum > r.Maximum || r.Method != "number" && (r.Minimum != null || r.Maximum != null)) throw new ArgumentException("检查数值标准无效");
        if (r.ToolId != null) { var t = await Tool(r.ToolId.Value); if (t.area_id != r.AreaId || !t.valid || t.status == "disposed") throw new ArgumentException("工具不在此区域或不可用"); }
        if (r.SupplyId != null) { var s = await Supply(r.SupplyId.Value); if (s.area_id != r.AreaId) throw new ArgumentException("物资不在此区域"); }
        if (r.ToolId != null && r.SupplyId != null || r.Kind == "tool" && r.ToolId == null || r.Kind == "supply" && r.SupplyId == null) throw new ArgumentException("检查项关联对象无效");
        var c = r.Id == 0 ? new FnbCheckItem { valid = true } : await (from c0 in db.fnbCheckItem join a in db.fnbArea on c0.area_id equals a.id where c0.id == r.Id && a.shop_id == shopId select c0).SingleOrDefaultAsync() ?? throw new ArgumentException("本店检查项不存在");
        c.area_id = r.AreaId; c.name = Text(r.Name, 200, "检查项名称"); c.method = r.Method; c.kind = r.Kind; c.required = r.Required;
        c.photo_suggested = r.PhotoSuggested; c.unit = Optional(r.Unit, 40, "单位"); c.minimum = r.Minimum; c.maximum = r.Maximum; c.tool_id = r.ToolId; c.supply_id = r.SupplyId; c.valid = r.Valid; c.updated_at = Now;
        if (r.Id == 0) db.fnbCheckItem.Add(c); else Modified(c); await db.SaveChangesAsync(); return c;
    }
    private async Task<(FnbCheckItem[] Items, string Fingerprint)> CheckConfiguration()
    {
        var areas = await db.fnbArea.Where(x => x.shop_id == shopId).OrderBy(x => x.id).ToArrayAsync();
        var ids = areas.Where(x => x.valid && (x.parent_id == null || areas.Any(p => p.id == x.parent_id && p.valid))).Select(x => x.id).ToArray();
        var items = await db.fnbCheckItem.Where(x => x.valid && ids.Contains(x.area_id)).OrderBy(x => x.id).ToArrayAsync();
        string json = JsonSerializer.Serialize(new { areas = areas.Select(x => new { x.id, x.parent_id, x.name, x.valid, x.updated_at }), items });
        return (items, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
    }
    private async Task<FnbCheckSheet> Sheet(long id) => await db.fnbCheckSheet.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId) ?? throw new ArgumentException("本店检查单不存在");
    private async Task<FnbCheckSheet> EditingSheet(long id)
    { var s = await Sheet(id); if (s.status != "in_progress") throw new ArgumentException("检查单已提交，原始记录不能修改"); return s; }
    public async Task<object> TodayCheck()
    {
        var s = await db.fnbCheckSheet.SingleOrDefaultAsync(x => x.shop_id == shopId && x.business_date == Today);
        return s == null ? new { businessDate = Today, status = "none", items = (await CheckConfiguration()).Items } : await CheckData(s.id);
    }
    public async Task<object> StartCheck()
    {
        var prior = await db.fnbCheckSheet.SingleOrDefaultAsync(x => x.shop_id == shopId && x.business_date == Today);
        if (prior != null) return await CheckData(prior.id);
        var cfg = await CheckConfiguration(); if (cfg.Items.Length == 0) throw new ArgumentException("请先配置启用区域及检查项");
        var sheet = new FnbCheckSheet { shop_id = shopId, business_date = Today, status = "in_progress", fingerprint = cfg.Fingerprint, started_by = StaffId, started_at = Now };
        db.fnbCheckSheet.Add(sheet); await db.SaveChangesAsync();
        foreach (var c in cfg.Items) db.fnbCheckLine.Add(new FnbCheckLine { sheet_id = sheet.id, item_id = c.id, snapshot_json = JsonSerializer.Serialize(c), active = true });
        await db.SaveChangesAsync(); return await CheckData(sheet.id);
    }
    public async Task<object> RefreshCheck(long id)
    {
        var sheet = await EditingSheet(id); var cfg = await CheckConfiguration(); var lines = await db.fnbCheckLine.Where(x => x.sheet_id == id).ToArrayAsync();
        foreach (var l in lines)
        {
            var c = cfg.Items.FirstOrDefault(x => x.id == l.item_id); l.active = c != null;
            if (c != null)
            {
                var before = JsonSerializer.Deserialize<FnbCheckItem>(l.snapshot_json)!;
                if (before.method != c.method || before.minimum != c.minimum || before.maximum != c.maximum || before.unit != c.unit || before.tool_id != c.tool_id || before.supply_id != c.supply_id)
                { l.result = null; l.value = null; l.reason = null; l.upload_id = null; l.bulk = false; }
                l.snapshot_json = JsonSerializer.Serialize(c);
            }
            Modified(l);
        }
        foreach (var c in cfg.Items.Where(x => !lines.Any(l => l.item_id == x.id))) db.fnbCheckLine.Add(new FnbCheckLine { sheet_id = id, item_id = c.id, active = true, snapshot_json = JsonSerializer.Serialize(c) });
        sheet.fingerprint = cfg.Fingerprint; sheet.saved_at = Now; Modified(sheet); await db.SaveChangesAsync(); return await CheckData(id);
    }
    private static string? CheckResult(FnbCheckItem c, string? result, decimal? value)
    {
        if (result != null && result is not ("pass" or "abnormal" or "na")) throw new ArgumentException("检查结果无效");
        if (result == "na") return "na";
        if (c.method != "number") return result;
        if (value == null) return null;
        if (Math.Abs(value.Value) > 1_000_000_000_000m || Round(value.Value) != value) throw new ArgumentException("数值超出支持范围");
        return c.minimum != null && value < c.minimum || c.maximum != null && value > c.maximum ? "abnormal" : "pass";
    }
    public async Task<object> SaveCheck(FnbCheckDraftRequest input)
    {
        var sheet = await EditingSheet(input.SheetId);
        if (input.Lines == null || input.Lines.Select(x => x.ItemId).Distinct().Count() != input.Lines.Length) throw new ArgumentException("检查行重复");
        foreach (var r in input.Lines)
        {
            var l = await db.fnbCheckLine.SingleOrDefaultAsync(x => x.sheet_id == sheet.id && x.item_id == r.ItemId && x.active) ?? throw new ArgumentException("检查行不属于当前快照");
            var c = JsonSerializer.Deserialize<FnbCheckItem>(l.snapshot_json)!; await Photo(r.UploadId);
            l.result = CheckResult(c, r.Result, r.Value); l.value = l.result == "na" ? null : r.Value; l.reason = Optional(r.Reason, 1000, "异常原因"); l.upload_id = r.UploadId;
            l.bulk = false; l.staff_id = StaffId; l.updated_at = Now; Modified(l);
        }
        sheet.saved_at = Now; Modified(sheet); await db.SaveChangesAsync(); return await CheckData(sheet.id);
    }
    public async Task<object> PassAll(long id)
    {
        var sheet = await EditingSheet(id); var lines = await db.fnbCheckLine.Where(x => x.sheet_id == id && x.active && x.result == null).ToArrayAsync();
        foreach (var l in lines)
        {
            if (JsonSerializer.Deserialize<FnbCheckItem>(l.snapshot_json)!.method == "number") continue;
            l.result = "pass"; l.bulk = true; l.staff_id = StaffId; l.updated_at = Now; Modified(l);
        }
        sheet.saved_at = Now; Modified(sheet); await db.SaveChangesAsync(); return await CheckData(id);
    }
    public async Task<object> SubmitCheck(long id)
    {
        var sheet = await EditingSheet(id); var config = await CheckConfiguration();
        if (config.Fingerprint != sheet.fingerprint) throw new FnbConflictException("检查配置已变更，请刷新快照");
        var lines = await db.fnbCheckLine.Where(x => x.sheet_id == id && x.active).ToArrayAsync();
        foreach (var l in lines)
        {
            var c = JsonSerializer.Deserialize<FnbCheckItem>(l.snapshot_json)!; string? calculated = CheckResult(c, l.result, l.value);
            if (c.required && calculated == null) throw new ArgumentException(c.name + "是必填项");
            if (calculated == "abnormal" && string.IsNullOrWhiteSpace(l.reason)) throw new ArgumentException(c.name + "异常须填写原因");
            l.result = calculated; Modified(l);
            if (calculated == "pass" && c.tool_id != null) { var t = await Tool(c.tool_id.Value); t.last_check_date = sheet.business_date; Modified(t); }
        }
        sheet.status = "submitted"; sheet.submitted_by = StaffId; sheet.submitted_at = Now; Modified(sheet); await db.SaveChangesAsync(); return await CheckData(id);
    }
    public async Task<object> HandleCheck(FnbCheckHandleRequest r)
    {
        var l = await (from l0 in db.fnbCheckLine join s in db.fnbCheckSheet on l0.sheet_id equals s.id where l0.id == r.LineId && s.shop_id == shopId && s.status == "submitted" && l0.active && l0.result == "abnormal" select l0).SingleOrDefaultAsync() ?? throw new ArgumentException("异常不存在或检查单不在待确认状态");
        db.fnbCheckHandling.Add(new FnbCheckHandling { line_id = l.id, remark = Text(r.Remark, 1000, "处理说明"), staff_id = StaffId, created_at = Now });
        await db.SaveChangesAsync(); return await CheckData(l.sheet_id);
    }
    public async Task<object> ConfirmCheck(long id)
    {
        var sheet = await Sheet(id); if (sheet.status != "submitted") throw new ArgumentException("检查单不在待确认状态");
        if (await db.fnbCheckLine.AnyAsync(x => x.sheet_id == id && x.active && x.result == "abnormal" && !db.fnbCheckHandling.Any(h => h.line_id == x.id))) throw new ArgumentException("仍有异常没有处理记录");
        sheet.status = "confirmed"; sheet.confirmed_by = StaffId; sheet.confirmed_at = Now; Modified(sheet); await db.SaveChangesAsync(); return await CheckData(id);
    }
    public async Task<object> CheckData(long id)
    {
        var sheet = await Sheet(id); var lines = await db.fnbCheckLine.Where(x => x.sheet_id == id).OrderBy(x => x.item_id).ToArrayAsync();
        var ids = lines.Select(x => x.id).ToArray(); var handling = await db.fnbCheckHandling.Where(x => ids.Contains(x.line_id)).OrderBy(x => x.id).ToArrayAsync();
        return new { sheet, stale = sheet.status == "in_progress" && sheet.fingerprint != (await CheckConfiguration()).Fingerprint,
            total = lines.Count(x => x.active), filled = lines.Count(x => x.active && x.result != null), abnormal = lines.Count(x => x.active && x.result == "abnormal"),
            requiredRemaining = lines.Count(x => x.active && x.result == null && JsonSerializer.Deserialize<FnbCheckItem>(x.snapshot_json)!.required),
            lines = lines.Select(l => new { line = l, item = JsonSerializer.Deserialize<FnbCheckItem>(l.snapshot_json), handlings = handling.Where(h => h.line_id == l.id).ToArray() }).ToArray() };
    }
    public async Task<object> CheckHistory(DateTime? from, DateTime? to)
    {
        var sheets = await db.fnbCheckSheet.Where(x => x.shop_id == shopId && (from == null || x.business_date >= from) && (to == null || x.business_date <= to)).OrderByDescending(x => x.business_date).Take(200).ToArrayAsync();
        var output = new List<object>(); foreach (var s in sheets) output.Add(await CheckData(s.id)); return output;
    }
}
