using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

public sealed record FnbCategoryInput(int ShopId, int Id, int? ParentId, byte Level, string Name,
    string? BatchCode = null, string? MeasureType = null, string? DefaultStorage = null,
    int? WarnDays = null, int? OpenDays = null, bool IsPrepared = false, int Sort = 0, bool Valid = true);
public sealed record FnbCategoryDeleteInput(int ShopId, int Id);
public sealed record FnbShelfRuleInput(int ShopId, int Id, int? CategoryId, int? ItemId,
    string StorageType, string Season, int Days, bool Valid = true);
public sealed record FnbCreateItemInput(int ShopId, int CategoryId, string Name, string FinalFormName,
    string BaseUnitCode, string FinalFormCode = "F", string? StorageType = null,
    int? WarnDays = null, int? OpenDays = null, int? ImageId = null);
public sealed record FnbItemDefaultsInput(int ShopId, int ItemId, int? WarnDays, int? OpenDays);
public sealed record FnbUpstreamInput(int ShopId, int ItemId, string Name, string UnitName,
    decimal PerBase, string StorageType, string FormCode, string OpName,
    decimal StandardYield = 1, decimal DurationHours = 0, int? ShelfAfterOpDays = null);
public sealed record FnbFormInput(int ShopId, int ItemId, int Id, string Name, string UnitName,
    decimal PerBase, string StorageType, string FormCode, int? ShelfAfterOpDays = null,
    string? OpName = null, decimal StandardYield = 1, decimal DurationHours = 0);
public sealed record FnbRemoveFormInput(int ShopId, int ItemId, int Id);
public sealed record FnbSpecInput(int ShopId, int ItemId, int Id, int EntryFormId, string Name,
    string? Brand = null, string? PackDesc = null, string? Barcode = null, int Sort = 0, bool Valid = true);

// 写方法由控制器的 Serializable 事务包住，结构变化和引用检查在同一事务内。
public sealed class FnbV4CatalogService(ApplicationDBContext db)
{
    private static ArgumentException Invalid(string message) => new(message);
    private void Modified<T>(T row) where T : class => db.Entry(row).State = EntityState.Modified;

    public async Task<FnbCategory> SaveCategoryAsync(FnbCategoryInput input)
    {
        if (input.Id < 0 || input.Level is not (1 or 2)) throw Invalid("分类编号或层级无效");
        string name = FnbV4Rules.RequiredText(input.Name, 100, "分类名称");
        FnbV4Rules.Days(input.WarnDays); FnbV4Rules.Days(input.OpenDays);
        if (input.Level == 1 && (input.ParentId != null || input.BatchCode != null || input.MeasureType != null ||
            input.DefaultStorage != null || input.WarnDays != null || input.OpenDays != null || input.IsPrepared))
            throw Invalid("一级分类只维护名称和排序；属性放在二级分类");
        string? batchCode = null;
        if (input.Level == 2)
        {
            if (input.ParentId == null || !await db.fnbCategory.AnyAsync(x => x.id == input.ParentId && x.level == 1 && x.valid)) throw Invalid("父分类须为有效一级分类");
            if (!FnbV4Rules.Storage(input.DefaultStorage) || FnbV4Rules.Dimension(input.MeasureType) == 0) throw Invalid("计量类型或储存方式无效");
            batchCode = FnbV4Rules.Code(input.BatchCode, "批次编码");
            if (input.Valid && await db.fnbCategory.AnyAsync(x => x.id != input.Id && x.valid && x.batch_code == batchCode)) throw Invalid("批次编码已使用");
        }
        if (input.Valid && await db.fnbCategory.AnyAsync(x => x.id != input.Id && x.valid && x.parent_id == input.ParentId && x.name == name)) throw Invalid("同级分类名称已存在");
        var row = input.Id == 0 ? new FnbCategory { valid = true, created_at = DateTime.UtcNow } : await db.fnbCategory.AsNoTracking().SingleOrDefaultAsync(x => x.id == input.Id);
        if (row == null) throw Invalid("分类不存在");
        if (row.id != 0)
        {
            if (row.level != input.Level) throw Invalid("分类层级不可修改");
            if (row.valid && !input.Valid) throw Invalid("停用分类请使用 DeleteCategory");
            if (row.measure_type != input.MeasureType || row.is_prepared != input.IsPrepared)
                if (await db.fnbItem.AnyAsync(x => x.category_id == row.id)) throw Invalid("已有食材的分类不可更改计量类型或原料/半成品类型");
        }
        row.parent_id = input.ParentId; row.level = input.Level; row.name = name;
        row.batch_code = batchCode; row.measure_type = input.MeasureType; row.default_storage = input.DefaultStorage;
        row.warn_days = input.WarnDays; row.open_days = input.OpenDays; row.is_prepared = input.IsPrepared;
        row.sort = input.Sort; row.valid = input.Valid; row.updated_at = DateTime.UtcNow;
        if (row.id == 0) db.fnbCategory.Add(row); else Modified(row);
        await db.SaveChangesAsync(); return row;
    }

