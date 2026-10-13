using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SnowmeetApi.Data;
using SnowmeetApi.Services.Fnb;

namespace SnowmeetApi.Controllers.Fnb;

[ApiController]
[Route("api/[controller]/[action]")]
public sealed class FnbRouteController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    private FnbV4CatalogService Service => new(Db);
    [HttpGet]
    public Task<SnowmeetApi.Models.ApiResult<object>> ListItems(string sessionKey, int shopId, int? categoryId = null, string? keyword = null, int page = 1, int pageSize = 30) => Execute(sessionKey, shopId, false, async () =>
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue) throw new System.ArgumentException("分页参数无效");
        var q = Db.fnbItem.AsNoTracking().Where(x => x.valid);
        if (categoryId != null) q = q.Where(x => x.category_id == categoryId);
        if (!string.IsNullOrWhiteSpace(keyword)) q = q.Where(x => x.name.Contains(keyword.Trim()));
        var categories = Db.fnbCategory.AsNoTracking();
        var rows = await (from item in q join category in categories on item.category_id equals category.id
            orderby item.id select new { item, categoryName = category.name, effectiveWarnDays = item.warn_days ?? category.warn_days ?? 1,
                effectiveOpenDays = item.open_days ?? category.open_days, defaultStorage = category.default_storage,
                measureType = category.measure_type }).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new { total = await q.CountAsync(), rows };
    });
    [HttpGet]
    public Task<SnowmeetApi.Models.ApiResult<object>> GetRoute(string sessionKey, int shopId, int itemId) => Execute(sessionKey, shopId, false, () => Service.GetRouteAsync(itemId));
    [HttpPost]
    public Task<SnowmeetApi.Models.ApiResult<object>> CreateItem([FromQuery] string sessionKey, [FromBody] FnbCreateItemInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.CreateItemAsync(input));
    [HttpPost]
    public Task<SnowmeetApi.Models.ApiResult<object>> SaveItemDefaults([FromQuery] string sessionKey, [FromBody] FnbItemDefaultsInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.SaveDefaultsAsync(input));
    [HttpPost]
    public Task<SnowmeetApi.Models.ApiResult<object>> AddUpstreamForm([FromQuery] string sessionKey, [FromBody] FnbUpstreamInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.AddUpstreamAsync(input));
    [HttpPost]
    public Task<SnowmeetApi.Models.ApiResult<object>> UpdateForm([FromQuery] string sessionKey, [FromBody] FnbFormInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.UpdateFormAsync(input));
    [HttpPost]
    public Task<SnowmeetApi.Models.ApiResult<object>> RemoveForm([FromQuery] string sessionKey, [FromBody] FnbRemoveFormInput input) => Execute(sessionKey, input.ShopId, true, async () => new { id = await Service.RemoveFormAsync(input) });
    [HttpPost]
    public Task<SnowmeetApi.Models.ApiResult<object>> SaveSpec([FromQuery] string sessionKey, [FromBody] FnbSpecInput input) => Execute(sessionKey, input.ShopId, true, async () => await Service.SaveSpecAsync(input));
    [HttpGet]
    public Task<SnowmeetApi.Models.ApiResult<object>> FindSpecByBarcode(string sessionKey, int shopId, string barcode) => Execute(sessionKey, shopId, false, async () =>
    {
        var code = FnbV4Rules.RequiredText(barcode, 100, "条码");
        var spec = await Db.fnbPurchaseSpec.AsNoTracking().SingleOrDefaultAsync(x => x.valid && x.enabled && x.barcode == code);
        if (spec == null) throw new System.ArgumentException("未找到启用中的进货规格");
        var item = await (from i in Db.fnbItem.AsNoTracking()
            join c in Db.fnbCategory on i.category_id equals c.id
            join p in Db.fnbCategory on c.parent_id equals p.id
            where i.id == spec.item_id && i.enabled && c.enabled && p.enabled
            select i).SingleOrDefaultAsync() ?? throw new System.ArgumentException("食材或所属分类不存在或已停用");
        var entryForm = await Db.fnbItemForm.AsNoTracking().SingleOrDefaultAsync(x => x.id == spec.entry_form_id && x.enabled)
            ?? throw new System.ArgumentException("入口形态不存在或已停用");
        return new { spec, item, entryForm };
    });
}
