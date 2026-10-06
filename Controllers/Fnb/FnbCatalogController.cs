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
public sealed class FnbCatalogController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    private FnbV4CatalogService Service => new(Db);
    [HttpGet]
    public Task<ApiResult<object>> ListUnits(string sessionKey, int shopId, string? measureType = null) => Execute(sessionKey, shopId, false, async () =>
    {
        byte dimension = FnbV4Rules.Dimension(measureType);
        if (measureType != null && dimension == 0) throw new ArgumentException("计量类型无效");
        return await Db.fnbUnit.AsNoTracking().Where(x => x.valid && (measureType == null || x.dimension == dimension)).OrderBy(x => x.sort).ThenBy(x => x.code).ToListAsync();
    });
    [HttpGet]
    public Task<ApiResult<object>> ListCategories(string sessionKey, int shopId, bool includeDisabled = false) => Execute(sessionKey, shopId, false, async () =>
        await Db.fnbCategory.AsNoTracking().Where(x => includeDisabled || x.valid).OrderBy(x => x.level).ThenBy(x => x.sort).ThenBy(x => x.id).ToListAsync());
    [HttpPost]
    public Task<ApiResult<object>> SaveCategory([FromQuery] string sessionKey, [FromBody] FnbCategoryInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.SaveCategoryAsync(input));
    [HttpPost]
    public Task<ApiResult<object>> DeleteCategory([FromQuery] string sessionKey, [FromBody] FnbCategoryDeleteInput input) => Execute(sessionKey, input.ShopId, true, async () => new { ids = await Service.DeleteCategoryAsync(input.Id) });
    [HttpGet]
    public Task<ApiResult<object>> ListShelfRules(string sessionKey, int shopId, int? categoryId = null, int? itemId = null, bool includeDisabled = false) => Execute(sessionKey, shopId, false, async () =>
    {
        if (categoryId != null && itemId != null) throw new ArgumentException("归属只能选食材或分类中的一个");
        var q = Db.fnbShelfLifeRule.AsNoTracking().Where(x => includeDisabled || x.valid);
        if (categoryId != null) q = q.Where(x => x.category_id == categoryId);
        if (itemId != null) q = q.Where(x => x.item_id == itemId);
        return await q.OrderBy(x => x.id).ToListAsync();
    });
    [HttpPost]
    public Task<ApiResult<object>> SaveShelfRule([FromQuery] string sessionKey, [FromBody] FnbShelfRuleInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.SaveRuleAsync(input));
}