    public async Task<int[]> DeleteCategoryAsync(int id)
    {
        var root = await db.fnbCategory.AsNoTracking().SingleOrDefaultAsync(x => x.id == id && x.valid) ?? throw Invalid("分类不存在或已停用");
        var rows = await db.fnbCategory.AsNoTracking().Where(x => x.id == id || x.parent_id == id).ToListAsync();
        var ids = rows.Select(x => x.id).ToArray();
        if (await db.fnbItem.AnyAsync(x => ids.Contains(x.category_id))) throw Invalid("分类下仍有食材，不能删除");
        foreach (var row in rows) { row.valid = false; row.updated_at = DateTime.UtcNow; Modified(row); }
        await db.SaveChangesAsync(); return ids;
    }

    public async Task<FnbShelfLifeRule> SaveRuleAsync(FnbShelfRuleInput input)
    {
        if (input.Id < 0 || (input.ItemId == null) == (input.CategoryId == null) ||
            !FnbV4Rules.Storage(input.StorageType) || input.Season is not ("all" or "warm" or "cold") || input.Days is <= 0 or > 36500)
            throw Invalid("保质期须归属食材或二级分类中的一个，且季节、天数、储存方式有效");
        if (input.CategoryId != null && !await db.fnbCategory.AnyAsync(x => x.id == input.CategoryId && x.level == 2 && x.valid) ||
            input.ItemId != null && !await db.fnbItem.AnyAsync(x => x.id == input.ItemId && x.valid)) throw Invalid("规则归属不存在或已停用");
        if (input.Valid && await db.fnbShelfLifeRule.AnyAsync(x => x.id != input.Id && x.valid && x.category_id == input.CategoryId && x.item_id == input.ItemId && x.storage_type == input.StorageType && x.season == input.Season)) throw Invalid("该归属已有同储存方式、同季节的规则");
        var row = input.Id == 0 ? new FnbShelfLifeRule { valid = true, created_at = DateTime.UtcNow } : await db.fnbShelfLifeRule.AsNoTracking().SingleOrDefaultAsync(x => x.id == input.Id);
        if (row == null) throw Invalid("规则不存在");
        if (row.id != 0 && (row.item_id != input.ItemId || row.category_id != input.CategoryId)) throw Invalid("规则归属不可修改");
        row.item_id = input.ItemId; row.category_id = input.CategoryId; row.storage_type = input.StorageType;
        row.season = input.Season; row.days = input.Days; row.valid = input.Valid; row.updated_at = DateTime.UtcNow;
        if (row.id == 0) db.fnbShelfLifeRule.Add(row); else Modified(row);
        await db.SaveChangesAsync(); return row;
    }

    private async Task<FnbItem> ItemAsync(int id) => await db.fnbItem.AsNoTracking().SingleOrDefaultAsync(x => x.id == id && x.valid) ?? throw Invalid("食材不存在或已停用");
    private Task<List<FnbItemForm>> FormsAsync(int itemId) => db.fnbItemForm.AsNoTracking().Where(x => x.item_id == itemId && x.valid).OrderBy(x => x.seq).ToListAsync();

