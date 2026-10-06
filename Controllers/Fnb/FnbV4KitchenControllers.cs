using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;
namespace SnowmeetApi.Controllers.Fnb;

[ApiController, Route("api/FnbPrep/[action]")]
public sealed class FnbPrepController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListPreps(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ListPreps());
    [HttpGet] public Task<ApiResult<object>> ListPrepRecords(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).Records("prep"));
    [HttpGet] public Task<ApiResult<object>> PreviewPreparation(string sessionKey, int shopId, int itemId, decimal batches) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).PreviewPreparation(itemId, batches));
    [HttpPost] public Task<ApiResult<object>> CreatePrep([FromQuery] string sessionKey, [FromBody] FnbPrepCreateRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).CreatePrep(r));
    [HttpPost] public Task<ApiResult<object>> SavePrepBom([FromQuery] string sessionKey, [FromBody] FnbRecipeSaveRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SaveRecipe(r, true));
    [HttpPost] public Task<ApiResult<object>> PostPreparation([FromQuery] string sessionKey, [FromBody] FnbPreparationRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "preparation", r, a => new FnbV4Service(Db, r.ShopId, a).Prepare(r));
}
[ApiController, Route("api/FnbDish/[action]")]
public sealed class FnbDishController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListDishes(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ListDishes());
    [HttpPost] public Task<ApiResult<object>> CreateDish([FromQuery] string sessionKey, [FromBody] FnbDishCreateRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).CreateDish(r));
    [HttpPost] public Task<ApiResult<object>> AddSpec([FromQuery] string sessionKey, [FromBody] FnbDishSpecRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).AddSpec(r));
    [HttpPost] public Task<ApiResult<object>> DeleteSpec([FromQuery] string sessionKey, [FromBody] FnbSpecDisableRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).DeleteSpec(r.SpecId));
    [HttpPost] public Task<ApiResult<object>> SaveSpecLines([FromQuery] string sessionKey, [FromBody] FnbRecipeSaveRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SaveRecipe(r, false));
}
[ApiController, Route("api/FnbServe/[action]")]
public sealed class FnbServeController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListPendingOrders(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).PendingOrders());
    [HttpGet] public Task<ApiResult<object>> PreviewServe(string sessionKey, int shopId, long orderId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).PreviewServe(orderId));
    [HttpGet] public Task<ApiResult<object>> ListServeLog(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).Records("serve"));
    [HttpPost] public Task<ApiResult<object>> CreateOrder([FromQuery] string sessionKey, [FromBody] FnbOrderCreateRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "order.create", r, a => new FnbV4Service(Db, r.ShopId, a).CreateOrder(r));
    [HttpPost] public Task<ApiResult<object>> PostServe([FromQuery] string sessionKey, [FromBody] FnbServeRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "serve", r, a => new FnbV4Service(Db, r.ShopId, a).Serve(r));
}
[ApiController, Route("api/FnbStocktake/[action]")]
public sealed class FnbV4StocktakeController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> GetSnapshot(string sessionKey, int shopId, long documentId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).StocktakeData(documentId));
    [HttpPost] public Task<ApiResult<object>> CreateSnapshot([FromQuery] string sessionKey, [FromBody] FnbSnapshotRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "stocktake.snapshot", r, a => new FnbV4Service(Db, r.ShopId, a).Snapshot(r));
    [HttpPost] public Task<ApiResult<object>> SaveCount([FromQuery] string sessionKey, [FromBody] FnbCountRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "stocktake.count", r, a => new FnbV4Service(Db, r.ShopId, a).SaveCount(r));
    [HttpPost] public Task<ApiResult<object>> PostStocktake([FromQuery] string sessionKey, [FromBody] FnbDocumentRequest r) => StockWrite(sessionKey, r.ShopId, true, r.RequestId, "stocktake.post", r, a => new FnbV4Service(Db, r.ShopId, a).PostStocktake(r));
}
