using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Models.Fnb;

namespace SnowmeetApi.Services.Fnb;

/// <summary>Deleting a category only marks it invalid: stock and reports still look up category names of
/// disabled materials by id. A parent is deleted together with its subcategories; any valid material
/// under the affected categories blocks the whole deletion.</summary>
public sealed class FnbCategoryService(ApplicationDBContext db)
{
    /// <summary>Only valid siblings compete for a name.</summary>
    public Task<bool> NameTakenAsync(int id, int? parentId, string name) =>
        db.fnbMaterialCategory.AnyAsync(x => x.id != id && x.valid && x.parent_id == parentId && x.name == name);

    /// <summary>The unique index on (parent_id, name) also covers deleted rows, so a deleted sibling holding
    /// the name gets a 「（已删除#id）」 suffix to free it; stock and reports still find it by id.</summary>
    public async Task FreeNameAsync(int id, int? parentId, string name)
    {
        var holders = await db.fnbMaterialCategory.AsTracking()
            .Where(x => x.id != id && !x.valid && x.parent_id == parentId && x.name == name).ToListAsync();
        if (holders.Count == 0) return;
        foreach (var x in holders)
        {
            string freed = $"{name}（已删除#{x.id}）";
            x.name = FnbText.FitsChineseVarchar(freed, 100) ? freed : $"已删除#{x.id}";
            x.updated_at = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    /// <summary>分类是否算半成品：自身标为半成品，或其一级父分类标为半成品。食材类型由此决定。</summary>
    public async Task<bool> IsPreparedAsync(int categoryId)
    {
        var row = await db.fnbMaterialCategory.AsNoTracking().FirstOrDefaultAsync(x => x.id == categoryId);
        if (row == null) return false;
        return row.is_prepared || row.parent_id != null &&
            await db.fnbMaterialCategory.AnyAsync(x => x.id == row.parent_id && x.is_prepared);
    }

    /// <summary>改分类的原料/半成品类型前检查：类型会变的二级分类里，有效食材须已经是改后的类型，
    /// 否则返回提示（食材类型不跟着分类批量改，避免已入库、已配方的食材类型被悄悄改掉）。</summary>
    public async Task<string?> TypeChangeConflictAsync(FnbMaterialCategory row, bool newPrepared)
    {
        if (row.id == 0 || row.is_prepared == newPrepared) return null;
        var changed = new List<int>();
        if (row.level == 1)
        {
            // 一级分类改类型只影响自身没标半成品的二级分类
            changed.AddRange(await db.fnbMaterialCategory.Where(x => x.parent_id == row.id && x.valid && !x.is_prepared)
                .Select(x => x.id).ToListAsync());
        }
        else if (row.parent_id == null || !await db.fnbMaterialCategory.AnyAsync(x => x.id == row.parent_id && x.is_prepared))
        {
            changed.Add(row.id);
        }
        if (changed.Count == 0) return null;
        string target = newPrepared ? "prepared" : "raw";
        int conflicts = await db.fnbMaterialItem.CountAsync(x => changed.Contains(x.category_id) && x.valid && x.item_type != target);
        if (conflicts == 0) return null;
        return newPrepared
            ? $"分类下已有 {conflicts} 种原料食材，不能改成半成品分类；可以另建一个半成品分类"
            : $"分类下已有 {conflicts} 种半成品食材，不能改成原料分类；可以另建一个原料分类";
    }

    public async Task<int[]> DeleteAsync(int id)
    {
        var row = await db.fnbMaterialCategory.AsTracking().FirstOrDefaultAsync(x => x.id == id && x.valid);
        if (row == null) throw new ArgumentException("分类不存在或已删除");
        var rows = new List<FnbMaterialCategory> { row };
        if (row.level == 1)
            rows.AddRange(await db.fnbMaterialCategory.AsTracking().Where(x => x.parent_id == id && x.valid).ToListAsync());
        int[] ids = rows.Select(x => x.id).ToArray();
        int inUse = await db.fnbMaterialItem.CountAsync(x => ids.Contains(x.category_id) && x.valid);
        if (inUse > 0) throw new ArgumentException($"该分类下还有 {inUse} 种可用食材，请先停用这些食材再删除");
        foreach (var x in rows)
        {
            x.valid = false;
            x.updated_at = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return ids;
    }
}