    public async Task<object> GetRouteAsync(int itemId)
    {
        var item = await ItemAsync(itemId);
        var category = await db.fnbCategory.AsNoTracking().SingleAsync(x => x.id == item.category_id);
        var defaults = FnbV4Rules.Defaults(item, category);
        return new { item, defaults = new { warnDays = defaults.WarnDays, openDays = defaults.OpenDays,
            storageType = category.default_storage, measureType = category.measure_type },
            forms = await FormsAsync(itemId),
            specs = await db.fnbPurchaseSpec.AsNoTracking().Where(x => x.item_id == itemId).OrderBy(x => x.sort).ThenBy(x => x.id).ToListAsync(),
            shelfRules = await db.fnbShelfLifeRule.AsNoTracking().Where(x => x.valid && (x.item_id == itemId || x.category_id == item.category_id)).OrderBy(x => x.id).ToListAsync() };
    }

    public async Task<FnbItem> CreateItemAsync(FnbCreateItemInput input)
    {
        var category = await db.fnbCategory.AsNoTracking().SingleOrDefaultAsync(x => x.id == input.CategoryId && x.level == 2 && x.valid) ?? throw Invalid("须选择有效二级分类");
        var name = FnbV4Rules.RequiredText(input.Name, 200, "食材名称");
        if (input.BaseUnitCode != FnbV4Rules.BaseUnit(category.measure_type) ||
            !await db.fnbUnit.AnyAsync(x => x.code == input.BaseUnitCode && x.valid && x.dimension == FnbV4Rules.Dimension(category.measure_type))) throw Invalid("基本单位须与分类计量类型一致，且为 g、ml 或 piece");
        if (await db.fnbItem.AnyAsync(x => x.category_id == input.CategoryId && x.name == name && x.valid)) throw Invalid("该分类已有同名食材");
        if (input.ImageId != null && !await db.UploadFile.AnyAsync(x => x.id == input.ImageId)) throw Invalid("照片不存在");
        FnbV4Rules.Days(input.WarnDays); FnbV4Rules.Days(input.OpenDays);
        var formName = FnbV4Rules.RequiredText(input.FinalFormName, 100, "出品态名称");
        var formCode = FnbV4Rules.Code(input.FinalFormCode, "形态编码");
        var storage = input.StorageType ?? category.default_storage!;
        if (!FnbV4Rules.Storage(storage)) throw Invalid("储存方式无效");
        var item = new FnbItem { category_id = category.id, name = name, item_type = category.is_prepared ? "prepared" : "raw",
            base_unit_code = input.BaseUnitCode, warn_days = input.WarnDays, open_days = input.OpenDays, image_id = input.ImageId,
            valid = true, created_at = DateTime.UtcNow };
        db.fnbItem.Add(item); await db.SaveChangesAsync();
        db.fnbItemForm.Add(new FnbItemForm { item_id = item.id, seq = 0, name = formName, unit_name = input.BaseUnitCode,
            per_base = 1, storage_type = storage, form_code = formCode, valid = true, created_at = DateTime.UtcNow });
        await db.SaveChangesAsync(); return item;
    }

    public async Task<FnbItem> SaveDefaultsAsync(FnbItemDefaultsInput input)
    {
        FnbV4Rules.Days(input.WarnDays); FnbV4Rules.Days(input.OpenDays);
        var item = await ItemAsync(input.ItemId); item.warn_days = input.WarnDays; item.open_days = input.OpenDays;
        item.updated_at = DateTime.UtcNow; Modified(item); await db.SaveChangesAsync(); return item;
    }

