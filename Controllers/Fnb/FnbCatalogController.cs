using System;
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
public sealed class FnbCatalogController(ApplicationDBContext db) : ControllerBase
{
    private readonly FnbAccess _access = new(db);
    private static ApiResult<object> Result(int code, string message, object? data = null) => new() { code = code, message = message, data = data };
    private async Task<int> Permission(string? key, int shopId, bool manager)
    {
        var staff = await _access.ResolveAsync(key);
        return staff == null ? 2 : FnbAccess.CanAccess(staff, shopId, manager) ? 0 : 3;
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetUnits(string sessionKey, int shopId)
    {
        int p = await Permission(sessionKey, shopId, false);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "无门店权限");
        return Result(0, "", await db.fnbUnit.AsNoTracking().Where(x => x.valid).OrderBy(x => x.sort).ToListAsync());
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListCategories(string sessionKey, int shopId)
    {
        int p = await Permission(sessionKey, shopId, false);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "无门店权限");
        return Result(0, "", await db.fnbMaterialCategory.AsNoTracking().OrderBy(x => x.level).ThenBy(x => x.sort).ThenBy(x => x.id).ToListAsync());
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListShelfLifeRules(string sessionKey, int shopId, int? categoryId = null)
    {
        int p = await Permission(sessionKey, shopId, false);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "无门店权限");
        var q = db.fnbShelfLifeRule.AsNoTracking().AsQueryable();
        if (categoryId.HasValue) q = q.Where(x => x.category_id == categoryId.Value);
        return Result(0, "", await q.OrderBy(x => x.category_id).ThenBy(x => x.storage_type).ThenBy(x => x.production_month).ToListAsync());
    }

    [HttpGet]
    public async Task<ApiResult<object>> ListMaterials(string sessionKey, int shopId, int? categoryId = null, string? keyword = null, int page = 1, int pageSize = 30)
    {
        int p = await Permission(sessionKey, shopId, false);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "无门店权限");
        if (page < 1 || pageSize < 1 || pageSize > 100) return Result(1, "分页参数无效");
        var q = db.fnbMaterialItem.AsNoTracking().AsQueryable();
        if (categoryId.HasValue) q = q.Where(x => x.category_id == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(keyword)) q = q.Where(x => x.name.Contains(keyword.Trim()) || x.code.Contains(keyword.Trim()));
        int total = await q.CountAsync();
        var rows = await q.OrderBy(x => x.id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Result(0, "", new { total, rows });
    }

