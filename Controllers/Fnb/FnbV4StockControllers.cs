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

[ApiController, Route("api/FnbHome/[action]")]
public sealed class FnbHomeController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> GetHome(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).Home());
}
[ApiController, Route("api/FnbInbound/[action]")]
public sealed class FnbInboundController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> NextBatchNo(string sessionKey, int shopId, int itemId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).NextBatchNo(itemId));
    [HttpGet] public Task<ApiResult<object>> PreviewExpiry(string sessionKey, int shopId, int itemId, string storageType, DateTime? productionDate = null, DateTime? expireDate = null) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).PreviewExpiry(itemId, storageType, productionDate, expireDate));
    [HttpPost] public Task<ApiResult<object>> PostReceipt([FromQuery] string sessionKey, [FromBody] FnbReceiptRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "receipt", r, a => new FnbV4Service(Db, r.ShopId, a).Receipt(r));
    [HttpPost] public Task<ApiResult<object>> DeleteReceipt([FromQuery] string sessionKey, [FromBody] FnbDocumentRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "receipt.cancel", r, a => new FnbV4Service(Db, r.ShopId, a).DeleteReceipt(r));
}
[ApiController, Route("api/FnbStock/[action]")]
public sealed class FnbStockController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListStock(string sessionKey, int shopId, int? categoryId = null, string? storageType = null, string? keyword = null) => Execute(sessionKey, shopId, false, async () => await new FnbV4Service(Db, shopId).Stock(categoryId, storageType, keyword));
    [HttpGet] public Task<ApiResult<object>> GetItemLayers(string sessionKey, int shopId, int itemId) => Execute(sessionKey, shopId, false, async () => (await new FnbV4Service(Db, shopId).Stock()).SingleOrDefault(x => x.ItemId == itemId) ?? throw new ArgumentException("食材不存在"));
    [HttpGet] public Task<ApiResult<object>> GetBatch(string sessionKey, int shopId, int batchId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).GetBatch(batchId));
    [HttpGet] public Task<ApiResult<object>> ListExpiry(string sessionKey, int shopId, string? kind = null) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ListExpiry(kind));
    [HttpGet] public Task<ApiResult<object>> ListLowStock(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).LowStocks());
    [HttpGet] public Task<ApiResult<object>> ListDestroy(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, async () => new { pending = await new FnbV4Service(Db, shopId).ListExpiry("expired"),
        records = await Db.fnbStockDocument.AsNoTracking().Where(x => x.shop_id == shopId && x.document_type == "destroy" && x.status == "posted").OrderByDescending(x => x.id).Take(200).ToArrayAsync() });
    [HttpPost] public Task<ApiResult<object>> PostDestroy([FromQuery] string sessionKey, [FromBody] FnbBatchWriteRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "destroy", r, a => new FnbV4Service(Db, r.ShopId, a).Dispose(r, true));
    [HttpPost] public Task<ApiResult<object>> PostWaste([FromQuery] string sessionKey, [FromBody] FnbBatchWriteRequest r) => StockWrite(sessionKey, r.ShopId, true, r.RequestId, "waste", r, a => new FnbV4Service(Db, r.ShopId, a).Dispose(r, false));
    [HttpPost] public Task<ApiResult<object>> SaveLowStockRule([FromQuery] string sessionKey, [FromBody] FnbLowRuleRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).LowRule(r));
}
[ApiController, Route("api/FnbOperation/[action]")]
public sealed class FnbOperationController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> GetWorkbench(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).Workbench());
    [HttpGet] public Task<ApiResult<object>> PreviewOperation(string sessionKey, int shopId, int batchId, decimal quantity) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).PreviewOperation(batchId, quantity));
    [HttpPost] public Task<ApiResult<object>> PostOperation([FromQuery] string sessionKey, [FromBody] FnbOperationRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "operation", r, a => new FnbV4Service(Db, r.ShopId, a).Operation(r));
    [HttpPost] public Task<ApiResult<object>> CompleteOperation([FromQuery] string sessionKey, [FromBody] FnbCompleteOperationRequest r) => StockWrite(sessionKey, r.ShopId, true, r.RequestId, "operation.complete", r, a => new FnbV4Service(Db, r.ShopId, a).CompleteOperation(r));
}