    public async Task<FnbItemForm> AddUpstreamAsync(FnbUpstreamInput input)
    {
        await ItemAsync(input.ItemId);
        var forms = await FormsAsync(input.ItemId);
        if (forms.Count == 0) throw Invalid("食材缺少出品态");
        FnbV4Rules.Operation(input.OpName, input.StandardYield, input.DurationHours);
        var top = forms[0];
        var row = new FnbItemForm { item_id = input.ItemId, seq = 0,
            name = FnbV4Rules.RequiredText(input.Name, 100, "形态名称"), unit_name = FnbV4Rules.RequiredText(input.UnitName, 40, "形态单位"),
            per_base = FnbV4Rules.Quantity(input.PerBase), storage_type = input.StorageType,
            form_code = FnbV4Rules.Code(input.FormCode, "形态编码"),
            valid = true, created_at = DateTime.UtcNow };
        ValidateForm(row);
        if (forms.Any(x => x.form_code == row.form_code)) throw Invalid("该食材的形态编码已存在");
        // 先移至不重叠区间，再整体归位；避免唯一索引在逐行 UPDATE 时撞号。
        top.in_op_name = input.OpName.Trim(); top.in_op_ratio = FnbV4Rules.Ratio(row.per_base, top.per_base);
        top.in_op_yield = input.StandardYield; top.in_op_hours = input.DurationHours;
        if (input.ShelfAfterOpDays != null) { FnbV4Rules.Days(input.ShelfAfterOpDays); top.shelf_after_op_days = input.ShelfAfterOpDays; }
        int offset = forms.Count + 1;
        foreach (var form in forms) { form.seq += offset; form.updated_at = DateTime.UtcNow; Modified(form); }
        await db.SaveChangesAsync();
        foreach (var form in forms) { form.seq -= offset - 1; Modified(form); }
        await db.SaveChangesAsync();
        db.fnbItemForm.Add(row); await db.SaveChangesAsync(); return row;
    }

    private static void ValidateForm(FnbItemForm row)
    {
        if (!FnbV4Rules.Storage(row.storage_type)) throw Invalid("储存方式无效");
        FnbV4Rules.Days(row.shelf_after_op_days); FnbV4Rules.Quantity(row.per_base);
    }
    private async Task<bool> FormReferencedAsync(int id) =>
        await db.fnbBatch.AnyAsync(x => x.form_id == id) || await db.fnbPurchaseSpec.AnyAsync(x => x.entry_form_id == id) ||
        await db.fnbStockDocumentLine.AnyAsync(x => x.form_id == id) ||
        await db.fnbStockOperation.AnyAsync(x => x.from_form_id == id || x.to_form_id == id);

    public async Task<FnbItemForm> UpdateFormAsync(FnbFormInput input)
    {
        await ItemAsync(input.ItemId); var forms = await FormsAsync(input.ItemId);
        var row = forms.SingleOrDefault(x => x.id == input.Id) ?? throw Invalid("形态不属于该食材");
        var code = FnbV4Rules.Code(input.FormCode, "形态编码");
        var name = FnbV4Rules.RequiredText(input.Name, 100, "形态名称");
        var unitName = FnbV4Rules.RequiredText(input.UnitName, 40, "形态单位");
        var quantity = FnbV4Rules.Quantity(input.PerBase);
        if (forms.Any(x => x.id != row.id && x.form_code == code)) throw Invalid("形态编码已存在");
        bool structuralChange = row.per_base != quantity || row.unit_name != unitName || row.form_code != code;
        if (row.seq == forms[^1].seq && (quantity != 1 || unitName != (await ItemAsync(input.ItemId)).base_unit_code)) throw Invalid("出品态换算固定为 1，单位固定为食材基本单位");
        if (structuralChange && await FormReferencedAsync(row.id)) throw Invalid("已有规格或库存引用的形态不能修改换算、单位或编码");
        if (row.seq == 0 && input.OpName != null) throw Invalid("最上游形态没有到达作业");
        if (row.seq > 0) FnbV4Rules.Operation(input.OpName, input.StandardYield, input.DurationHours);
        row.name = name; row.unit_name = unitName; row.per_base = quantity; row.form_code = code;
        row.storage_type = input.StorageType; row.shelf_after_op_days = input.ShelfAfterOpDays;
        ValidateForm(row);
        if (row.seq > 0) { row.in_op_name = input.OpName!.Trim(); row.in_op_yield = input.StandardYield; row.in_op_hours = input.DurationHours; }
        for (int i = 1; i < forms.Count; i++)
        {
            var ratio = FnbV4Rules.Ratio(forms[i - 1].per_base, forms[i].per_base);
            if (forms[i].in_op_ratio != ratio) { forms[i].in_op_ratio = ratio; forms[i].updated_at = DateTime.UtcNow; Modified(forms[i]); }
        }
        row.updated_at = DateTime.UtcNow; Modified(row); await db.SaveChangesAsync(); return row;
    }