    [HttpGet]
    public async Task<ApiResult<object>> GetMaterial(string sessionKey, int shopId, int id)
    {
        int p = await Permission(sessionKey, shopId, false);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "无门店权限");
        var item = await db.fnbMaterialItem.AsNoTracking().FirstOrDefaultAsync(x => x.id == id);
        return item == null ? Result(1, "食材不存在") : Result(0, "", item);
    }

    public sealed record CategoryInput(int ShopId, int Id, int? ParentId, byte Level, string Name,
        string? DefaultStorage, string? DefaultUnitCode, int? WarnDays, string? DefaultOpenStorage,
        int? DefaultOpenDays, int Sort, bool Valid);

    [HttpPost]
    public async Task<ApiResult<object>> SaveCategory([FromQuery] string sessionKey, [FromBody] CategoryInput input)
    {
        int p = await Permission(sessionKey, input.ShopId, true);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "需要门店管理权限");
        if (string.IsNullOrWhiteSpace(input.Name) || !FnbText.FitsChineseVarchar(input.Name.Trim(), 100) || input.Level is not (1 or 2)) return Result(1, "分类名称或层级无效");
        if (input.Level == 1 && (input.ParentId != null || input.DefaultStorage != null || input.DefaultUnitCode != null || input.WarnDays != null || input.DefaultOpenStorage != null || input.DefaultOpenDays != null)) return Result(1, "一级分类不能设置二级默认值");
        if (input.Level == 2)
        {
            if (input.ParentId == null || !await db.fnbMaterialCategory.AnyAsync(x => x.id == input.ParentId && x.level == 1 && x.valid)) return Result(1, "父分类必须是有效一级分类");
            if (!Storage(input.DefaultStorage) || string.IsNullOrWhiteSpace(input.DefaultUnitCode) || !await db.fnbUnit.AnyAsync(x => x.code == input.DefaultUnitCode && x.valid) || input.WarnDays is null or < 0 || input.DefaultOpenDays < 0 || input.DefaultOpenStorage != null && !Storage(input.DefaultOpenStorage)) return Result(1, "二级分类默认值无效");
        }
        var row = input.Id == 0 ? new FnbMaterialCategory { created_at = DateTime.UtcNow } : await db.fnbMaterialCategory.AsTracking().FirstOrDefaultAsync(x => x.id == input.Id);
        if (row == null) return Result(1, "分类不存在");
        if (input.Id != 0 && row.level != input.Level) return Result(1, "分类层级不可修改");
        row.parent_id = input.ParentId; row.level = input.Level; row.name = input.Name.Trim();
        row.default_storage = input.DefaultStorage; row.default_unit_code = input.DefaultUnitCode;
        row.warn_days = input.WarnDays; row.default_open_storage = input.DefaultOpenStorage;
        row.default_open_days = input.DefaultOpenDays; row.sort = input.Sort; row.valid = input.Valid;
        row.updated_at = DateTime.UtcNow;
        if (input.Id == 0) db.fnbMaterialCategory.Add(row);
        await db.SaveChangesAsync();
        return Result(0, "", row);
    }

    public sealed record RuleInput(int ShopId, int Id, int CategoryId, string StorageType, byte ProductionMonth,
        int ShelfLifeValue, string ShelfLifeUnit, string? Remark, bool Valid);

    [HttpPost]
    public async Task<ApiResult<object>> SaveShelfLifeRule([FromQuery] string sessionKey, [FromBody] RuleInput input)
    {
        int p = await Permission(sessionKey, input.ShopId, true);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "需要门店管理权限");
        if (!await db.fnbMaterialCategory.AnyAsync(x => x.id == input.CategoryId && x.level == 2 && x.valid) || !Storage(input.StorageType)
            || input.ProductionMonth is < 1 or > 12 || input.ShelfLifeValue <= 0 || input.ShelfLifeUnit is not ("day" or "month")) return Result(1, "保质期规则无效");
        if (input.Valid && await db.fnbShelfLifeRule.AnyAsync(x => x.id != input.Id && x.category_id == input.CategoryId && x.storage_type == input.StorageType && x.production_month == input.ProductionMonth && x.valid)) return Result(1, "该月份已有启用规则");
        var row = input.Id == 0 ? new FnbShelfLifeRule { created_at = DateTime.UtcNow } : await db.fnbShelfLifeRule.AsTracking().FirstOrDefaultAsync(x => x.id == input.Id);
        if (row == null) return Result(1, "规则不存在");
        row.category_id = input.CategoryId; row.storage_type = input.StorageType; row.production_month = input.ProductionMonth;
        row.shelf_life_value = input.ShelfLifeValue; row.shelf_life_unit = input.ShelfLifeUnit; row.remark = input.Remark;
        row.valid = input.Valid; row.updated_at = DateTime.UtcNow;
        if (input.Id == 0) db.fnbShelfLifeRule.Add(row);
        await db.SaveChangesAsync();
        return Result(0, "", row);
    }

    public sealed record MaterialInput(int ShopId, int Id, string Code, string Name, int CategoryId, string ItemType,
        string BaseUnitCode, string DefaultInputUnitCode, int? ImageId, string? Remark, bool Valid);

    [HttpPost]
    public async Task<ApiResult<object>> SaveMaterial([FromQuery] string sessionKey, [FromBody] MaterialInput input)
    {
        int p = await Permission(sessionKey, input.ShopId, true);
        if (p != 0) return Result(p, p == 2 ? "会话失效" : "需要门店管理权限");
        if (string.IsNullOrWhiteSpace(input.Code) || !FnbText.FitsChineseVarchar(input.Code.Trim(), 64) || string.IsNullOrWhiteSpace(input.Name) || !FnbText.FitsChineseVarchar(input.Name.Trim(), 200) ||
            !FnbText.FitsChineseVarchar(input.Remark, 1000) || input.ItemType is not ("raw" or "prepared") || input.BaseUnitCode is not ("g" or "ml" or "piece")) return Result(1, "食材资料无效");
        if (!await db.fnbMaterialCategory.AnyAsync(x => x.id == input.CategoryId && x.level == 2 && x.valid)) return Result(1, "须选择有效二级分类");
        var baseUnit = await db.fnbUnit.AsNoTracking().FirstOrDefaultAsync(x => x.code == input.BaseUnitCode && x.valid);
        var inputUnit = await db.fnbUnit.AsNoTracking().FirstOrDefaultAsync(x => x.code == input.DefaultInputUnitCode && x.valid);
        if (baseUnit == null || inputUnit == null || baseUnit.dimension != inputUnit.dimension) return Result(1, "单位维度不一致");
        if (input.ImageId != null && !await db.UploadFile.AnyAsync(x => x.id == input.ImageId)) return Result(1, "食材图片不存在");
        if (await db.fnbMaterialItem.AnyAsync(x => x.id != input.Id && x.code == input.Code.Trim())) return Result(1, "食材编码已存在");
        var row = input.Id == 0 ? new FnbMaterialItem { created_at = DateTime.UtcNow } : await db.fnbMaterialItem.AsTracking().FirstOrDefaultAsync(x => x.id == input.Id);
        if (row == null) return Result(1, "食材不存在");
        if (input.Id != 0 && row.code != input.Code.Trim()) return Result(1, "食材编码创建后不可修改");
        if (input.Id != 0 && row.base_unit_code != input.BaseUnitCode && await db.fnbMaterialBatchStock.AnyAsync(x => x.item_id == input.Id)) return Result(1, "已有库存的食材不能修改基本单位");
        row.code = input.Code.Trim(); row.name = input.Name.Trim(); row.category_id = input.CategoryId;
        row.item_type = input.ItemType; row.base_unit_code = input.BaseUnitCode;
        row.default_input_unit_code = input.DefaultInputUnitCode; row.image_id = input.ImageId;
        row.remark = input.Remark; row.valid = input.Valid; row.updated_at = DateTime.UtcNow;
        if (input.Id == 0) db.fnbMaterialItem.Add(row);
        await db.SaveChangesAsync();
        return Result(0, "", row);
    }

    private static bool Storage(string? value) => value is "ambient" or "chilled" or "frozen";
}
