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