    public async Task<int> RemoveFormAsync(FnbRemoveFormInput input)
    {
        await ItemAsync(input.ItemId); var forms = await FormsAsync(input.ItemId);
        if (forms.Count <= 1 || forms[0].id != input.Id) throw Invalid("只能删除最上游形态，且须保留出品态");
        if (await FormReferencedAsync(input.Id)) throw Invalid("形态已有规格、批次或作业引用，不能删除");
        var removed = forms[0]; removed.valid = false; removed.updated_at = DateTime.UtcNow; Modified(removed);
        await db.SaveChangesAsync();
        var remaining = forms.Skip(1).ToList(); int offset = forms.Count + 1;
        foreach (var row in remaining) { row.seq += offset; Modified(row); }
        await db.SaveChangesAsync();
        foreach (var row in remaining)
        {
            row.seq -= offset + 1; row.updated_at = DateTime.UtcNow;
            if (row.seq == 0) { row.in_op_name = null; row.in_op_ratio = null; row.in_op_yield = null; row.in_op_hours = null; }
            Modified(row);
        }
        await db.SaveChangesAsync(); return removed.id;
    }

    public async Task<FnbPurchaseSpec> SaveSpecAsync(FnbSpecInput input)
    {
        if (input.Id < 0) throw Invalid("规格编号无效");
        await ItemAsync(input.ItemId);
        if (!await db.fnbItemForm.AnyAsync(x => x.id == input.EntryFormId && x.item_id == input.ItemId && x.valid)) throw Invalid("入口形态不属于该食材");
        string name = FnbV4Rules.RequiredText(input.Name, 100, "规格名称");
        string? brand = FnbV4Rules.OptionalText(input.Brand, 100, "品牌"), desc = FnbV4Rules.OptionalText(input.PackDesc, 200, "包装说明"), barcode = FnbV4Rules.OptionalText(input.Barcode, 100, "条码");
        var row = input.Id == 0 ? new FnbPurchaseSpec { item_id = input.ItemId, valid = true, created_at = DateTime.UtcNow } : await db.fnbPurchaseSpec.AsNoTracking().SingleOrDefaultAsync(x => x.id == input.Id && x.item_id == input.ItemId);
        if (row == null) throw Invalid("规格不属于该食材");
        bool referenced = row.id != 0 && (await db.fnbBatch.AnyAsync(x => x.spec_id == row.id) || await db.fnbStockDocumentLine.AnyAsync(x => x.spec_id == row.id));
        if (referenced && (input.Valid || row.entry_form_id != input.EntryFormId || row.name != name || row.brand != brand || row.pack_desc != desc || row.barcode != barcode || row.sort != input.Sort))
            throw Invalid("已被入库引用的规格只能原样停用");
        if (input.Valid && barcode != null && await db.fnbPurchaseSpec.AnyAsync(x => x.id != input.Id && x.valid && x.barcode == barcode)) throw Invalid("启用中的进货规格条码须全局唯一");
        row.entry_form_id = input.EntryFormId; row.name = name; row.brand = brand; row.pack_desc = desc; row.barcode = barcode;
        row.sort = input.Sort; row.valid = input.Valid; row.updated_at = DateTime.UtcNow;
        if (row.id == 0) db.fnbPurchaseSpec.Add(row); else Modified(row);
        await db.SaveChangesAsync(); return row;
    }
}
