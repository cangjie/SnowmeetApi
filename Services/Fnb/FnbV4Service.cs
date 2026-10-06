#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed partial class FnbV4Service(ApplicationDBContext db, int shopId, FnbAccess.Actor? actor = null, DateTime? clock = null)
{
    private DateTime Now => clock ?? DateTime.UtcNow;
    private DateTime Today => Now.AddHours(8).Date;
    private int StaffId => actor?.Staff.id ?? throw new InvalidOperationException("写操作缺少员工身份");
    private void Modified<T>(T row) where T : class
    {
        var entry = db.Entry(row); var key = entry.Metadata.FindPrimaryKey()!;
        foreach (var other in db.ChangeTracker.Entries<T>().Where(x => !ReferenceEquals(x.Entity, row)).ToArray())
            if (key.Properties.All(p => Equals(other.Property(p.Name).CurrentValue, entry.Property(p.Name).CurrentValue))) other.State = EntityState.Detached;
        entry.State = EntityState.Modified;
    }
    private static decimal Round(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static decimal Qty(decimal value, bool zero = false) => FnbV4Rules.Quantity(value, zero);
    private static string Text(string? value, int size, string label) => FnbV4Rules.RequiredText(value, size, label);
    private static string? Optional(string? value, int size, string label) => FnbV4Rules.OptionalText(value, size, label);
    private void Manager()
    { if (actor?.Staff.title_level < 200) throw new ArgumentException("此操作需要店长权限"); }
    private async Task<FnbItem> Item(int id) => await db.fnbItem.SingleOrDefaultAsync(x => x.id == id && x.valid) ?? throw new ArgumentException("食材不存在或已停用");
    private async Task<FnbBatch> Batch(int id) => await db.fnbBatch.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId) ?? throw new ArgumentException("本店批次不存在");
    private async Task<FnbItemForm[]> Forms(int itemId) => await db.fnbItemForm.Where(x => x.item_id == itemId && x.valid).OrderBy(x => x.seq).ToArrayAsync();
    private bool Available(FnbBatch b) => b.state == "final" && b.quantity > 0 && b.dispose_status == null && b.effective_expire >= Today && (b.ready_at == null || b.ready_at <= Now);
    private async Task Area(int? id, bool leaf = true, bool required = false)
    {
        if (id == null) { if (required) throw new ArgumentException("请选择存放区域"); return; }
        var a = await db.fnbArea.SingleOrDefaultAsync(x => x.id == id && x.shop_id == shopId && x.valid) ?? throw new ArgumentException("本店存储区域不存在或已停用");
        if (leaf && a.parent_id == null) throw new ArgumentException("请选择下级存储区域");
        if (a.parent_id != null && !await db.fnbArea.AnyAsync(x => x.id == a.parent_id && x.shop_id == shopId && x.valid)) throw new ArgumentException("上级区域已停用");
    }
    private async Task Photo(int? id)
    {
        if (id == null) return;
        if (!await db.UploadFile.AnyAsync(x => x.id == id && (x.staff_id == StaffId || x.owner == actor!.AuditUserId) && (x.purpose == "fnb" || x.purpose == "food" || x.purpose == "食材批次")))
            throw new ArgumentException("照片必须是当前员工上传的食材管理文件");
    }
    private async Task<FnbStockDocument> Document(string type, Guid requestId, string? remark = null, string status = "posted")
    {
        if (requestId == Guid.Empty) throw new ArgumentException("requestId 不能为空");
        var d = new FnbStockDocument { shop_id = shopId, document_type = type, document_no = type.ToUpperInvariant() + "-" + Now.ToString("yyyyMMdd") + "-" + requestId.ToString("N"),
            request_id = requestId, status = status, source_client = actor!.SourceClient, business_date = Today, occurred_at = Now, created_at = Now,
            posted_at = status == "posted" ? Now : null, created_by_staff_id = StaffId, posted_by_staff_id = status == "posted" ? StaffId : null,
            remark = Optional(remark, 2000, "备注") };
        db.fnbStockDocument.Add(d); await db.SaveChangesAsync(); return d;
    }
    private async Task<FnbStockDocumentLine> Line(FnbStockDocument d, FnbItem item, short direction, decimal input, decimal factor = 1, int? formId = null, int? batchId = null)
    {
        var line = new FnbStockDocumentLine { document_id = d.id, shop_id = shopId, line_no = await db.fnbStockDocumentLine.CountAsync(x => x.document_id == d.id) + 1,
            item_id = item.id, item_name = item.name, direction = direction, input_qty = Qty(input, true), input_unit_name = item.base_unit_code, input_to_base = Qty(factor), form_id = formId, specified_batch_id = batchId };
        db.fnbStockDocumentLine.Add(line); await db.SaveChangesAsync(); return line;
    }
    private async Task Move(FnbStockDocumentLine line, FnbBatch batch, short direction, decimal quantity, decimal amount, string emptyStatus = "used_up")
    {
        quantity = Qty(quantity, true); amount = Qty(amount, true);
        batch.quantity = Round(batch.quantity + direction * quantity); batch.amount = Round(batch.amount + direction * amount);
        if (batch.quantity < 0 || batch.amount < 0) throw new FnbConflictException("批次余额不足");
        if (batch.quantity == 0) { batch.amount = 0; batch.dispose_status = emptyStatus; } else batch.dispose_status = null;
        Modified(batch);
        db.fnbStockMovement.Add(new FnbStockMovement { document_line_id = line.id, shop_id = shopId, item_id = batch.item_id, batch_id = batch.id,
            direction = direction, quantity = quantity, amount = amount, balance_qty = batch.quantity, balance_amount = batch.amount, created_at = Now });
        line.actual_qty = Round(line.actual_qty + quantity); line.actual_amount = Round(line.actual_amount + amount); Modified(line);
        await db.SaveChangesAsync();
    }
    private async Task<decimal> Consume(FnbStockDocument d, int itemId, decimal required, bool shortageAllowed = false)
    {
        var item = await Item(itemId); var final = (await Forms(itemId)).Last(); required = Qty(required);
        var batches = await db.fnbBatch.Where(x => x.shop_id == shopId && x.item_id == itemId && x.form_id == final.id && x.state == "final" && x.quantity > 0 && x.dispose_status == null && x.effective_expire >= Today && (x.ready_at == null || x.ready_at <= Now))
            .OrderBy(x => x.effective_expire).ThenBy(x => x.received_at).ThenBy(x => x.id).ToArrayAsync();
        if (!shortageAllowed && batches.Sum(x => x.quantity) < required) throw new ArgumentException(item.name + "出品态库存不足");
        var line = await Line(d, item, -1, required, 1, final.id); decimal left = required;
        foreach (var b in batches)
        {
            decimal n = Math.Min(left, b.quantity); if (n <= 0) break;
            decimal cost = n == b.quantity ? b.amount : Round(b.amount * n / b.quantity);
            await Move(line, b, -1, n, cost); left = Round(left - n);
        }
        return line.actual_amount;
    }
    public async Task<object> NextBatchNo(int itemId)
    {
        var item = await Item(itemId); var category = await db.fnbCategory.SingleAsync(x => x.id == item.category_id);
        string prefix = Today.ToString("yyMMdd") + "-" + category.batch_code + "-";
        var names = await db.fnbBatch.Where(x => x.shop_id == shopId && x.batch_no.StartsWith(prefix)).Select(x => x.batch_no).ToArrayAsync();
        int max = names.Select(x => x[prefix.Length..].Split('-')[0]).Select(x => int.TryParse(x, out int n) ? n : 0).DefaultIfEmpty(0).Max();
        return new { batchNo = prefix + (max + 1).ToString("D2"), reserved = false };
    }
    private async Task<string> NewBatchNo(int itemId) => ((dynamic)await NextBatchNo(itemId)).batchNo;
    private async Task<string> DerivedNo(FnbBatch source, string formCode)
    {
        string prefix = source.batch_no + "-" + formCode;
        string candidate = prefix; int n = 2;
        while (await db.fnbBatch.AnyAsync(x => x.shop_id == shopId && x.batch_no == candidate)) candidate = prefix + "-" + n++;
        return Text(candidate, 100, "派生批次号");
    }
    public async Task<object> PreviewExpiry(int itemId, string storage, DateTime? production, DateTime? expiry)
    {
        var result = await Expiry(itemId, storage, production, expiry);
        return new { expireDate = result.Date, expirySource = result.Source, ruleId = result.RuleId, expired = result.Date < Today };
    }
    private async Task<(DateTime Date, string Source, int? RuleId)> Expiry(int itemId, string storage, DateTime? production, DateTime? expiry)
    {
        if (!FnbV4Rules.Storage(storage)) throw new ArgumentException("储存方式无效");
        var item = await Item(itemId);
        if (production?.Date > Today) throw new ArgumentException("生产日期不能在未来");
        if (expiry != null)
        {
            if (production?.Date > expiry.Value.Date) throw new ArgumentException("生产日期不能晚于到期日期");
            return (expiry.Value.Date, "manual", null);
        }
        if (production == null) throw new ArgumentException("需要到期日期，或生产日期及保质期规则");
        var rules = await db.fnbShelfLifeRule.Where(x => x.valid && (x.item_id == itemId || x.category_id == item.category_id)).ToArrayAsync();
        var rule = FnbV4Rules.ShelfRule(rules, itemId, item.category_id, storage, production.Value.Month) ?? throw new ArgumentException("没有适用的保质期规则，请填写到期日期");
        return (production.Value.Date.AddDays(rule.days), "rule", rule.id);
    }
    public async Task<object> Receipt(FnbReceiptRequest input)
    {
        if (input.Lines == null || input.Lines.Length is < 1 or > 100) throw new ArgumentException("入库须有 1–100 行");
        var d = await Document("receipt", input.RequestId, input.Remark); var outputs = new List<object>();
        foreach (var r in input.Lines)
        {
            var item = await Item(r.ItemId); var forms = await Forms(item.id); var final = forms.Last();
            FnbPurchaseSpec? spec = r.SpecId == null ? null : await db.fnbPurchaseSpec.SingleOrDefaultAsync(x => x.id == r.SpecId && x.item_id == item.id && x.valid) ?? throw new ArgumentException("进货规格不存在或不属于此食材");
            if (item.item_type == "prepared") throw new ArgumentException("半成品请通过制作入库");
            if (forms.Length > 1 && spec == null) throw new ArgumentException("有链路的食材入库必须选择进货规格");
            var form = spec == null ? final : forms.Single(x => x.id == spec.entry_form_id);
            await Area(r.AreaId, required: true); var expiry = await Expiry(item.id, r.StorageType, r.ProductionDate, r.ExpireDate);
            if (expiry.Date < Today) throw new ArgumentException("已过期食材不能入库");
            decimal quantity = Qty(Round(Qty(r.Quantity) * form.per_base));
            string state = form.id == final.id ? "final" : "staged";
            decimal? pack = null; string? label = null;
            if (r.Sealed)
            {
                if (forms.Length != 1) throw new ArgumentException("链路入库不能同时使用无链路封装模式");
                pack = Qty(r.PackSize ?? 0); label = Text(r.PackLabel, 40, "包装单位");
                if (r.Quantity != decimal.Truncate(r.Quantity)) throw new ArgumentException("封装入库须为整数包装数");
                quantity = Qty(Round(r.Quantity * pack.Value)); state = "sealed";
                if (!FnbV4Rules.Storage(r.OpenStorage)) throw new ArgumentException("须填写开封后储存方式");
                FnbV4Rules.Days(r.OpenDays); if (r.OpenDays == null) throw new ArgumentException("须填写开封后天数");
            }
            var b = new FnbBatch { shop_id = shopId, item_id = item.id, form_id = form.id, batch_no = await NewBatchNo(item.id), state = state, quantity = 0, amount = 0,
                pack_size = pack, pack_label = label, storage_type = r.StorageType, production_date = r.ProductionDate?.Date,
                expire_date = expiry.Date, expiry_source = expiry.Source, spec_id = spec?.id, received_at = Now };
            db.fnbBatch.Add(b); await db.SaveChangesAsync();
            db.fnbBatchDetail.Add(new FnbBatchDetail { batch_id = b.id, area_id = r.AreaId, open_storage = r.OpenStorage, open_days = r.OpenDays });
            var line = await Line(d, item, 1, r.Quantity, r.Sealed ? pack!.Value : form.per_base, form.id, b.id);
            line.spec_id = spec?.id; line.input_unit_name = r.Sealed ? label! : form.unit_name;
            await Move(line, b, 1, quantity, Qty(r.Amount, true));
            foreach (int photoId in (r.PhotoIds ?? []).Distinct()) { await Photo(photoId); db.fnbBatchImage.Add(new FnbBatchImage { batch_id = b.id, upload_id = photoId }); }
            await db.SaveChangesAsync(); outputs.Add(await BatchData(b));
        }
        return new { documentId = d.id.ToString(), batches = outputs };
    }
    public async Task<object> DeleteReceipt(FnbDocumentRequest input)
    {
        var d = await db.fnbStockDocument.SingleOrDefaultAsync(x => x.id == input.DocumentId && x.shop_id == shopId && x.document_type == "receipt") ?? throw new ArgumentException("入库单不存在");
        if (d.status != "posted" || Now - d.created_at > TimeSpan.FromMinutes(10)) throw new ArgumentException("只能撤销 10 分钟内且未撤销的入库");
        var lines = await db.fnbStockDocumentLine.Where(x => x.document_id == d.id).ToArrayAsync();
        foreach (var line in lines)
        {
            var b = await Batch(line.specified_batch_id!.Value);
            if (await db.fnbStockMovement.AnyAsync(x => x.batch_id == b.id && x.document_line_id != line.id) || await db.fnbBatch.AnyAsync(x => x.parent_batch_id == b.id)) throw new ArgumentException("批次已有后续流水，不能撤销");
            await Move(line, b, -1, b.quantity, b.amount);
        }
        d.status = "cancelled"; Modified(d); await db.SaveChangesAsync(); return new { documentId = d.id.ToString(), status = d.status };
    }
    public async Task<object> BatchData(FnbBatch b)
    {
        var form = await db.fnbItemForm.SingleAsync(x => x.id == b.form_id);
        var item = await db.fnbItem.SingleAsync(x => x.id == b.item_id);
        var detail = await db.fnbBatchDetail.SingleOrDefaultAsync(x => x.batch_id == b.id);
        var photos = await (from i in db.fnbBatchImage join u in db.UploadFile on i.upload_id equals u.id where i.batch_id == b.id select new { u.id, u.file_path_name, u.thumb }).ToArrayAsync();
        return new { batch = b, itemName = item.name, formName = form.name, unitName = b.state == "sealed" ? b.pack_label : form.unit_name,
            displayQuantity = b.state == "sealed" ? b.quantity / b.pack_size : b.quantity / form.per_base, baseUnit = item.base_unit_code,
            available = Available(b), remainingDays = (b.effective_expire - Today).Days, areaId = detail?.area_id, photos,
            label = new { batchId = b.id, batchNo = b.batch_no, name = item.name, formName = form.name, quantity = b.quantity,
                baseUnit = item.base_unit_code, effectiveExpire = b.effective_expire, storageType = b.storage_type, qrUrl = "https://mini.snowmeet.top/fnb/b?id=" + b.id } };
    }
    public async Task<object> GetBatch(int id) => await BatchData(await Batch(id));
    public async Task<object> GetLabel(int id)
    {
        var b = await Batch(id); var item = await db.fnbItem.SingleAsync(x => x.id == b.item_id); var form = await db.fnbItemForm.SingleAsync(x => x.id == b.form_id);
        return new { batchId = b.id.ToString(), name = item.name, formName = form.name, batchNo = b.batch_no, expireDate = b.effective_expire.ToString("yyyy-MM-dd"),
            originalExpireDate = b.expire_date.ToString("yyyy-MM-dd"), storageType = b.storage_type, quantity = b.quantity, baseUnit = item.base_unit_code,
            qrUrl = "https://mini.snowmeet.top/fnb/b?id=" + b.id };
    }
    public async Task<object> LowStocks()
    {
        var supplies = await db.fnbSupply.Where(x => x.shop_id == shopId && x.valid).ToArrayAsync();
        return new { food = (await Stock()).Where(x => x.LowStock).ToArray(), supplies = supplies.Select(x => new { supply = x, threshold = x.low_stock_qty ?? Round(x.last_receipt_qty * (x.low_stock_ratio ?? .1m)) }).Where(x => x.supply.quantity < x.threshold).ToArray() };
    }
    public async Task<object> Dispose(FnbBatchWriteRequest input, bool destroy)
    {
        var b = await Batch(input.BatchId); if (b.quantity <= 0) throw new ArgumentException("批次已用尽");
        if (destroy && (b.effective_expire >= Today || !input.ConfirmExpired)) throw new ArgumentException("销毁须确认已过期批次");
        var item = await Item(b.item_id); decimal n = Qty(input.Quantity); if (n > b.quantity) throw new ArgumentException("数量超过批次余额");
        if (b.state == "sealed" && n % b.pack_size!.Value != 0) throw new ArgumentException("封装批次须按整包装报损或销毁");
        var d = await Document(destroy ? "destroy" : "waste", input.RequestId, Text(input.Reason, 600, "原因"));
        var line = await Line(d, item, -1, n, 1, b.form_id, b.id);
        await Move(line, b, -1, n, n == b.quantity ? b.amount : Round(b.amount * n / b.quantity), destroy ? "destroyed" : "wasted");
        return new { documentId = d.id.ToString(), batch = await BatchData(b) };
    }
    public sealed record StockRow(int ItemId, string Name, int CategoryId, string BaseUnit, decimal AvailableQuantity, decimal StagedQuantity, decimal SealedQuantity,
        decimal TotalQuantity, decimal Amount, decimal LowThreshold, bool LowStock, int BatchCount, decimal? Servings, object[] Layers);
    public async Task<StockRow[]> Stock(int? categoryId = null, string? storage = null, string? keyword = null)
    {
        var items = await db.fnbItem.Where(x => x.valid && (keyword == null || x.name.Contains(keyword))).ToArrayAsync();
        if (categoryId != null)
        {
            var ids = await db.fnbCategory.Where(x => x.id == categoryId || x.parent_id == categoryId).Select(x => x.id).ToArrayAsync();
            items = items.Where(x => ids.Contains(x.category_id)).ToArray();
        }
        var batches = await db.fnbBatch.Where(x => x.shop_id == shopId && x.quantity > 0 && x.dispose_status == null && (storage == null || x.storage_type == storage)).ToArrayAsync();
        var forms = await db.fnbItemForm.Where(x => x.valid).ToArrayAsync();
        var receipts = await (from l in db.fnbStockDocumentLine join d in db.fnbStockDocument on l.document_id equals d.id
                              where d.shop_id == shopId && d.document_type == "receipt" && d.status == "posted" select new { l.item_id, l.actual_qty, d.created_at, l.id }).ToArrayAsync();
        var recipeLines = await (from l in db.fnbRecipeLine join r in db.fnbRecipe on l.recipe_id equals r.id
                                 where r.shop_id == shopId && r.recipe_type == "dish" && r.status == "published" select l).ToArrayAsync();
        var result = new List<StockRow>();
        foreach (var it in items)
        {
            var bs = batches.Where(x => x.item_id == it.id).ToArray();
            decimal available = bs.Where(Available).Sum(x => x.quantity);
            decimal latest = receipts.Where(x => x.item_id == it.id).OrderByDescending(x => x.created_at).ThenByDescending(x => x.id).Select(x => x.actual_qty).FirstOrDefault();
            decimal low = it.low_stock_qty ?? Round(latest * (it.low_stock_ratio ?? .1m));
            var uses = recipeLines.Where(x => x.item_id == it.id).Select(x => decimal.Floor(available / x.quantity)).ToArray();
            var layers = bs.GroupBy(x => x.form_id).Select(g => (object)new { formId = g.Key, formName = forms.FirstOrDefault(x => x.id == g.Key)?.name,
                quantity = g.Sum(x => x.quantity), displayQuantity = g.Sum(x => x.quantity) / (forms.FirstOrDefault(x => x.id == g.Key)?.per_base ?? 1),
                availableQuantity = g.Where(Available).Sum(x => x.quantity), batches = g.ToArray() }).ToArray();
            result.Add(new(it.id, it.name, it.category_id, it.base_unit_code, available, bs.Where(x => x.state == "staged").Sum(x => x.quantity),
                bs.Where(x => x.state == "sealed").Sum(x => x.quantity), bs.Sum(x => x.quantity), bs.Sum(x => x.amount), low, available < low, bs.Length, uses.Length == 0 ? null : uses.Min(), layers));
        }
        return result.ToArray();
    }
    public async Task<object> ListExpiry(string? kind)
    {
        if (kind != null && kind is not ("expired" or "today" or "near")) throw new ArgumentException("到期筛选无效");
        var batches = await db.fnbBatch.Where(x => x.shop_id == shopId && x.quantity > 0 && x.dispose_status == null).OrderBy(x => x.effective_expire).ToArrayAsync();
        var items = await db.fnbItem.ToArrayAsync(); var cats = await db.fnbCategory.ToArrayAsync(); var output = new List<object>();
        foreach (var b in batches)
        {
            var item = items.Single(x => x.id == b.item_id); int warn = FnbV4Rules.Defaults(item, cats.Single(x => x.id == item.category_id)).WarnDays;
            string k = b.effective_expire < Today ? "expired" : b.effective_expire == Today ? "today" : "near";
            if (b.effective_expire <= Today.AddDays(warn) && (kind == null || kind == k)) output.Add(new { kind = k, data = await BatchData(b) });
        }
        return output;
    }
    public async Task<object> LowRule(FnbLowRuleRequest r)
    {
        ValidateLow(r.Ratio, r.Quantity); var item = await Item(r.ItemId); item.low_stock_ratio = r.Ratio; item.low_stock_qty = r.Quantity; Modified(item); await db.SaveChangesAsync();
        return (await Stock()).Single(x => x.ItemId == item.id);
    }
    private static void ValidateLow(decimal? ratio, decimal? quantity)
    {
        if ((ratio == null) == (quantity == null)) throw new ArgumentException("比例和固定预警值须二选一");
        if (ratio != null && (ratio <= 0 || ratio > 1 || decimal.Round(ratio.Value, 4) != ratio)) throw new ArgumentException("预警比例须在 0–1 之间且最多 4 位小数");
        if (quantity != null) Qty(quantity.Value, true);
    }
}
