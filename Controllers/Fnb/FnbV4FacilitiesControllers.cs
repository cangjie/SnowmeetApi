using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SnowmeetApi.Data;
using SnowmeetApi.Models;
using SnowmeetApi.Models.Fnb;
using SnowmeetApi.Services.Fnb;
namespace SnowmeetApi.Controllers.Fnb;

[ApiController, Route("api/FnbArea/[action]")]
public sealed class FnbAreaController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListAreas(string sessionKey, int shopId, bool includeDisabled = false) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ListAreas(includeDisabled));
    [HttpPost] public Task<ApiResult<object>> SaveArea([FromQuery] string sessionKey, [FromBody] FnbAreaSaveRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SaveArea(r));
    [HttpPost] public Task<ApiResult<object>> DeleteArea([FromQuery] string sessionKey, [FromBody] FnbIdRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).DeleteArea(r.Id));
    [HttpPost] public Task<ApiResult<object>> AddPhoto([FromQuery] string sessionKey, [FromBody] FnbAreaImageRequest r) => Run(sessionKey, r.ShopId, false, true, a => new FnbV4Service(Db, r.ShopId, a).AreaImage(r, false));
    [HttpPost] public Task<ApiResult<object>> RemovePhoto([FromQuery] string sessionKey, [FromBody] FnbAreaImageRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).AreaImage(r, true));
}
[ApiController, Route("api/FnbSupply/[action]")]
public sealed class FnbSupplyController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListSupplies(string sessionKey, int shopId, string? type = null) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ListSupplies(type));
    [HttpGet] public Task<ApiResult<object>> ListLog(string sessionKey, int shopId, int supplyId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).SupplyLog(supplyId));
    [HttpPost] public Task<ApiResult<object>> SaveSupply([FromQuery] string sessionKey, [FromBody] FnbSupplySaveRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SaveSupply(r));
    [HttpPost] public Task<ApiResult<object>> PostMovement([FromQuery] string sessionKey, [FromBody] FnbSupplyPostRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "supply.post", r, a => new FnbV4Service(Db, r.ShopId, a).PostSupply(r));
    [HttpPost] public Task<ApiResult<object>> UndoReceipt([FromQuery] string sessionKey, [FromBody] FnbSupplyUndoRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "supply.undo", r, a => new FnbV4Service(Db, r.ShopId, a).UndoSupply(r));
    [HttpPost] public Task<ApiResult<object>> SaveLowStockRule([FromQuery] string sessionKey, [FromBody] FnbSupplyLowRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SupplyLow(r));
}
[ApiController, Route("api/FnbTool/[action]")]
public sealed class FnbToolController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> ListTools(string sessionKey, int shopId, string? status = null) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ListTools(status));
    [HttpGet] public Task<ApiResult<object>> ListLog(string sessionKey, int shopId, int toolId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).ToolLog(toolId));
    [HttpPost] public Task<ApiResult<object>> SaveTool([FromQuery] string sessionKey, [FromBody] FnbToolSaveRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SaveTool(r));
    [HttpPost] public Task<ApiResult<object>> ChangeStatus([FromQuery] string sessionKey, [FromBody] FnbToolChangeRequest r) => StockWrite(sessionKey, r.ShopId, r.Status == "disposed", r.RequestId, "tool.change", r, a => new FnbV4Service(Db, r.ShopId, a).ChangeTool(r));
}
[ApiController, Route("api/FnbCheck/[action]")]
public sealed class FnbCheckController(ApplicationDBContext db) : FnbV4ControllerBase(db)
{
    [HttpGet] public Task<ApiResult<object>> GetToday(string sessionKey, int shopId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).TodayCheck());
    [HttpGet] public Task<ApiResult<object>> GetSheet(string sessionKey, int shopId, long sheetId) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).CheckData(sheetId));
    [HttpGet] public Task<ApiResult<object>> ListHistory(string sessionKey, int shopId, DateTime? from = null, DateTime? to = null) => Execute(sessionKey, shopId, false, () => new FnbV4Service(Db, shopId).CheckHistory(from, to));
    [HttpPost] public Task<ApiResult<object>> SaveItem([FromQuery] string sessionKey, [FromBody] FnbCheckItemRequest r) => Run(sessionKey, r.ShopId, true, true, a => new FnbV4Service(Db, r.ShopId, a).SaveCheckItem(r));
    [HttpPost] public Task<ApiResult<object>> StartToday([FromQuery] string sessionKey, [FromBody] FnbSnapshotRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "check.start", r, a => new FnbV4Service(Db, r.ShopId, a).StartCheck());
    [HttpPost] public Task<ApiResult<object>> RefreshSnapshot([FromQuery] string sessionKey, [FromBody] FnbCheckWriteRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "check.refresh", r, a => new FnbV4Service(Db, r.ShopId, a).RefreshCheck(r.SheetId));
    [HttpPost] public Task<ApiResult<object>> SaveDraft([FromQuery] string sessionKey, [FromBody] FnbCheckDraftRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "check.save", r, a => new FnbV4Service(Db, r.ShopId, a).SaveCheck(r));
    [HttpPost] public Task<ApiResult<object>> PassAll([FromQuery] string sessionKey, [FromBody] FnbCheckWriteRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "check.pass", r, a => new FnbV4Service(Db, r.ShopId, a).PassAll(r.SheetId));
    [HttpPost] public Task<ApiResult<object>> Submit([FromQuery] string sessionKey, [FromBody] FnbCheckWriteRequest r) => StockWrite(sessionKey, r.ShopId, false, r.RequestId, "check.submit", r, a => new FnbV4Service(Db, r.ShopId, a).SubmitCheck(r.SheetId));
    [HttpPost] public Task<ApiResult<object>> HandleAbnormal([FromQuery] string sessionKey, [FromBody] FnbCheckHandleRequest r) => StockWrite(sessionKey, r.ShopId, true, r.RequestId, "check.handle", r, a => new FnbV4Service(Db, r.ShopId, a).HandleCheck(r));
    [HttpPost] public Task<ApiResult<object>> Confirm([FromQuery] string sessionKey, [FromBody] FnbCheckWriteRequest r) => StockWrite(sessionKey, r.ShopId, true, r.RequestId, "check.confirm", r, a => new FnbV4Service(Db, r.ShopId, a).ConfirmCheck(r.SheetId));
}
