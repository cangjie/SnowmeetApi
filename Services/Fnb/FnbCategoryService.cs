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
